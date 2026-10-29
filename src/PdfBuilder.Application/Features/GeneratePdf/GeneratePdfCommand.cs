using MediatR;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.GeneratePdf;

/// <summary>Render <paramref name="TemplateKey"/> bound to <paramref name="JsonPayload"/> and store the result.</summary>
/// <param name="PageSetup">Adjustments to the template's page setup for this job only. Null keeps the template's.</param>
public sealed record GeneratePdfCommand(string TemplateKey, string JsonPayload, PageSetup? PageSetup = null)
    : IRequest<GeneratePdfResult>;

/// <summary>Where the rendered PDF ended up.</summary>
public sealed record GeneratePdfResult(string FileId, string FileUrl, long SizeBytes);
