namespace BlazeHarness.Web.Services;

/// <summary>
/// Limits how often a view re-renders while updates arrive faster than people can read them.
/// </summary>
/// <remarks>
/// The first request starts a short wait, and every request during the wait is folded into one render at its end,
/// so the final state is always rendered.
/// </remarks>
public sealed class RenderThrottle : IDisposable
{
    private readonly Func<Task> _render;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private int _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderThrottle"/> class.
    /// </summary>
    /// <param name="render">The action that renders the view.</param>
    /// <param name="interval">The shortest time between renders.</param>
    /// <param name="time">The clock, which tests replace; null uses the system clock.</param>
    public RenderThrottle(Func<Task> render, TimeSpan interval, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        _render = render;
        _interval = interval;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Ask for a render, which happens within one interval.
    /// </summary>
    public void Request()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 0)
        {
            _ = RunAsync();
        }
    }

    /// <summary>
    /// Cancel a render that is waiting.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await Task.Delay(_interval, _time, _stop.Token);
            Volatile.Write(ref _pending, 0);
            await _render();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The view went away while a render was waiting.
        }
    }
}
