using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Typed access to the browser panel's picture and input forwarding in <c>browser.js</c>.
/// </summary>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal sealed class BrowserScripts(IJSRuntime js) : ScriptInterop(js)
{
    /// <summary>
    /// Start showing the page's pictures and passing the reader's clicks, keys and scrolling on to <paramref name="target"/>.
    /// </summary>
    /// <remarks>It stops any earlier stream first. The caller keeps ownership of <paramref name="target"/> and disposes it after <see cref="StopAsync"/>.</remarks>
    /// <typeparam name="T">The component that receives the input.</typeparam>
    /// <param name="view">The element that takes the input.</param>
    /// <param name="image">The canvas the pictures are drawn on.</param>
    /// <param name="target">The component whose <c>[JSInvokable]</c> methods receive the input.</param>
    /// <param name="streamAddress">The address of the stream of pictures.</param>
    /// <returns>A task that completes when the stream is bound.</returns>
    public ValueTask BindAsync<T>(ElementReference view, ElementReference image, DotNetObjectReference<T> target, string streamAddress)
        where T : class
    {
        return InvokeVoidAsync("bhBrowser.bind", view, image, target, streamAddress);
    }

    /// <summary>
    /// Stop the stream and remove its input handlers.
    /// </summary>
    /// <returns>A task that completes when the stream has stopped.</returns>
    public ValueTask StopAsync() => InvokeVoidAsync("bhBrowser.stop");
}
