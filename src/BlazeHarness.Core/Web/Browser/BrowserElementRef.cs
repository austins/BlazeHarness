using System.Globalization;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// The names the model uses for elements of a page, such as <c>e42</c>, which are the ids of their DOM nodes.
/// </summary>
internal static class BrowserElementRef
{
    private const string Prefix = "e";

    /// <summary>
    /// Writes the ref of a DOM node.
    /// </summary>
    /// <param name="backendNodeId">The backend id of the DOM node.</param>
    /// <returns>The ref.</returns>
    public static string Format(long backendNodeId) => Prefix + backendNodeId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a ref back into a DOM node id.
    /// </summary>
    /// <param name="text">The ref as the model wrote it.</param>
    /// <param name="backendNodeId">The backend id of the DOM node, when the result is true.</param>
    /// <returns>True when the text is a ref.</returns>
    public static bool TryParse(string? text, out long backendNodeId)
    {
        backendNodeId = 0;
        return text is not null
            && text.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(text.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out backendNodeId)
            && backendNodeId > 0;
    }
}
