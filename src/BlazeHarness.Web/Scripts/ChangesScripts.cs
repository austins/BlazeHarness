using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Typed access to the changes panel helpers in <c>ui.js</c>.
/// </summary>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal sealed class ChangesScripts(IJSRuntime js) : ScriptInterop(js)
{
    /// <summary>
    /// Scroll the diff back to its top.
    /// </summary>
    /// <returns>A task that completes when the diff has scrolled.</returns>
    public ValueTask ResetScrollAsync() => InvokeVoidAsync("bhChanges.resetScroll");
}
