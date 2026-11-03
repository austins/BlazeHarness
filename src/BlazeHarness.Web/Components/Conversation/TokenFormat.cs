using BlazeHarness.Web.Resources;
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
            < 10_000 => Unit(nameof(WebStrings.TokenFormatThousands), (tokens / 1_000.0).ToString("0.#", CultureInfo.CurrentCulture)),
            _ => Unit(nameof(WebStrings.TokenFormatThousands), (tokens / 1_000).ToString(CultureInfo.CurrentCulture)),
        };
    }

    /// <summary>
    /// Format a speed, such as <c>8.4 t/s</c> or <c>1.2k t/s</c>.
    /// </summary>
    /// <param name="tokensPerSecond">The tokens per second.</param>
    /// <returns>The text to show.</returns>
    public static string Rate(double tokensPerSecond)
    {
        var value = tokensPerSecond switch
        {
            < 10 => tokensPerSecond.ToString("0.0", CultureInfo.CurrentCulture),
            < 1_000 => tokensPerSecond.ToString("0", CultureInfo.CurrentCulture),
            _ => Of((long)tokensPerSecond),
        };
        return Unit(nameof(WebStrings.TokenFormatRate), value);
    }

    /// <summary>
    /// Format a duration in a short form, such as <c>420 ms</c>, <c>3.2 s</c> or <c>2m 05s</c>.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The text to show.</returns>
    public static string Duration(TimeSpan duration)
    {
        return duration.TotalSeconds switch
        {
            < 1 => Unit(nameof(WebStrings.TokenFormatMilliseconds), duration.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture)),
            < 60 => Unit(nameof(WebStrings.TokenFormatSeconds), duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)),
            _ => ResourceMessageFormatter.Web.Format(
                nameof(WebStrings.TokenFormatMinutes),
                new Dictionary<string, object?>
                {
                    ["minutes"] = ((int)duration.TotalMinutes).ToString(CultureInfo.CurrentCulture),
                    ["seconds"] = duration.Seconds.ToString("00", CultureInfo.CurrentCulture),
                }),
        };
    }

    private static string Unit(string key, string value) =>
        ResourceMessageFormatter.Web.Format(key, new Dictionary<string, object?> { ["value"] = value });
}
