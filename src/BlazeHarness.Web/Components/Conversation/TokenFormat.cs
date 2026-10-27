using System.Globalization;

namespace BlazeHarness.Web.Components.Conversation;

/// <summary>
/// Formats token counts for display.
/// </summary>
public static class TokenFormat
{
    /// <summary>
    /// Format a count in a short form, such as <c>950</c>, <c>4.2k</c> or <c>128k</c>.
    /// </summary>
    /// <param name="tokens">The number of tokens.</param>
    /// <returns>The text to show.</returns>
    public static string Of(long tokens)
    {
        return tokens switch
        {
            < 1_000 => tokens.ToString(CultureInfo.CurrentCulture),
            < 10_000 => (tokens / 1_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "k",
            _ => (tokens / 1_000).ToString(CultureInfo.CurrentCulture) + "k",
        };
    }
}
