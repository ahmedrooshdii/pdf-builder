using PdfBuilder.Contracts;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Api.Messaging;

/// <summary>From the wire shape to the domain one. Shared by the HTTP endpoint and the consumer.</summary>
internal static class PageSetupMapping
{
    public static PageSetup? ToDomain(this PdfPageSetup? setup) => setup is null
        ? null
        : new PageSetup
        {
            Format = setup.Format,
            Width = setup.Width,
            Height = setup.Height,
            Landscape = setup.Landscape,
            PrintBackground = setup.PrintBackground,
            PreferCssPageSize = setup.PreferCssPageSize,
            Margins = setup.Margins is { } m ? new PageMarginsSetup(m.Top, m.Right, m.Bottom, m.Left) : null
        };
}
