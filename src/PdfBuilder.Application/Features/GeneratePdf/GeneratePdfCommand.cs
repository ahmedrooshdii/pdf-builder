using MediatR;

namespace PdfBuilder.Application.Features.GeneratePdf;

/// <summary>Render <paramref name="TemplateId"/> bound to <paramref name="JsonPayload"/> and store the result.</summary>
public sealed record GeneratePdfCommand(string TemplateId, string JsonPayload) : IRequest<GeneratePdfResult>;

/// <summary>Where the rendered PDF ended up.</summary>
public sealed record GeneratePdfResult(string FileId, string FileUrl, long SizeBytes);
