using System.Collections.Concurrent;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Api.Tests.Support;

/// <summary>
/// Stands in for the browser: remembers the HTML each job was rendered from, so a test can say which
/// template a job used, and returns the smallest thing that looks like a PDF. Register it with
/// <c>ApiFactory.StartAsync(postgres, services => services.AddSingleton&lt;IPdfRenderer&gt;(renderer))</c>.
/// </summary>
public sealed class RecordingRenderer : IPdfRenderer
{
    private readonly ConcurrentQueue<string> _html = new();

    /// <summary>The HTML of every render so far, in order.</summary>
    public IReadOnlyList<string> Html => [.. _html];

    public Task<byte[]> RenderAsync(string html, PdfRenderOptions renderOptions, CancellationToken cancellationToken = default)
    {
        _html.Enqueue(html);
        return Task.FromResult<byte[]>([0x25, 0x50, 0x44, 0x46]);
    }
}
