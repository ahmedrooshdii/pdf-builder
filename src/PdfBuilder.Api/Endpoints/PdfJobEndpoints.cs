using System.Text.Json;
using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.Identity;
using FluentValidation;
using MassTransit;
using PdfBuilder.Api.Files;
using PdfBuilder.Api.Messaging;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Application.Features.JobFiles;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Api.Endpoints;

/// <summary>HTTP front door: submit a job, then poll it.</summary>
/// <remarks>
/// Rendering takes seconds and is memory-hungry, so it does not happen on the request thread.
/// The endpoint queues the work and hands back a job id. Callers that can consume messages should
/// use the bus directly and receive <see cref="IGeneratePdfResponse"/> instead of polling.
/// </remarks>
internal static class PdfJobEndpoints
{
    public static IEndpointRouteBuilder MapPdfJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pdf/jobs")
            .WithTags("PDF jobs")
            .RequirePermission(PdfPermissions.Generate)

            // Jobs belong to a client: a key is one, the operator must choose one (feature 003).
            .RequireActingClient();

        group.MapPost("/", EnqueueAsync)
            .WithName("EnqueuePdfJob")
            .WithSummary("Queue a PDF generation job")
            .WithDescription("Returns immediately with a job id. Poll the Location header for the outcome.")
            .Produces<JobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        group.MapGet("/{jobId:guid}", GetAsync)
            .WithName("GetPdfJob")
            .WithSummary("Read the state of a queued job")
            .Produces<JobStatusResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{jobId:guid}/file-url", GetFileUrlAsync)
            .WithName("GetPdfJobFileUrl")
            .WithSummary("Get a fresh link to the PDF of a job")
            .WithDescription("A new link that works without credentials until it expires. Earlier links keep working until their own expiry. 404: no such job (or it is not yours); 409 JOB_HAS_NO_FILE: the job did not succeed; 410 FILE_EXPIRED: the file was deleted after the retention period.")
            .Produces<FileUrlResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        group.MapGet("/{jobId:guid}/file", DownloadFileAsync)
            .WithName("DownloadPdfJobFile")
            .WithSummary("Download the PDF of a job")
            .WithDescription("Served to the caller who owns the job, with their key; no link is involved. Errors as for the file-url endpoint.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<IResult> EnqueueAsync(
        GeneratePdfRequest request,
        IValidator<GeneratePdfCommand> validator,
        IPdfJobStore jobs,
        IPublishEndpoint bus,
        ICurrentClient caller,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request.Data.ValueKind != JsonValueKind.Object)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["data"] = ["Data must be a JSON object."]
            });

        var payload = request.Data.GetRawText();

        // Reuse the command's validator rather than restating its rules here, so an obviously bad
        // request is refused with a 400 instead of being queued only to fail a second later.
        var command = new GeneratePdfCommand(request.TemplateKey ?? string.Empty, payload, request.PageSetup.ToDomain());
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var jobId = NewId.NextGuid();   // sequential GUID: index-friendly in the jobs table

        // Publish first, then save. With the bus outbox the publish only queues the message in the
        // database context, and the save commits it together with the job, in one transaction. So a
        // worker cannot start before the job exists (it would otherwise overwrite a Pending row that
        // was written after it finished), and a failure of either leaves neither a job nobody will
        // work on nor a message for a job nobody can poll.
        await bus.Publish<IGeneratePdfRequest>(new
        {
            CorrelationId = jobId,
            command.TemplateKey,
            JsonPayload = payload,
            request.PageSetup,

            // The worker renders with this client's templates and records the job under it, exactly as the
            // job below is recorded: the message and the job name the same owner.
            caller.ClientId
        }, ct);

        await jobs.SaveAsync(PdfJob.Enqueued(jobId, command.TemplateKey, clock.GetUtcNow()), ct);

        return Results.Accepted(
            uri: $"/api/pdf/jobs/{jobId}",
            value: new JobAcceptedResponse(jobId, $"/api/pdf/jobs/{jobId}"));
    }

    private static async Task<IResult> GetAsync(Guid jobId, JobFiles files, PdfFileLinks links, CancellationToken ct)
    {
        var job = await files.GetJobAsync(jobId, ct);

        return job is null
            ? Results.NotFound()
            : Results.Ok(JobStatusResponse.From(job, links));
    }

    private static async Task<IResult> GetFileUrlAsync(
        Guid jobId, HttpContext http, JobFiles files, PdfFileLinks links, CancellationToken ct)
    {
        var fileId = await files.RequireFileAsync(jobId, ct);
        if (fileId is null)
            return Results.NotFound();

        var link = links.For(fileId);
        http.Response.Headers.CacheControl = "no-store";     // every call makes a new link; none is worth keeping
        return Results.Ok(new FileUrlResponse(link.Url, link.ExpiresAt));
    }

    private static async Task<IResult> DownloadFileAsync(Guid jobId, HttpContext http, JobFiles files, CancellationToken ct)
    {
        var file = await files.OpenFileAsync(jobId, ct);

        return file is null
            ? Results.NotFound()
            : PdfFileResponses.Pdf(http, file.Content, file.FileId);
    }
}

/// <summary>Body of <c>POST /api/pdf/jobs</c>.</summary>
/// <param name="TemplateKey">Id of a registered template, for example <c>invoice</c>.</param>
/// <param name="Data">A JSON object bound to the template.</param>
/// <param name="PageSetup">Optional: paper size, orientation or margins for this document only.</param>
internal sealed record GeneratePdfRequest(string? TemplateKey, JsonElement Data, PdfPageSetup? PageSetup = null);

internal sealed record JobAcceptedResponse(Guid JobId, string StatusUrl);

/// <param name="FileUrl">A link that works without credentials, made for this response. Null unless the job succeeded and its file is still kept.</param>
/// <param name="FileUrlExpiresAt">When <paramref name="FileUrl"/> stops working. Null when the deployment does not sign links, and when there is no link.</param>
/// <param name="FileExpired">The file was deleted after the retention period, or is gone from storage.</param>
internal sealed record JobStatusResponse(
    Guid JobId,
    string TemplateKey,
    string Status,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? CompletedAt,
    string? FileId,
    string? FileUrl,
    DateTimeOffset? FileUrlExpiresAt,
    bool FileExpired,
    long? SizeBytes,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static JobStatusResponse From(PdfJob job, PdfFileLinks links)
    {
        FileLink? link = job is { Status: PdfJobStatus.Succeeded, FileExpired: false, FileId: { } fileId }
            ? links.For(fileId)
            : null;

        return new(
            job.JobId,
            job.TemplateKey,
            job.Status.ToString(),
            job.EnqueuedAt,
            job.CompletedAt,
            job.FileId,
            link?.Url,
            link?.ExpiresAt,
            job.FileExpired,
            job.SizeBytes,
            job.ErrorCode,
            job.ErrorMessage);
    }
}

/// <summary>Body of <c>GET /api/pdf/jobs/{id}/file-url</c>.</summary>
/// <param name="FileUrl">A link that works without credentials until <paramref name="FileUrlExpiresAt"/>.</param>
/// <param name="FileUrlExpiresAt">Null when the deployment does not sign links.</param>
internal sealed record FileUrlResponse(string FileUrl, DateTimeOffset? FileUrlExpiresAt);
