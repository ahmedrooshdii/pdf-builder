using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Rendering;

/// <summary>
/// Prints HTML with headless Chromium.
/// </summary>
/// <remarks>
/// Registered as a singleton because it owns the browser process. Concurrency is bounded two ways:
/// <see cref="PlaywrightOptions.MaxConcurrentRenders"/> limits pages in flight, and the browser
/// itself is recycled after <see cref="PlaywrightOptions.MaxRendersPerBrowser"/> renders. Every
/// render gets its own <see cref="IBrowserContext"/>, which is Playwright's isolation boundary -
/// one job can never see another's cookies, storage or cache.
/// </remarks>
public sealed class PlaywrightPdfRenderer : IPdfRenderer, IAsyncDisposable
{
    private readonly PlaywrightOptions _options;
    private readonly ILogger<PlaywrightPdfRenderer> _logger;
    private readonly SemaphoreSlim _renderSlots;

    /// <summary>Serialises launch and recycle only, never the renders themselves.</summary>
    private readonly SemaphoreSlim _browserGate = new(1, 1);

    private IPlaywright? _playwright;
    private BrowserLease? _currentBrowser;
    private volatile bool _disposed;

    public PlaywrightPdfRenderer(IOptions<PlaywrightOptions> options, ILogger<PlaywrightPdfRenderer> logger)
    {
        _options = options.Value;
        _logger = logger;
        _renderSlots = new SemaphoreSlim(_options.MaxConcurrentRenders);
    }

    public async Task<byte[]> RenderAsync(string html, PdfRenderOptions renderOptions, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _renderSlots.WaitAsync(cancellationToken);

        BrowserLease? lease = null;
        IBrowserContext? context = null;
        try
        {
            lease = await AcquireBrowserAsync(cancellationToken);
            context = await CreateContextAsync(lease.Browser);

            var page = await context.NewPageAsync();
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });

            // Playwright takes no CancellationToken. WaitAsync releases the caller promptly, and the
            // context teardown in `finally` aborts the work the browser is still doing.
            return await page.PdfAsync(ToPagePdfOptions(renderOptions)).WaitAsync(cancellationToken);
        }
        catch (PlaywrightException ex)   // covers Playwright's own TimeoutException
        {
            throw new PdfRenderException($"Chromium failed to render the PDF: {ex.Message}", ex);
        }
        finally
        {
            await CloseQuietlyAsync(context);

            if (lease is not null)
                await lease.ReleaseAsync();

            _renderSlots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _browserGate.WaitAsync();
        try
        {
            if (_currentBrowser is not null)
                await _currentBrowser.RetireAsync();

            _currentBrowser = null;
            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            _browserGate.Release();
            _browserGate.Dispose();
            _renderSlots.Dispose();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = _options.EnableJavaScript,
            ServiceWorkers = ServiceWorkerPolicy.Block,
            AcceptDownloads = false
        });

        context.SetDefaultTimeout(_options.RenderTimeoutMs);
        await context.RouteAsync("**/*", HandleRouteAsync);

        return context;
    }

    /// <summary>
    /// Hands out the live browser, launching or recycling first when needed. Everything happens
    /// inside the gate so a lease can never be taken on a browser that is already being retired.
    /// </summary>
    private async Task<BrowserLease> AcquireBrowserAsync(CancellationToken cancellationToken)
    {
        await _browserGate.WaitAsync(cancellationToken);
        try
        {
            var browser = _currentBrowser;

            if (browser is null || !browser.IsHealthy || browser.ServedRenders >= _options.MaxRendersPerBrowser)
            {
                if (browser is not null)
                {
                    RendererLog.BrowserRetiring(_logger, browser.IsHealthy, browser.ServedRenders);

                    await browser.RetireAsync();
                }

                _currentBrowser = browser = await LaunchAsync();
            }

            browser.Acquire();
            return browser;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    private async Task<BrowserLease> LaunchAsync()
    {
        _playwright ??= await Playwright.CreateAsync();

        var browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = _options.LaunchArgs
        });

        browser.Disconnected += (_, _) => RendererLog.BrowserDisconnected(_logger);
        RendererLog.BrowserLaunched(_logger, browser.Version);

        return new BrowserLease(browser);
    }

    private Task HandleRouteAsync(IRoute route) =>
        IsAllowed(route.Request.Url) ? route.ContinueAsync() : route.AbortAsync("blockedbyclient");

    /// <summary>
    /// Inline content is always fine; anything over the network must be on the allow-list.
    /// This is the SSRF boundary: without it a template could make the renderer fetch
    /// internal addresses on the author's behalf.
    /// </summary>
    private bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme is "data" or "about" or "blob"
            || _options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));

    private static PagePdfOptions ToPagePdfOptions(PdfRenderOptions options) => new()
    {
        Format = options.Format,
        Landscape = options.Landscape,
        PrintBackground = options.PrintBackground,
        PreferCSSPageSize = options.PreferCssPageSize,
        Margin = new Margin
        {
            Top = options.Margins.Top,
            Right = options.Margins.Right,
            Bottom = options.Margins.Bottom,
            Left = options.Margins.Left
        }
    };

    private async Task CloseQuietlyAsync(IBrowserContext? context)
    {
        if (context is null)
            return;

        try
        {
            await context.CloseAsync();
        }
        catch (PlaywrightException ex)
        {
            RendererLog.ContextCloseFailed(_logger, ex);
        }
    }
}
