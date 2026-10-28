using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;

namespace PdfBuilder.Application.Features.GeneratePdf;

/// <summary>
/// The one use case this service exists for: look up a template, bind data to it, print it, store it.
/// Every step is a port, so this is the only place that order is written down.
/// </summary>
public sealed partial class GeneratePdfCommandHandler(
    IPdfTemplateRepository templates,
    ITemplateEngine templateEngine,
    IPdfRenderer renderer,
    IFileStorage storage,
    ILogger<GeneratePdfCommandHandler> logger)
    : IRequestHandler<GeneratePdfCommand, GeneratePdfResult>
{
    public async Task<GeneratePdfResult> Handle(GeneratePdfCommand request, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var template = await templates.GetByKeyAsync(request.TemplateKey, cancellationToken)
                       ?? throw new TemplateNotFoundException(request.TemplateKey);

        var html = await templateEngine.RenderAsync(template.Html, request.JsonPayload, cancellationToken);
        var pdf = await renderer.RenderAsync(html, template.Options, cancellationToken);
        var stored = await storage.SaveAsync(pdf, "application/pdf", cancellationToken);

        var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LogGenerated(logger, stored.FileId, request.TemplateKey, stored.SizeBytes, elapsedMs);

        return new GeneratePdfResult(stored.FileId, stored.Url, stored.SizeBytes);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Generated PDF {FileId} from template {TemplateKey} ({SizeBytes} bytes) in {ElapsedMs} ms")]
    private static partial void LogGenerated(
        ILogger logger, string fileId, string templateKey, long sizeBytes, long elapsedMs);
}
