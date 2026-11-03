#pragma warning disable IDISP004 // JsonElement's enumerators are structs with nothing to release.
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Turns a page's accessibility tree (the <c>nodes</c> of Chrome's <c>Accessibility.getFullAXTree</c>) into an indented outline the model can read, one line per node.
/// </summary>
/// <remarks>
/// Things the model can act on carry a <c>[ref=eN]</c> that <c>browser_click</c> and <c>browser_type</c> take back. The number is the
/// DOM node's id, so a ref stays valid while its element stays on the page.
/// </remarks>
internal static class AccessibilityTextRenderer
{
    private const int MaxDepth = 40;

    private static readonly HashSet<string> Transparent = new(StringComparer.Ordinal)
    {
        "RootWebArea", "WebArea", "generic", "none", "presentation", "Section", "InlineTextBox", "LineBreak", "ListMarker",
    };

    private static readonly HashSet<string> Controls = new(StringComparer.Ordinal)
    {
        "button", "link", "textbox", "searchbox", "checkbox", "radio", "combobox", "listbox", "option", "menuitem", "menuitemcheckbox",
        "menuitemradio", "tab", "switch", "slider", "spinbutton", "treeitem", "PopUpButton", "DisclosureTriangle", "ComboBoxSelect",
    };

    /// <summary>
    /// Renders the tree; the page's root node itself is left out.
    /// </summary>
    /// <param name="nodes">The <c>nodes</c> array of the accessibility tree, with the root first, or a value that is not an array when the page has none.</param>
    /// <returns>The outline, or an empty string for an empty page.</returns>
    public static string Render(JsonElement nodes)
    {
        if (nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("nodeId", out var id) && id.GetString() is { } key)
            {
                byId[key] = node;
            }
        }

        var builder = new StringBuilder();
        Visit(builder, nodes[0], byId, 0);
        return builder.ToString().TrimEnd();
    }

    private static void Visit(StringBuilder builder, JsonElement node, Dictionary<string, JsonElement> byId, int depth)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        var role = AxText(node, "role");
        if (role is "InlineTextBox" or "LineBreak" or "ListMarker")
        {
            return;
        }

        var ignored = node.TryGetProperty("ignored", out var flag) && flag.ValueKind == JsonValueKind.True;
        var name = AxText(node, "name");
        if (!ignored && role == "StaticText")
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.Append(' ', depth * 2).Append("- text: ").Append(Quote(name)).AppendLine();
            }

            return;
        }

        var properties = Properties(node);
        var isControl = Controls.Contains(role)
            || (role is not ("RootWebArea" or "WebArea") && properties.TryGetValue("focusable", out var focusable) && focusable == "true");
        if (ignored || (Transparent.Contains(role) && !isControl))
        {
            VisitChildren(builder, node, byId, depth);
            return;
        }

        var value = AxText(node, "value");
        var leaf = name.Length > 0 && (isControl || role is "heading" or "img" or "image");
        var inner = new StringBuilder();
        if (!leaf)
        {
            VisitChildren(inner, node, byId, depth + 1);
            if (inner.Length == 0 && name.Length == 0 && value.Length == 0 && !isControl)
            {
                return;
            }
        }

        builder.Append(' ', depth * 2).Append("- ").Append(role);
        if (name.Length > 0)
        {
            builder.Append(' ').Append(Quote(name));
        }

        AppendStates(builder, properties);
        if (isControl && node.TryGetProperty("backendDOMNodeId", out var backend) && backend.TryGetInt64(out var backendId))
        {
            builder.Append(" [ref=").Append(BrowserElementRef.Format(backendId)).Append(']');
        }

        if (value.Length > 0)
        {
            builder.Append(": ").Append(Quote(value));
        }

        builder.AppendLine().Append(inner);
    }

    private static void VisitChildren(StringBuilder builder, JsonElement node, Dictionary<string, JsonElement> byId, int depth)
    {
        if (!node.TryGetProperty("childIds", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (child.GetString() is { } id && byId.TryGetValue(id, out var next))
            {
                Visit(builder, next, byId, depth);
            }
        }
    }

    private static Dictionary<string, string> Properties(JsonElement node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!node.TryGetProperty("properties", out var list))
        {
            return result;
        }

        foreach (var property in list.EnumerateArray())
        {
            if (property.TryGetProperty("name", out var key) && key.GetString() is { } name)
            {
                result[name] = property.TryGetProperty("value", out var wrapper) ? Scalar(wrapper) : string.Empty;
            }
        }

        return result;
    }

    private static void AppendStates(StringBuilder builder, Dictionary<string, string> properties)
    {
        if (properties.TryGetValue("level", out var level))
        {
            builder.Append(" [level=").Append(level).Append(']');
        }

        // Only positive states are shown: "false" is the usual case and would be noise on every line.
        if (properties.TryGetValue("checked", out var state) && state is "true" or "mixed")
        {
            builder.Append(state == "true" ? " [checked]" : " [checked=mixed]");
        }

        foreach (var flag in (string[])["selected", "expanded", "disabled", "required", "focused", "readonly"])
        {
            if (properties.TryGetValue(flag, out var on) && on == "true")
            {
                builder.Append(" [").Append(flag).Append(']');
            }
        }

        if (properties.TryGetValue("url", out var url) && url.Length > 0)
        {
            builder.Append(" [url=").Append(url).Append(']');
        }
    }

    private static string AxText(JsonElement node, string property) =>
        node.TryGetProperty(property, out var wrapper) ? Scalar(wrapper) : string.Empty;

    private static string Scalar(JsonElement axValue)
    {
        if (!axValue.TryGetProperty("value", out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    private static string Quote(string text) =>
        "\"" + text.ReplaceLineEndings(" ").Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
