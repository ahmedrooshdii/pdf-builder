using FluentValidation;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.GeneratePdf;

/// <summary>A template's full page setup, as saved with the template.</summary>
public sealed class PdfRenderOptionsValidator : AbstractValidator<PdfRenderOptions>
{
    public PdfRenderOptionsValidator()
    {
        // Required even with a custom size: it is what the page falls back to if the size is cleared.
        RuleFor(x => x.Format).NotEmpty().PaperFormat();

        RuleFor(x => x.Width).PageLength().When(x => x.Width is not null);
        RuleFor(x => x.Height).PageHeight().When(x => x.Height is not null);
        RuleFor(x => x)
            .Must(x => (x.Width is null) == (x.Height is null))
            .WithName("Height")
            .WithMessage("A custom page size needs both a width and a height.");

        RuleFor(x => x.Margins).NotNull();
        RuleFor(x => x.Margins.Top).MarginLength().When(x => x.Margins is not null);
        RuleFor(x => x.Margins.Right).MarginLength().When(x => x.Margins is not null);
        RuleFor(x => x.Margins.Bottom).MarginLength().When(x => x.Margins is not null);
        RuleFor(x => x.Margins.Left).MarginLength().When(x => x.Margins is not null);
    }
}

/// <summary>One request's adjustments to the template's page setup.</summary>
public sealed class PageSetupValidator : AbstractValidator<PageSetup>
{
    public PageSetupValidator()
    {
        RuleFor(x => x.Format).PaperFormat().When(x => x.Format is not null);
        RuleFor(x => x.Width).PageLength().When(x => x.Width is not null);
        RuleFor(x => x.Height).PageHeight().When(x => x.Height is not null);

        RuleFor(x => x)
            .Must(x => (x.Width is null) == (x.Height is null))
            .WithName("Height")
            .WithMessage("A custom page size needs both a width and a height.");
        RuleFor(x => x)
            .Must(x => x.Format is null || x.Width is null)
            .WithName("Format")
            .WithMessage("Give either a format or a width and height, not both.");

        RuleFor(x => x.Margins!.Top).MarginLength().When(x => x.Margins?.Top is not null);
        RuleFor(x => x.Margins!.Right).MarginLength().When(x => x.Margins?.Right is not null);
        RuleFor(x => x.Margins!.Bottom).MarginLength().When(x => x.Margins?.Bottom is not null);
        RuleFor(x => x.Margins!.Left).MarginLength().When(x => x.Margins?.Left is not null);
    }
}

internal static class PageSetupRules
{
    public static IRuleBuilderOptions<T, string?> PaperFormat<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PageSize.IsKnownFormat).WithMessage($"Format must be {PageSize.FormatRule}.");

    public static IRuleBuilderOptions<T, string?> PageLength<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PageSize.IsValidPageLength).WithMessage($"'{{PropertyName}}' must be {PageSize.LengthRule}, above zero.");

    public static IRuleBuilderOptions<T, string?> PageHeight<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PageSize.IsValidPageHeight).WithMessage($"'{{PropertyName}}' must be {PageSize.HeightRule}.");

    public static IRuleBuilderOptions<T, string?> MarginLength<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PageSize.IsValidLength).WithMessage($"'{{PropertyName}}' must be {PageSize.LengthRule}.");
}
