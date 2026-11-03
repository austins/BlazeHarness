using PuppeteerSharp.PageAccessibility;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Turns a page's accessibility tree into an indented outline the model can read, one line per node.
/// </summary>
internal static class AccessibilityTextRenderer
{
    private const int MaxDepth = 40;

    /// <summary>
    /// Renders the tree below <paramref name="root"/>; the page's root node itself is left out.
    /// </summary>
    /// <param name="root">The root of the page's accessibility tree, or null when the page has none.</param>
    /// <returns>The outline, or an empty string for an empty page.</returns>
    public static string Render(SerializedAXNode? root)
    {
        if (root is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var child in root.Children)
        {
            Append(builder, child, 0);
        }

        return builder.ToString().TrimEnd();
    }

    private static void Append(StringBuilder builder, SerializedAXNode node, int depth)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        builder.Append(' ', depth * 2).Append("- ");
        if (node.Role is "StaticText" or "text")
        {
            builder.Append("text: ").Append(Quote(node.Name)).AppendLine();
            return;
        }

        builder.Append(node.Role);
        if (!string.IsNullOrEmpty(node.Name))
        {
            builder.Append(' ').Append(Quote(node.Name));
        }

        AppendStates(builder, node);
        if (!string.IsNullOrEmpty(node.Value))
        {
            builder.Append(": ").Append(Quote(node.Value));
        }

        builder.AppendLine();
        foreach (var child in node.Children)
        {
            Append(builder, child, depth + 1);
        }
    }

    private static void AppendStates(StringBuilder builder, SerializedAXNode node)
    {
        if (node.Level > 0)
        {
            builder.Append(" [level=").Append(node.Level.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        // Checked and Expanded are not nullable, so "false" can't be told from "not applicable"; only the positive states are shown.
        if (node.Checked == CheckedState.True)
        {
            builder.Append(" [checked]");
        }
        else if (node.Checked == CheckedState.Mixed)
        {
            builder.Append(" [checked=mixed]");
        }

        if (node.Selected)
        {
            builder.Append(" [selected]");
        }

        if (node.Expanded)
        {
            builder.Append(" [expanded]");
        }

        if (node.Disabled)
        {
            builder.Append(" [disabled]");
        }

        if (node.Required)
        {
            builder.Append(" [required]");
        }

        if (node.Focused)
        {
            builder.Append(" [focused]");
        }

        if (!string.IsNullOrEmpty(node.Url))
        {
            builder.Append(" [url=").Append(node.Url).Append(']');
        }
    }

    private static string Quote(string? text) =>
        "\"" + (text ?? string.Empty).ReplaceLineEndings(" ").Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
