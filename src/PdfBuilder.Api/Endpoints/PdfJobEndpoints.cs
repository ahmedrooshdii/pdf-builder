using System.Text.Json;
using FluentValidation;
using MassTransit;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
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
        var group = app.MapGroup("/api/pdf/jobs").WithTags("PDF jobs");

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

        return app;
    }

    private static async Task<IResult> EnqueueAsync(
        GeneratePdfRequest request,
        IValidator<GeneratePdfCommand> validator,
        IPdfJobStore jobs,
        IPublishEndpoint bus,
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
        var command = new GeneratePdfCommand(request.TemplateId ?? string.Empty, payload);
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var jobId = NewId.NextGuid();   // sequential GUID: index-friendly if this ever reaches a database

        // Recorded before publishing, so a fast worker cannot finish the job and have its result
        // overwritten by a Pending entry written afterwards.
        await jobs.SaveAsync(PdfJob.Enqueued(jobId, command.TemplateId, clock.GetUtcNow()), ct);

        await bus.Publish<IGeneratePdfRequest>(new
        {
            CorrelationId = jobId,
            command.TemplateId,
            JsonPayload = payload
        }, ct);

        return Results.Accepted(
            uri: $"/api/pdf/jobs/{jobId}",
            value: new JobAcceptedResponse(jobId, $"/api/pdf/jobs/{jobId}"));
    }

    private static async Task<IResult> GetAsync(Guid jobId, IPdfJobStore jobs, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);

        return job is null
            ? Results.NotFound()
            : Results.Ok(JobStatusResponse.From(job));
    }
}

/// <summary>Body of <c>POST /api/pdf/jobs</c>.</summary>
/// <param name="TemplateId">Id of a registered template, for example <c>invoice</c>.</param>
/// <param name="Data">A JSON object bound to the template.</param>
internal sealed record GeneratePdfRequest(string? TemplateId, JsonElement Data);

internal sealed record JobAcceptedResponse(Guid JobId, string StatusUrl);

internal sealed record JobStatusResponse(
    Guid JobId,
    string TemplateId,
    string Status,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? CompletedAt,
    string? FileId,
    string? FileUrl,
    long? SizeBytes,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static JobStatusResponse From(PdfJob job) => new(
        job.JobId,
        job.TemplateId,
        job.Status.ToString(),
        job.EnqueuedAt,
        job.CompletedAt,
        job.FileId,
        job.FileUrl,
        job.SizeBytes,
        job.ErrorCode,
        job.ErrorMessage);
}
