using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.Templates;

/// <summary>
/// What a new template starts as: small, but already showing the three things everyone needs
/// first — a value, a loop and print CSS — so it can be published straight away to try the flow.
/// </summary>
internal static class TemplateStarter
{
    public static TemplateContent Content { get; } = new(
        Html: """
              <!doctype html>
              <html lang="en">
              <head>
                <meta charset="utf-8">
                <title>{{ title }}</title>
              </head>
              <body>
                <h1>{{ title }}</h1>
                <p class="muted">Prepared for {{ customer.name }}</p>

                <ul>
                  {{ for item in items }}
                  <li>{{ item }}</li>
                  {{ end }}
                </ul>
              </body>
              </html>
              """,
        Css: """
             @page { size: A4; margin: 16mm; }

             body {
               font-family: "Segoe UI", system-ui, sans-serif;
               color: #18181b;
             }

             h1 { margin: 0 0 4px; font-size: 24px; }
             .muted { color: #71717a; }
             """,
        SampleJson: """
                    {
                      "title": "Hello from PDF Builder",
                      "customer": { "name": "Acme Trading LLC" },
                      "items": ["First line", "Second line", "Third line"]
                    }
                    """,
        Options: PdfRenderOptions.Default);
}
