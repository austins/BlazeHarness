namespace BlazeHarness.Core;

/// <summary>
/// Cuts text to a character limit without splitting a UTF-16 surrogate pair.
/// </summary>
internal static class TextClip
{
    /// <summary>
    /// Return at most <paramref name="maxCharacters"/> UTF-16 code units from the start of <paramref name="text"/>,
    /// stepping back one unit when the cut would split a surrogate pair.
    /// </summary>
    /// <param name="text">The text to clip.</param>
    /// <param name="maxCharacters">The maximum length in <see cref="char"/> units.</param>
    /// <returns>The clipped prefix, or <paramref name="text"/> unchanged when it already fits.</returns>
    public static string Start(string text, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);
        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var length = maxCharacters > 0 && char.IsHighSurrogate(text[maxCharacters - 1]) ? maxCharacters - 1 : maxCharacters;
        return text[..length];
    }
}
