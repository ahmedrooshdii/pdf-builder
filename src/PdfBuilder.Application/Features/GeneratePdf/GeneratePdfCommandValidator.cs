using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;

namespace PdfBuilder.Application.Features.GeneratePdf;

public sealed partial class GeneratePdfCommandValidator : AbstractValidator<GeneratePdfCommand>
{
    /// <summary>Upper bound on payload size. Larger documents belong in a template that fetches its own data.</summary>
    public const int MaxPayloadBytes = 5 * 1024 * 1024;

    public GeneratePdfCommandValidator()
    {
        // The id becomes a file name, so the character set doubles as a path-traversal guard.
        RuleFor(x => x.TemplateId)
            .NotEmpty()
            .Matches(TemplateIdPattern())
            .WithMessage("TemplateId may contain letters, digits, '_' and '-' (max 64 characters).");

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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex TemplateIdPattern();
}
