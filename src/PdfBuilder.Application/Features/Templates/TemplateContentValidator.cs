using System.Text;
using System.Text.Json;
using FluentValidation;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.Templates;

/// <summary>Limits on what a template may contain. Shared by every command that carries content.</summary>
public sealed class TemplateContentValidator : AbstractValidator<TemplateContent>
{
    public const int MaxHtmlBytes = 1024 * 1024;
    public const int MaxCssBytes = 512 * 1024;

    public TemplateContentValidator()
    {
        RuleFor(x => x.Html)
            .NotEmpty().WithMessage("The template needs some HTML.")
            .Must(html => Encoding.UTF8.GetByteCount(html) <= MaxHtmlBytes)
            .WithMessage($"HTML exceeds {MaxHtmlBytes / 1024} KB.");

        RuleFor(x => x.Css)
            .NotNull()
            .Must(css => Encoding.UTF8.GetByteCount(css) <= MaxCssBytes)
            .WithMessage($"CSS exceeds {MaxCssBytes / 1024} KB.");

        RuleFor(x => x.SampleJson)
            .NotNull()
            .Must(json => Encoding.UTF8.GetByteCount(json) <= GeneratePdfCommandValidator.MaxPayloadBytes)
            .WithMessage($"Sample data exceeds {GeneratePdfCommandValidator.MaxPayloadBytes / 1024 / 1024} MB.")
            .Must(BeEmptyOrAJsonObject)
            .WithMessage("Sample data must be a JSON object, or empty.");

        RuleFor(x => x.Options).NotNull();
        RuleFor(x => x.Options.Format).NotEmpty().When(x => x.Options is not null);
    }

    private static bool BeEmptyOrAJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal static class TemplateKeyRules
{
    public static IRuleBuilderOptions<T, string> ValidTemplateKey<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Must(TemplateKey.IsValid)
            .WithMessage($"A template key may contain {TemplateKey.Rule}.");
}
