using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Rendering;

public sealed class PlaywrightOptions
{
    public const string Section = "Playwright";

    /// <summary>
    /// How many pages may render at once. Each one costs roughly a browser tab's worth of memory,
    /// so this is the main lever for sizing the container. Also caps the messaging prefetch.
    /// </summary>
    [Range(1, 64)]
    public int MaxConcurrentRenders { get; init; } = 4;

    /// <summary>Recycle Chromium after this many renders, to bound the memory it accumulates.</summary>
    [Range(1, 100_000)]
    public int MaxRendersPerBrowser { get; init; } = 500;

    [Range(1_000, 300_000)]
    public int RenderTimeoutMs { get; init; } = 30_000;

    /// <summary>
    /// Off by default. Payload data is untrusted and Scriban does not auto-escape HTML, so a
    /// crafted payload could otherwise run script inside the rendering browser.
    /// </summary>
    public bool EnableJavaScript { get; init; }

    /// <summary>
    /// Hosts a template may load fonts, images or stylesheets from. Everything else is blocked,
    /// which stops a template from being used to probe the internal network. Empty means
    /// "inline assets only" - the safest setting, and the default.
    /// </summary>
    public string[] AllowedHosts { get; init; } = [];

    /// <summary>
    /// Chromium command-line flags. The defaults matter in containers: <c>/dev/shm</c> is 64 MB by
    /// default under Docker, which crashes the renderer on large pages.
    /// </summary>
    public string[] LaunchArgs { get; init; } = ["--disable-dev-shm-usage", "--disable-gpu"];
}
