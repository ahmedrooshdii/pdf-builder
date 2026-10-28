using System.Text;
using System.Text.Json;
using FluentValidation;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.GeneratePdf;

public sealed class GeneratePdfCommandValidator : AbstractValidator<GeneratePdfCommand>
{
    /// <summary>Upper bound on payload size. Larger documents belong in a template that fetches its own data.</summary>
    public const int MaxPayloadBytes = 5 * 1024 * 1024;

    public GeneratePdfCommandValidator()
    {
        // The key becomes a file name, so the character set doubles as a path-traversal guard.
        RuleFor(x => x.TemplateKey)
            .NotEmpty()
            .Must(TemplateKey.IsValid)
            .WithMessage("TemplateKey may contain letters, digits, '_' and '-' (max 64 characters).");

        RuleFor(x => x.JsonPayload)
            .NotEmpty()
            .Must(p => Encoding.UTF8.GetByteCount(p) <= MaxPayloadBytes)
            .WithMessage($"Payload exceeds {MaxPayloadBytes / 1024 / 1024} MB.")
            .Must(BeAJsonObject)
            .WithMessage("Payload must be a valid JSON object.");
    }

    private static bool BeAJsonObject(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
