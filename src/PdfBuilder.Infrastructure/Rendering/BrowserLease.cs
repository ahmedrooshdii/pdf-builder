using Microsoft.Playwright;

namespace PdfBuilder.Infrastructure.Rendering;

/// <summary>
/// A reference-counted Chromium instance.
/// </summary>
/// <remarks>
/// Recycling a browser is a two-step affair: the renderer stops handing it out, then the process is
/// closed once the renders already using it have finished. Counting leases is what keeps those two
/// steps from colliding - closing a browser out from under an in-flight render would surface as a
/// spurious <c>TargetClosedException</c>.
/// </remarks>
internal sealed class BrowserLease(IBrowser browser)
{
    private int _activeLeases;
    private int _servedRenders;
    private int _retired;
    private int _closed;

    public IBrowser Browser { get; } = browser;

    /// <summary>Total renders this browser has been handed out for, used to decide when to recycle.</summary>
    public int ServedRenders => Volatile.Read(ref _servedRenders);

    /// <summary>False once the process has died or the browser has been marked for recycling.</summary>
    public bool IsHealthy => Browser.IsConnected && Volatile.Read(ref _retired) == 0;

    /// <summary>Called under the renderer's browser gate, so it can never race with <see cref="RetireAsync"/>.</summary>
    public void Acquire()
    {
        Interlocked.Increment(ref _activeLeases);
        Interlocked.Increment(ref _servedRenders);
    }

    public ValueTask ReleaseAsync() =>
        Interlocked.Decrement(ref _activeLeases) == 0 && Volatile.Read(ref _retired) == 1
            ? CloseAsync()
            : ValueTask.CompletedTask;

    /// <summary>Stops new leases. Closes the process now if idle, otherwise on the last release.</summary>
    public ValueTask RetireAsync()
    {
        Volatile.Write(ref _retired, 1);

        return Volatile.Read(ref _activeLeases) == 0 ? CloseAsync() : ValueTask.CompletedTask;
    }

    private async ValueTask CloseAsync()
    {
        // Exactly once, whichever of retire/release wins the race.
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;

        try
        {
            await Browser.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // The process may already be gone; there is nothing left to clean up.
        }
    }
}
