using PuppeteerSharp;
using System.Text.Json;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Finds elements of a page by the ref the model has for them and prepares them for the mouse and keyboard.
/// </summary>
internal static class BrowserElementActions
{
    internal const string StaleMessage = "That element is no longer on the page. Call browser_snapshot for fresh refs.";

    private const string LocateScript = """
        function () {
            this.scrollIntoView({ block: 'center', inline: 'center' });
            const box = this.getBoundingClientRect();
            return { x: box.x + box.width / 2, y: box.y + box.height / 2, width: box.width, height: box.height };
        }
        """;

    private const string FocusScript = """
        function (text) {
            const tag = this.tagName;
            if (tag === 'SELECT') {
                const wanted = text.trim().toLowerCase();
                const index = Array.from(this.options).findIndex(o => o.text.trim().toLowerCase() === wanted || o.value.toLowerCase() === wanted);
                if (index < 0) {
                    return 'no-option';
                }

                this.selectedIndex = index;
                this.dispatchEvent(new Event('input', { bubbles: true }));
                this.dispatchEvent(new Event('change', { bubbles: true }));
                return 'selected';
            }

            const kind = (this.type || '').toLowerCase();
            const editable = this.isContentEditable || tag === 'TEXTAREA'
                || (tag === 'INPUT' && !['checkbox', 'radio', 'button', 'submit', 'reset', 'image', 'file', 'range', 'color'].includes(kind));
            if (!editable) {
                return 'not-editable';
            }

            this.focus();
            if (typeof this.select === 'function') {
                this.select();
            } else {
                const range = document.createRange();
                range.selectNodeContents(this);
                const selection = getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
            }

            return 'ready';
        }
        """;

    /// <summary>
    /// Scroll an element into view and find where to click it.
    /// </summary>
    /// <param name="client">The page's protocol session.</param>
    /// <param name="backendNodeId">The DOM node id from the ref.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>The middle of the element in page coordinates.</returns>
    /// <exception cref="BrowserException">The element is gone or has no size.</exception>
    public static async Task<(decimal X, decimal Y)> LocateAsync(ICDPSession client, long backendNodeId, CancellationToken cancellationToken)
    {
        var value = await CallAsync(client, backendNodeId, LocateScript, [], cancellationToken);
        var width = value.GetProperty("width").GetDecimal();
        var height = value.GetProperty("height").GetDecimal();
        if (width <= 0 || height <= 0)
        {
            throw new BrowserException("That element is not visible, so it can't be used. Call browser_snapshot to see the page as it is now.");
        }

        return (value.GetProperty("x").GetDecimal(), value.GetProperty("y").GetDecimal());
    }

    /// <summary>
    /// Focus a field and select what it holds so typing replaces it, or pick an option when the element is a drop-down list.
    /// </summary>
    /// <param name="client">The page's protocol session.</param>
    /// <param name="backendNodeId">The DOM node id from the ref.</param>
    /// <param name="text">What will be typed, or the option to pick.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>True when the field is ready for typing, false when the drop-down was set instead.</returns>
    /// <exception cref="BrowserException">The element can't take text.</exception>
    public static async Task<bool> PrepareTypingAsync(ICDPSession client, long backendNodeId, string text, CancellationToken cancellationToken)
    {
        var value = await CallAsync(client, backendNodeId, FocusScript, [text], cancellationToken);
        return value.GetString() switch
        {
            "ready" => true,
            "selected" => false,
            "no-option" => throw new BrowserException("The drop-down has no option with that text or value. Call browser_snapshot to see its options."),
            _ => throw new BrowserException("That element doesn't take text. Use browser_click for buttons, checkboxes and links."),
        };
    }

    private static async Task<JsonElement> CallAsync(ICDPSession client, long backendNodeId, string function, object[] arguments, CancellationToken cancellationToken)
    {
        string? objectId = null;
        try
        {
            var resolved = await client.SendAsync<JsonElement>("DOM.resolveNode", new { backendNodeId }).WaitAsync(cancellationToken);
            objectId = resolved.GetProperty("object").GetProperty("objectId").GetString();
            var result = await client.SendAsync<JsonElement>(
                "Runtime.callFunctionOn",
                new
                {
                    objectId,
                    functionDeclaration = function,
                    arguments = arguments.Select(argument => new { value = argument }).ToArray(),
                    returnByValue = true,
                }).WaitAsync(cancellationToken);
            if (result.TryGetProperty("exceptionDetails", out _))
            {
                throw new BrowserException(StaleMessage);
            }

            return result.GetProperty("result").GetProperty("value").Clone();
        }
        catch (Exception ex) when (ex is PuppeteerException or KeyNotFoundException)
        {
            throw new BrowserException(StaleMessage);
        }
        finally
        {
            if (objectId is not null)
            {
                await ReleaseAsync(client, objectId);
            }
        }
    }

    private static async Task ReleaseAsync(ICDPSession client, string objectId)
    {
        try
        {
            await client.SendAsync("Runtime.releaseObject", new { objectId });
        }
        catch (PuppeteerException)
        {
            // The page already went away, which released the object.
        }
    }
}
