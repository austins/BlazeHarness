using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Typed access to the general page helpers in <c>ui.js</c>: dialogs, menus, focus and the language.
/// </summary>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal sealed class UiScripts(IJSRuntime js) : ScriptInterop(js)
{
    /// <summary>
    /// Check whether the window is narrow enough that the sidebar becomes a drawer.
    /// </summary>
    /// <returns>True for a narrow window; false for a wide one, or when the circuit is gone.</returns>
    public ValueTask<bool> IsNarrowAsync() => InvokeAsync("bhUi.isNarrow", false);

    /// <summary>
    /// Focus a text box and select its text.
    /// </summary>
    /// <param name="input">The text box.</param>
    /// <returns>A task that completes when the box has focus.</returns>
    public ValueTask FocusSelectAsync(ElementReference input) => InvokeVoidAsync("bhUi.focusSelect", input);

    /// <summary>
    /// Move focus to the message box, or to the page content when there is none.
    /// </summary>
    /// <returns>A task that completes when focus has moved.</returns>
    public ValueTask SkipToChatAsync() => InvokeVoidAsync("bhUi.skipToChat");

    /// <summary>
    /// Open a dialog element as a modal and remember what had focus, so <see cref="RestoreFocusAsync"/> can return to it.
    /// </summary>
    /// <param name="dialog">The dialog element. A dialog that is already open is left alone.</param>
    /// <returns>A task that completes when the dialog is open.</returns>
    public ValueTask ShowModalAsync(ElementReference dialog) => InvokeVoidAsync("bhUi.showModal", dialog);

    /// <summary>
    /// Return focus to what had it before the last modal opened.
    /// </summary>
    /// <returns>A task that completes when focus has moved.</returns>
    public ValueTask RestoreFocusAsync() => InvokeVoidAsync("bhUi.restoreFocus");

    /// <summary>
    /// Open a popover menu at the pointer and nudge it back inside the window.
    /// </summary>
    /// <param name="menu">The menu element.</param>
    /// <param name="x">The pointer's distance from the left edge of the window, in pixels.</param>
    /// <param name="y">The pointer's distance from the top edge of the window, in pixels.</param>
    /// <returns>A task that completes when the menu is open.</returns>
    public ValueTask ShowContextMenuAsync(ElementReference menu, double x, double y) => InvokeVoidAsync("bhUi.showContextMenu", menu, x, y);

    /// <summary>
    /// Save the language on the server and reload the page.
    /// </summary>
    /// <param name="culture">The culture name, such as <c>en</c>.</param>
    /// <returns>A task that completes when the request for the reload was made.</returns>
    public ValueTask SetCultureAsync(string culture) => InvokeVoidAsync("bhUi.setCulture", culture);

    /// <summary>
    /// Check, once, whether the page reloaded because the language changed from the settings dialog.
    /// </summary>
    /// <returns>True when the settings dialog should open again; false otherwise, or when the circuit is gone.</returns>
    public ValueTask<bool> RestoreSettingsAfterCultureChangeAsync() => InvokeAsync("bhUi.restoreSettingsAfterCultureChange", false);
}
