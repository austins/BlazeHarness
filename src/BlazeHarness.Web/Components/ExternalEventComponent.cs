using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Components;

/// <summary>
/// Base class for components that react to plain C# events (<see cref="EventHandler"/>) raised by services.
/// </summary>
/// <remarks>
/// Such an event has a <see langword="void"/> delegate, so a handler cannot return the <see cref="Task"/> that
/// <see cref="ComponentBase.InvokeAsync(Action)"/> gives back, and discarding that task hides any exception in it.
/// The <c>RunOnRenderer</c> methods are the one place that awaits it and sends a failure to the renderer with
/// <see cref="ComponentBase.DispatchExceptionAsync"/>, so it is handled like a lifecycle exception. They return
/// <see langword="void"/> and start a task that handles every failure itself, rather than being <see langword="async void"/>.
/// Where a service owns the event, prefer a <see cref="Task"/>-returning delegate and await it in the handler instead.
/// </remarks>
public abstract class ExternalEventComponent : ComponentBase
{
    /// <summary>
    /// Run <paramref name="update"/> on the renderer's synchronization context, from an event handler that may be raised on another thread.
    /// </summary>
    /// <param name="update">The state change to make. It does not need to call <see cref="ComponentBase.StateHasChanged"/> itself unless it renders in between.</param>
    protected void RunOnRenderer(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _ = RunAsync(() => InvokeAsync(update));
    }

    /// <summary>
    /// Run <paramref name="update"/> on the renderer's synchronization context, from an event handler that may be raised on another thread.
    /// </summary>
    /// <param name="update">The asynchronous state change to make.</param>
    protected void RunOnRenderer(Func<Task> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _ = RunAsync(() => InvokeAsync(update));
    }

    /// <summary>
    /// Render the component again, as an <see cref="EventHandler"/> for events that only mean "something changed".
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event data.</param>
    protected void RenderOnEvent(object? sender, EventArgs e) => RunOnRenderer(StateHasChanged);

    /// <summary>
    /// Run <paramref name="work"/> and handle every exception, so the returned task never faults and discarding it hides nothing.
    /// </summary>
    private async Task RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (!IsTeardown(ex))
        {
            try
            {
                await DispatchExceptionAsync(ex);
            }
            catch (Exception dispatch) when (IsTeardown(dispatch))
            {
                // The circuit went away between the failure and the dispatch, and there is nobody left to tell.
            }
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // The circuit or page is gone, so there is nothing to render to.
        }
    }

    private static bool IsTeardown(Exception ex) => ex is OperationCanceledException or ObjectDisposedException or JSDisconnectedException;
}
