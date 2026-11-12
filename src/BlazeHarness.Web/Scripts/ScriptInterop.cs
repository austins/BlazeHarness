using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Base class for the typed wrappers around the page's global script objects (<c>bhUi</c>, <c>bhChat</c> and so on).
/// </summary>
/// <remarks>
/// The scripts are global files because the content security policy only allows <c>script-src 'self'</c>.
/// A wrapper holds the function names as constants in one place, so a typo is a compile-time change and not a runtime error.
/// A call does nothing, and a call that returns a value gives its fallback, when the circuit is gone or the call was canceled,
/// because the page those effects were for is gone too.
/// </remarks>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal abstract class ScriptInterop(IJSRuntime js)
{
    /// <summary>
    /// Call a script function that returns nothing.
    /// </summary>
    /// <param name="identifier">The function, such as <c>bhUi.restoreFocus</c>.</param>
    /// <param name="args">The arguments to pass.</param>
    /// <returns>A task that completes when the function has run, or when the circuit is gone.</returns>
    protected async ValueTask InvokeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
            // The circuit is gone, and the page with it.
        }
    }

    /// <summary>
    /// Call a script function that returns a value.
    /// </summary>
    /// <typeparam name="T">The type the function returns.</typeparam>
    /// <param name="identifier">The function, such as <c>bhUi.isNarrow</c>.</param>
    /// <param name="fallback">The value to give when the circuit is gone.</param>
    /// <param name="args">The arguments to pass.</param>
    /// <returns>The value the function returned, or <paramref name="fallback"/> when the circuit is gone.</returns>
    protected async ValueTask<T> InvokeAsync<T>(string identifier, T fallback, params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
            // The circuit is gone, and the page with it.
            return fallback;
        }
    }
}
