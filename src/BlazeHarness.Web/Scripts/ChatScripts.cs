using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Typed access to the conversation helpers in <c>chat.js</c>: scrolling, search highlights, the message box and copying.
/// </summary>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal sealed class ChatScripts(IJSRuntime js) : ScriptInterop(js)
{
    /// <summary>
    /// Copy text to the clipboard.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <returns>True when the text was copied; false when the browser refused, or when the circuit is gone.</returns>
    public ValueTask<bool> CopyTextAsync(string text) => InvokeAsync("bhChat.copyText", false, text);

    /// <summary>
    /// Follow new content while the reader is at the bottom, and stop following once they scroll up.
    /// </summary>
    /// <param name="root">The conversation element.</param>
    /// <param name="scroll">The element that scrolls.</param>
    /// <returns>A task that completes when the watch has started.</returns>
    public ValueTask WatchAsync(ElementReference root, ElementReference scroll) => InvokeVoidAsync("bhChat.watch", root, scroll);

    /// <summary>
    /// Bring the reader back to the newest content.
    /// </summary>
    /// <param name="root">The conversation element.</param>
    /// <returns>A task that completes when the reader is at the newest content.</returns>
    public ValueTask FollowAsync(ElementReference root) => InvokeVoidAsync("bhChat.follow", root);

    /// <summary>
    /// Mark the matches of a search in one message and scroll to it.
    /// </summary>
    /// <param name="scroll">The element that scrolls.</param>
    /// <param name="query">The text that was searched for.</param>
    /// <param name="entryId">The id of the message that matched.</param>
    /// <returns>A task that completes when the matches are marked.</returns>
    public ValueTask HighlightAsync(ElementReference scroll, string query, Guid entryId) => InvokeVoidAsync("bhChat.highlight", scroll, query, entryId.ToString());

    /// <summary>
    /// Remove the marks of the last search.
    /// </summary>
    /// <returns>A task that completes when the marks are gone.</returns>
    public ValueTask ClearHighlightAsync() => InvokeVoidAsync("bhChat.clearHighlight");

    /// <summary>
    /// Wire up the message box: Enter sends, Alt+Enter queues, Shift+Enter adds a line, and the box grows with its text.
    /// </summary>
    /// <param name="input">The message box.</param>
    /// <param name="form">The form around it.</param>
    /// <returns>A task that completes when the box is wired up.</returns>
    public ValueTask InitComposerAsync(ElementReference input, ElementReference form) => InvokeVoidAsync("bhChat.initComposer", input, form);

    /// <summary>
    /// Fit the message box to its text and focus it.
    /// </summary>
    /// <param name="input">The message box.</param>
    /// <returns>A task that completes when the box has its new height and focus.</returns>
    public ValueTask ResizeAsync(ElementReference input) => InvokeVoidAsync("bhChat.resize", input);

    /// <summary>
    /// Empty a file picker, so the browser accepts the same file again.
    /// </summary>
    /// <param name="picker">The file input.</param>
    /// <returns>A task that completes when the picker is empty.</returns>
    public ValueTask ClearPickerAsync(ElementReference picker) => InvokeVoidAsync("bhChat.clearPicker", picker);
}
