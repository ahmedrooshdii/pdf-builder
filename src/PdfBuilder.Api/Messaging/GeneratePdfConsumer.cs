using FluentValidation;
using MassTransit;
using MediatR;
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
    TimeProvider clock,
    ILogger<GeneratePdfConsumer> logger)
    : IConsumer<IGeneratePdfRequest>
{
    public async Task Consume(ConsumeContext<IGeneratePdfRequest> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["TemplateKey"] = message.TemplateKey
        });

        try
        {
            var result = await sender.Send(
                new GeneratePdfCommand(message.TemplateKey, message.JsonPayload), ct);

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

            await CompleteAsync(context, job => job.Fail(errorCode, errorMessage, clock.GetUtcNow()));

            await context.RespondAsync<IGeneratePdfResponse>(new
            {
                message.CorrelationId,
                IsSuccess = false,
                FileId = (string?)null,
                FileUrl = (string?)null,
                SizeBytes = (long?)null,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                CompletedAt = clock.GetUtcNow()
            });
        }

        // Everything else - a browser crash, a timeout, a disk hiccup - is transient and is left to
        // bubble. MassTransit redelivers it per GeneratePdfConsumerDefinition, and once the retries
        // are spent the message goes to the _error queue and the requester receives a Fault<T>.
    }

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
