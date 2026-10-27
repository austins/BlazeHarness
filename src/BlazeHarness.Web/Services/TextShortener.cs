namespace BlazeHarness.Web.Services;

/// <summary>
/// Cuts long text down for a preview.
/// </summary>
public static class TextShortener
{
    private const string Ellipsis = "…";

    /// <summary>
    /// Cut text to at most a length, ending in an ellipsis when something was removed.
    /// </summary>
    /// <param name="text">The text to shorten.</param>
    /// <param name="length">The most characters of the text to keep, which is at least 1.</param>
    /// <returns>The text unchanged when it fits; otherwise its start followed by an ellipsis, never ending inside a surrogate pair.</returns>
    public static string Shorten(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        if (text.Length <= length)
        {
            return text;
        }

        var cut = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..cut] + Ellipsis;
    }
}
