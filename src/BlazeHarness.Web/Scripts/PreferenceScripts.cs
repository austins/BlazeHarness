using Microsoft.JSInterop;

namespace BlazeHarness.Web.Scripts;

/// <summary>
/// Typed access to the reader's display preferences in <c>theme.js</c>, which keeps them in the browser's local storage.
/// </summary>
/// <param name="js">The runtime of the circuit the page belongs to.</param>
internal sealed class PreferenceScripts(IJSRuntime js) : ScriptInterop(js)
{
    private const string DefaultSize = "default";

    /// <summary>
    /// Check whether the dark theme is showing.
    /// </summary>
    /// <returns>True for the dark theme; false for the light one, or when the circuit is gone.</returns>
    public ValueTask<bool> IsDarkAsync() => InvokeAsync("bhTheme.isDark", false);

    /// <summary>
    /// Show and remember a theme.
    /// </summary>
    /// <param name="dark">True for the dark theme; false for the light one.</param>
    /// <returns>A task that completes when the theme is applied.</returns>
    public ValueTask SetDarkAsync(bool dark) => InvokeVoidAsync("bhTheme.set", dark ? "dark" : "light");

    /// <summary>
    /// Get a saved text size.
    /// </summary>
    /// <param name="kind">Which size to read.</param>
    /// <returns>The step, such as <c>large</c>; <c>default</c> when none is saved or the circuit is gone.</returns>
    public ValueTask<string> GetTextSizeAsync(TextSizeKind kind) => InvokeAsync("bhSize.get", DefaultSize, SizeName(kind));

    /// <summary>
    /// Apply and remember a text size.
    /// </summary>
    /// <param name="kind">Which size to change.</param>
    /// <param name="step">The step, such as <c>large</c>.</param>
    /// <returns>A task that completes when the size is applied.</returns>
    public ValueTask SetTextSizeAsync(TextSizeKind kind, string step) => InvokeVoidAsync("bhSize.set", SizeName(kind), step);

    /// <summary>
    /// Check whether the conversation uses the full width of the window.
    /// </summary>
    /// <returns>True for the full width; false otherwise, or when the circuit is gone.</returns>
    public ValueTask<bool> GetFullChatWidthAsync() => InvokeAsync("bhChatWidth.get", false);

    /// <summary>
    /// Apply and remember whether the conversation uses the full width of the window.
    /// </summary>
    /// <param name="full">True for the full width.</param>
    /// <returns>A task that completes when the width is applied.</returns>
    public ValueTask SetFullChatWidthAsync(bool full) => InvokeVoidAsync("bhChatWidth.set", full);

    /// <summary>
    /// Check whether long diff lines wrap.
    /// </summary>
    /// <returns>True when they wrap; false otherwise, or when the circuit is gone.</returns>
    public ValueTask<bool> GetDiffWrapAsync() => InvokeAsync("bhDiffWrap.get", false);

    /// <summary>
    /// Remember whether long diff lines wrap.
    /// </summary>
    /// <param name="wrap">True to wrap them.</param>
    /// <returns>A task that completes when the choice is saved.</returns>
    public ValueTask SetDiffWrapAsync(bool wrap) => InvokeVoidAsync("bhDiffWrap.set", wrap);

    /// <summary>
    /// Get the saved order of the chat lists.
    /// </summary>
    /// <returns>The saved order's name, or null when none is saved or the circuit is gone.</returns>
    public ValueTask<string?> GetChatSortAsync() => InvokeAsync<string?>("bhChatSort.get", null);

    /// <summary>
    /// Remember the order of the chat lists.
    /// </summary>
    /// <param name="order">The order's name.</param>
    /// <returns>A task that completes when the order is saved.</returns>
    public ValueTask SetChatSortAsync(string order) => InvokeVoidAsync("bhChatSort.set", order);

    private static string SizeName(TextSizeKind kind) => kind switch
    {
        TextSizeKind.Ui => "ui",
        TextSizeKind.Chat => "chat",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
