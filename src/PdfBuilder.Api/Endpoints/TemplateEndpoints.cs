using MediatR;
using PdfBuilder.Application.Features.Templates;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Api.Endpoints;

/// <summary>
/// Template authoring: drafts that can be edited freely, and a publish step that decides what jobs
/// render. Failures map to status codes in <see cref="ApiExceptionHandler"/>.
/// </summary>
internal static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/templates").WithTags("Templates");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListTemplatesQuery(), ct)).Select(TemplateSummaryResponse.From))
            .WithName("ListTemplates")
            .WithSummary("List built-in and authored templates");

        group.MapGet("/{templateKey}", async (string templateKey, ISender sender, CancellationToken ct) =>
                TemplateResponse.From(await sender.Send(new GetTemplateQuery(templateKey), ct)))
            .WithName("GetTemplate")
            .WithSummary("Read a template's draft and published content")
            .Produces<TemplateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateTemplateRequest request, ISender sender, CancellationToken ct) =>
            {
                var view = await sender.Send(
                    new CreateTemplateCommand(request.TemplateKey ?? string.Empty, request.Content?.ToDomain()), ct);
                return Results.Created($"/api/templates/{view.Key}", TemplateResponse.From(view));
            })
            .WithName("CreateTemplate")
            .WithSummary("Start a new draft template")
            .WithDescription("Without content, the draft starts from a small working example.")
            .Produces<TemplateResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{templateKey}/duplicate", async (
                string templateKey, DuplicateTemplateRequest request, ISender sender, CancellationToken ct) =>
            {
                var view = await sender.Send(
                    new DuplicateTemplateCommand(templateKey, request.TemplateKey ?? string.Empty), ct);
                return Results.Created($"/api/templates/{view.Key}", TemplateResponse.From(view));
            })
            .WithName("DuplicateTemplate")
            .WithSummary("Copy a template's draft into a new draft")
            .Produces<TemplateResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{templateKey}/draft", async (
                string templateKey, SaveDraftRequest request, ISender sender, CancellationToken ct) =>
                TemplateResponse.From(await sender.Send(
                    new SaveTemplateDraftCommand(templateKey, request.Revision, request.Content.ToDomain()), ct)))
            .WithName("SaveTemplateDraft")
            .WithSummary("Replace a template's draft")
            .WithDescription("Send the revision the edit was based on. A stale one is refused with 409.")
            .Produces<TemplateResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{templateKey}/publish", async (
                string templateKey, PublishTemplateRequest request, ISender sender, CancellationToken ct) =>
                TemplateResponse.From(await sender.Send(new PublishTemplateCommand(templateKey, request.Revision), ct)))
            .WithName("PublishTemplate")
            .WithSummary("Make the draft what jobs render")
            .WithDescription("The draft is rendered with its sample data first; one that fails is refused with 422.")
            .Produces<TemplateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{templateKey}", async (string templateKey, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteTemplateCommand(templateKey), ct);
                return Results.NoContent();
            })
            .WithName("DeleteTemplate")
            .WithSummary("Delete an authored template")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/preview", async (PreviewTemplateRequest request, ISender sender, CancellationToken ct) =>
                Results.Content(
                    await sender.Send(new PreviewTemplateCommand(request.Content.ToDomain()), ct),
                    "text/html; charset=utf-8"))
            .WithName("PreviewTemplate")
            .WithSummary("Render HTML, CSS and sample data to HTML, without saving or printing")
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}

/// <summary>Template content on the wire. Every member is optional on input and defaults to empty.</summary>
internal sealed record TemplateContentDto(string? Html, string? Css, string? SampleJson, PdfRenderOptions? Options)
{
    public TemplateContent ToDomain() =>
        new(Html ?? string.Empty, Css ?? string.Empty, SampleJson ?? string.Empty, Options ?? PdfRenderOptions.Default);

    public static TemplateContentDto From(TemplateContent content) =>
        new(content.Html, content.Css, content.SampleJson, content.Options);
}

internal sealed record CreateTemplateRequest(string? TemplateKey, TemplateContentDto? Content);

internal sealed record DuplicateTemplateRequest(string? TemplateKey);

internal sealed record SaveDraftRequest(int Revision, TemplateContentDto Content);

internal sealed record PublishTemplateRequest(int Revision);

internal sealed record PreviewTemplateRequest(TemplateContentDto Content);

internal sealed record TemplateSummaryResponse(
    string TemplateKey,
    string Source,
    string Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt)
{
    public static TemplateSummaryResponse From(TemplateSummary summary) => new(
        summary.Key,
        summary.Source.ToString(),
        summary.Status.ToString(),
        summary.Revision,
        summary.UpdatedAt,
        summary.PublishedAt);
}

internal sealed record TemplateResponse(
    string TemplateKey,
    string Source,
    string Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    TemplateContentDto Draft,
    TemplateContentDto? Published)
{
    public static TemplateResponse From(TemplateView view) => new(
        view.Key,
        view.Source.ToString(),
        view.Status.ToString(),
        view.Revision,
        view.UpdatedAt,
        view.PublishedAt,
        TemplateContentDto.From(view.Draft),
        view.Published is null ? null : TemplateContentDto.From(view.Published));
}
