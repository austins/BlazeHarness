namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Shapes provider text into the one-line form results are shown in.
/// </summary>
internal static class SnippetText
{
    /// <summary>
    /// Put text on one line and cut it to a length.
    /// </summary>
    /// <param name="text">The text, or null.</param>
    /// <param name="maxCharacters">The most characters to keep, counting the ellipsis.</param>
    /// <returns>The text with every run of whitespace as one space, ending in an ellipsis when it was cut.</returns>
    public static string OneLine(string? text, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= maxCharacters)
        {
            return line;
        }

        var keep = maxCharacters - 1;
        if (keep > 0 && char.IsHighSurrogate(line[keep - 1]))
        {
            keep--;
        }

        return line[..keep].TrimEnd() + "…";
    }

    /// <summary>
    /// Check that a result address is an absolute web address.
    /// </summary>
    /// <param name="url">The text to check.</param>
    /// <returns>True when it is an absolute http or https address.</returns>
    public static bool IsWebAddress(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }
}
