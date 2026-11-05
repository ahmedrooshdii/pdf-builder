using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Identity;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Api.Messaging;

/// <summary>
/// The worker side of the service: takes a request off RabbitMQ, runs the use case, and records
/// the outcome twice - as a response message for bus callers, and in the job store for HTTP callers.
/// </summary>
public sealed class GeneratePdfConsumer(
    ISender sender,
    IPdfJobStore jobs,
    IClientContext clientContext,
    IClientKeyStore clients,
    IOptions<BlocksBusOptions> bus,
    TimeProvider clock,
    ILogger<GeneratePdfConsumer> logger)
    : IConsumer<IGeneratePdfRequest>
{
    public async Task Consume(ConsumeContext<IGeneratePdfRequest> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        // The client this job is for: the one the message names, or the deployment's default for a publisher
        // that predates the member. It is the first thing resolved, because everything after it (the stores,
        // the template lookup) is confined to this client.
        var clientId = message.ClientId ?? bus.Value.DefaultClientId;

        var scope = new Dictionary<string, object>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["TemplateKey"] = message.TemplateKey
        };
        // Only a well-formed id goes into the log: a malformed one could be anything the publisher sent.
        if (ClientIds.IsValid(clientId))
            scope["ClientId"] = clientId!;
        using var logScope = logger.BeginScope(scope);

        if (clientId is null)
        {
            // No client could own the job, so none is recorded, and nothing is rendered.
            await RespondFailureAsync(context, PdfErrorCodes.ValidationFailed,
                "The message names no client and no default client is configured.");
            return;
        }

        // Who the job is for must be a client that exists and is enabled, as for a caller over HTTP. Nothing is
        // recorded or rendered for one that is not: there is no owner to record it under.
        if (await RefuseUnusableClientAsync(context, clientId, ct))
            return;

        clientContext.ActAs(clientId);

        try
        {
            // Job ids are unique across clients, so an id that another client used is refused here, before anything
            // is rendered, and that client's job is neither read nor changed. A job that came over HTTP already
            // exists for this client; one published straight to the bus is recorded now, as Pending.
            await jobs.ClaimAsync(message.CorrelationId, message.TemplateKey, ct);

            var result = await sender.Send(
                new GeneratePdfCommand(message.TemplateKey, message.JsonPayload, message.PageSetup.ToDomain()), ct);

            await CompleteAsync(context, job => job.Succeed(
                result.FileId, result.FileUrl, result.SizeBytes, clock.GetUtcNow()));

            await context.RespondAsync<IGeneratePdfResponse>(new
            {
                message.CorrelationId,
                IsSuccess = true,
                result.FileId,
                result.FileUrl,
                SizeBytes = (long?)result.SizeBytes,
                ErrorCode = (string?)null,
                ErrorMessage = (string?)null,
                CompletedAt = clock.GetUtcNow()
            });
        }
        catch (Exception ex) when (IsPermanent(ex))
        {
            // Redelivering cannot change the answer, so report it now rather than burning retries.
            var (errorCode, errorMessage) = Describe(ex);
            ConsumerLog.RequestRejected(logger, ex, errorCode);

            // Not for an id that belongs to another client: that job is not this message's to close.
            if (ex is not JobIdTakenException)
                await CompleteAsync(context, job => job.Fail(errorCode, errorMessage, clock.GetUtcNow()));

            await RespondFailureAsync(context, errorCode, errorMessage);
        }

        catch (Exception ex) when (context.GetRetryAttempt() >= GeneratePdfConsumerDefinition.RetryLimit)
        {
            // The last attempt of a transient failure. Close the tracked job so an HTTP caller
            // polling it sees the failure instead of Pending forever, then rethrow so the message
            // still goes to _error and a bus requester still receives its Fault<T>.
            var (errorCode, errorMessage) = Describe(ex);
            await CompleteAsync(context, job => job.Fail(errorCode, errorMessage, clock.GetUtcNow()));
            throw;
        }

        // Earlier attempts of a transient failure - a browser crash, a timeout, a disk hiccup - are
        // left to bubble, and MassTransit redelivers them per GeneratePdfConsumerDefinition.
    }

    /// <summary>
    /// Answers the publisher and returns true when <paramref name="clientId"/> cannot have work done for it: it is
    /// not a client id, no such client is registered, or the client is disabled.
    /// </summary>
    /// <remarks>A storage outage is not a refusal: it bubbles, and the message is redelivered.</remarks>
    private async Task<bool> RefuseUnusableClientAsync(
        ConsumeContext<IGeneratePdfRequest> context, string clientId, CancellationToken cancellationToken)
    {
        string? code = null;
        string? message = null;

        if (!ClientIds.IsValid(clientId))
        {
            (code, message) = (PdfErrorCodes.ValidationFailed, "ClientId is not a valid client id.");
        }
        else if (await clients.GetClientAsync(clientId, cancellationToken) is not { } client)
        {
            (code, message) = (PdfErrorCodes.ValidationFailed, $"Client '{clientId}' is not registered.");
        }
        else if (client.IsDisabled)
        {
            (code, message) = (PdfErrorCodes.ClientDisabled, $"Client '{clientId}' is disabled.");
        }

        if (code is null)
            return false;

        ConsumerLog.ClientRefused(logger, code);
        await RespondFailureAsync(context, code, message!);
        return true;
    }

    private async Task RespondFailureAsync(ConsumeContext<IGeneratePdfRequest> context, string errorCode, string errorMessage) =>
        await context.RespondAsync<IGeneratePdfResponse>(new
        {
            context.Message.CorrelationId,
            IsSuccess = false,
            FileId = (string?)null,
            FileUrl = (string?)null,
            SizeBytes = (long?)null,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CompletedAt = clock.GetUtcNow()
        });

    /// <summary>
    /// Permanent means the request is wrong, not that the system is having a bad moment.
    /// <see cref="PdfBuilderException.IsTransient"/> carries that decision; validation failures
    /// come from the MediatR pipeline and are permanent by nature.
    /// </summary>
    private static bool IsPermanent(Exception exception) => exception switch
    {
        PdfBuilderException domain => !domain.IsTransient,
        ValidationException => true,
        _ => false
    };

    private static (string ErrorCode, string ErrorMessage) Describe(Exception exception) => exception switch
    {
        PdfBuilderException domain => (domain.ErrorCode, domain.Message),
        ValidationException validation => (
            PdfErrorCodes.ValidationFailed,
            string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))),
        _ => (PdfErrorCodes.RenderFailed, exception.Message)
    };

    /// <summary>
    /// Updates the tracked job, tolerating the case where there is none: a producer may publish
    /// straight to RabbitMQ without going through this service's HTTP endpoint.
    /// </summary>
    private async Task CompleteAsync(
        ConsumeContext<IGeneratePdfRequest> context,
        Func<PdfJob, PdfJob> transition)
    {
        var message = context.Message;
        var existing = await jobs.GetAsync(message.CorrelationId, context.CancellationToken)
                       ?? PdfJob.Enqueued(message.CorrelationId, message.TemplateKey, clock.GetUtcNow());

        await jobs.SaveAsync(transition(existing), context.CancellationToken);
    }
}
