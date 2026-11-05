using Jeffijoe.MessageFormat;
using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Net;
using System.Resources;

namespace BlazeHarness.Web.Resources;

internal sealed class ResourceMessageFormatter(ResourceManager resources, CultureInfo neutralCulture)
{
    /// <summary>
    /// The only tags a translation may use; everything else in a pattern is encoded as text.
    /// </summary>
    private static readonly (string Tag, string Html)[] MarkupTags =
    [
        ("[code]", "<code>"), ("[/code]", "</code>"),
        ("[strong]", "<strong>"), ("[/strong]", "</strong>"),
        ("[seconds]", "<span id=\"components-seconds-to-next-attempt\"></span>"),
    ];

    /// <summary>
    /// Gets the shared formatter for web UI resources.
    /// </summary>
    /// <remarks>Exact-culture lookups stay separate from ResourceManager's cached parent fallbacks.</remarks>
    internal static ResourceMessageFormatter Web { get; } =
        new(new ResourceManager(typeof(WebStrings)), CultureInfo.GetCultureInfo("en-US"));

    internal string Format(string key, IReadOnlyDictionary<string, object?> arguments, CultureInfo? culture = null)
    {
        var (pattern, formatCulture) = Find(key, culture);
        return MessageFormatter.Format(pattern, arguments, formatCulture);
    }

    /// <summary>
    /// Format a sentence with the allowed inline markup.
    /// </summary>
    /// <remarks>The rest of the pattern and every text argument are HTML-encoded.</remarks>
    /// <param name="key">The resource key of the sentence.</param>
    /// <param name="arguments">The values to substitute into the pattern.</param>
    /// <param name="culture">The resource culture, or null for the current UI culture.</param>
    internal MarkupString FormatMarkup(string key, IReadOnlyDictionary<string, object?> arguments, CultureInfo? culture = null)
    {
        var (pattern, formatCulture) = Find(key, culture);
        var html = WebUtility.HtmlEncode(pattern);
        foreach (var (tag, replacement) in MarkupTags)
        {
            html = html.Replace(tag, replacement, StringComparison.Ordinal);
        }

        var encoded = arguments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is string text ? WebUtility.HtmlEncode(text) : pair.Value);
        return new MarkupString(MessageFormatter.Format(html, encoded, formatCulture));
    }

    internal MarkupString FormatMarkup(string key) => FormatMarkup(key, new Dictionary<string, object?>());

    private (string Pattern, CultureInfo Culture) Find(string key, CultureInfo? culture)
    {
        var resourceCulture = culture ?? CultureInfo.CurrentUICulture;
        while (!resourceCulture.Equals(CultureInfo.InvariantCulture))
        {
            var value = resources.GetResourceSet(resourceCulture, true, false)?.GetString(key);
            if (value is not null)
            {
                return (value, resourceCulture);
            }

            resourceCulture = resourceCulture.Parent;
        }

        var neutralValue = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)?.GetString(key)
            ?? throw new KeyNotFoundException($"The resource '{key}' was not found.");
        return (neutralValue, neutralCulture);
    }
}
