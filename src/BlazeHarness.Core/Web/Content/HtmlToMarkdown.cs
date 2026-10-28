using HtmlAgilityPack;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Turns an HTML page into Markdown that keeps the structure a model needs (headings, lists, links, code, tables) and drops the page furniture.
/// </summary>
/// <remarks>
/// Scripts, styles, navigation, footers, sidebars, forms and hidden elements are dropped. When the page has a main region or a single article,
/// only that is converted. The page is never run, so a page that builds its text with JavaScript comes out nearly empty, and the result says so.
/// </remarks>
internal sealed partial class HtmlToMarkdown
{
    private const int MaxDepth = 200;
    private const int MaxHeadings = 80;

    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "template", "svg", "iframe", "canvas", "object", "embed", "head", "button", "input", "select", "textarea", "dialog", "nav", "footer", "aside", "form", "link", "meta", "audio", "video", "map",
    };

    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal)
    {
        "address", "article", "blockquote", "body", "dd", "details", "div", "dl", "dt", "fieldset", "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hgroup", "hr", "html", "li", "main", "ol", "p", "pre", "section", "summary", "table", "tbody", "thead", "tfoot", "tr", "td", "th", "ul", "caption",
    };

    private readonly Uri _baseUri;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _headings = [];

    private HtmlToMarkdown(Uri baseUri, CancellationToken cancellationToken)
    {
        _baseUri = baseUri;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Convert a page.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="baseUri">The page address, which relative links are resolved against.</param>
    /// <param name="cancellationToken">A token that stops the conversion of a very large page.</param>
    /// <returns>The Markdown, the title, the headings, and a notice when the page looks like it needs JavaScript.</returns>
    public static ExtractedDocument Convert(HtmlDocument html, Uri baseUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(baseUri);
        var converter = new HtmlToMarkdown(baseUri, cancellationToken);
        var root = PickRoot(html);
        var text = string.Join("\n\n", converter.BlocksOf(root, 0));
        var title = TitleOf(html);
        var notice = text.Length < 200 && LooksScriptDriven(html)
            ? "This page has very little text and seems to build its content with JavaScript, which this tool does not run. The text here may be incomplete."
            : null;
        return new ExtractedDocument(title, text, converter._headings, notice);
    }

    private static string TitleOf(HtmlDocument html)
    {
        var title = html.DocumentNode.SelectSingleNode("//title")?.InnerText;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = html.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty);
        }

        return Collapse(WebUtility.HtmlDecode(title ?? string.Empty)).Trim();
    }

    private static bool LooksScriptDriven(HtmlDocument html)
    {
        var scripts = html.DocumentNode.SelectNodes("//script")?.Count ?? 0;
        var hasNoscriptHint = html.DocumentNode.SelectNodes("//noscript")?.Any(node => node.InnerText.Contains("javascript", StringComparison.OrdinalIgnoreCase)) ?? false;
        var emptyRoot = html.DocumentNode.SelectSingleNode("//*[@id='root' or @id='app' or @id='__next' or @id='__nuxt']") is { } shell && string.IsNullOrWhiteSpace(shell.InnerText);
        return scripts >= 3 || hasNoscriptHint || emptyRoot;
    }

    private static HtmlNode PickRoot(HtmlDocument html)
    {
        var body = html.DocumentNode.SelectSingleNode("//body") ?? html.DocumentNode;
        var bodyLength = TextLength(body);
        var main = html.DocumentNode.SelectSingleNode("//main | //*[@role='main']");
        if (main is not null && TextLength(main) >= Math.Min(200, bodyLength / 4))
        {
            return main;
        }

        var articles = html.DocumentNode.SelectNodes("//article");
        return articles is { Count: 1 } && TextLength(articles[0]) >= bodyLength / 3 ? articles[0] : body;
    }

    private static int TextLength(HtmlNode node)
    {
        return node.DescendantsAndSelf().Where(n => n.NodeType == HtmlNodeType.Text).Sum(n => n.InnerText.Count(c => !char.IsWhiteSpace(c)));
    }

    private static string Collapse(string text) => WhitespaceRun().Replace(text, " ");

    private static bool IsHidden(HtmlNode node)
    {
        if (node.Attributes.Contains("hidden") || string.Equals(node.GetAttributeValue("aria-hidden", string.Empty), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var style = node.GetAttributeValue("style", string.Empty);
        return style.Length > 0 && HiddenStyle().IsMatch(style);
    }

    private static bool Skipped(HtmlNode node) => Dropped.Contains(node.Name) || IsHidden(node);

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Fence(string code, string language)
    {
        var longest = Regex.Matches(code, "`+", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longest + 1));

        return $"{fence}{language}\n{code.Trim('\n')}\n{fence}";
    }

    private static string Language(HtmlNode pre)
    {
        var holder = pre.SelectSingleNode(".//code") ?? pre;
        var name = (pre.GetAttributeValue("class", string.Empty) + " " + holder.GetAttributeValue("class", string.Empty))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(n => n.StartsWith("language-", StringComparison.Ordinal));
        return name is null ? string.Empty : name[9..];
    }

    private List<string> BlocksOf(HtmlNode parent, int depth)
    {
        List<string> blocks = [];
        var run = new StringBuilder();
        foreach (var child in parent.ChildNodes)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (child.NodeType == HtmlNodeType.Text)
            {
                run.Append(Collapse(WebUtility.HtmlDecode(child.InnerText)));
            }
            else if (child.NodeType == HtmlNodeType.Element && !Skipped(child))
            {
                if (Blocks.Contains(child.Name))
                {
                    Flush(run, blocks);
                    blocks.AddRange(depth >= MaxDepth ? [] : BlockElement(child, depth + 1));
                }
                else
                {
                    run.Append(Inline(child, depth + 1));
                }
            }
        }

        Flush(run, blocks);
        return blocks;
    }

    private static void Flush(StringBuilder run, List<string> blocks)
    {
        if (run.Length == 0)
        {
            return;
        }

        var text = string.Join("\n", run.ToString().Split('\n').Select(line => Collapse(line).Trim())).Trim();
        run.Clear();
        if (text.Length > 0)
        {
            blocks.Add(text);
        }
    }

    private List<string> BlockElement(HtmlNode node, int depth)
    {
        switch (node.Name)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                var heading = Collapse(Inline(node, depth).Replace('\n', ' ')).Trim();
                if (heading.Length == 0)
                {
                    return [];
                }

                var line = new string('#', node.Name[1] - '0') + " " + heading;
                if (_headings.Count < MaxHeadings)
                {
                    _headings.Add(line);
                }

                return [line];
            case "pre":
                var code = WebUtility.HtmlDecode(node.InnerText).Replace("\r\n", "\n", StringComparison.Ordinal);
                return string.IsNullOrWhiteSpace(code) ? [] : [Fence(code, Language(node))];
            case "ul" or "ol":
                var list = ListOf(node, depth);
                return list.Length == 0 ? [] : [list];
            case "blockquote":
                var inner = BlocksOf(node, depth);
                return inner.Count == 0 ? [] : [string.Join("\n", string.Join("\n\n", inner).Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l))];
            case "table":
                var table = TableOf(node, depth);
                return table is null ? [] : [table];
            case "hr":
                return ["---"];
            case "dt":
                var term = Collapse(Inline(node, depth)).Trim();
                return term.Length == 0 ? [] : ["**" + term + "**"];
            default:
                return BlocksOf(node, depth);
        }
    }

    private string ListOf(HtmlNode list, int depth)
    {
        var ordered = list.Name == "ol";
        var number = int.TryParse(list.GetAttributeValue("start", "1"), out var start) ? start : 1;
        List<string> lines = [];
        foreach (var item in list.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element && n.Name == "li" && !Skipped(n)))
        {
            var parts = BlocksOf(item, depth);
            if (parts.Count == 0)
            {
                continue;
            }

            var marker = ordered ? number++ + ". " : "- ";
            var indent = new string(' ', marker.Length);
            var text = string.Join("\n", string.Join("\n", parts).Split('\n').Select((l, i) => i == 0 ? marker + l : (l.Length == 0 ? l : indent + l)));
            lines.Add(text);
        }

        return string.Join("\n", lines);
    }

    private static IEnumerable<HtmlNode> RowsOf(HtmlNode table)
    {
        foreach (var child in table.ChildNodes)
        {
            if (child.Name == "tr")
            {
                yield return child;
            }
            else if (child.Name is "thead" or "tbody" or "tfoot")
            {
                foreach (var row in child.ChildNodes.Where(n => n.Name == "tr"))
                {
                    yield return row;
                }
            }
        }
    }

    private string? TableOf(HtmlNode table, int depth)
    {
        List<List<string>> rows = [];
        foreach (var row in RowsOf(table))
        {
            var cells = row.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element && n.Name is "td" or "th" && !Skipped(n))
                .Select(cell => Escape(Collapse(Inline(cell, depth).Replace('\n', ' ')).Trim()))
                .ToList();
            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var width = rows.Max(r => r.Count);
        if (width == 1)
        {
            return string.Join("\n\n", rows.Select(r => r[0]).Where(c => c.Length > 0));
        }

        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            builder.Append("| ").AppendJoin(" | ", rows[i].Concat(Enumerable.Repeat(string.Empty, width - rows[i].Count))).Append(" |\n");
            if (i == 0)
            {
                builder.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string Inline(HtmlNode node, int depth)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (node.NodeType == HtmlNodeType.Text)
        {
            return Collapse(WebUtility.HtmlDecode(node.InnerText));
        }

        if (node.NodeType != HtmlNodeType.Element || Skipped(node) || depth >= MaxDepth)
        {
            return string.Empty;
        }

        switch (node.Name)
        {
            case "br":
                return "\n";
            case "img":
                var alt = Collapse(WebUtility.HtmlDecode(node.GetAttributeValue("alt", string.Empty))).Trim();
                return alt.Length == 0 ? string.Empty : $"[Image: {alt}]";
            case "a":
                var label = InlineChildren(node, depth).Trim();
                var target = LinkTarget(node.GetAttributeValue("href", string.Empty));
                return label.Length == 0 ? string.Empty : target is null ? label : $"[{label}]({target})";
            case "strong" or "b":
                return Wrap(InlineChildren(node, depth), "**");
            case "em" or "i":
                return Wrap(InlineChildren(node, depth), "*");
            case "code" or "kbd" or "samp" or "tt":
                var codeText = Collapse(WebUtility.HtmlDecode(node.InnerText)).Trim();
                return codeText.Length == 0 ? string.Empty : codeText.Contains('`', StringComparison.Ordinal) ? $"`` {codeText} ``" : $"`{codeText}`";
            default:
                var children = InlineChildren(node, depth);
                return Blocks.Contains(node.Name) ? " " + children + " " : children;
        }
    }

    private string InlineChildren(HtmlNode node, int depth)
    {
        var builder = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            builder.Append(Inline(child, depth + 1));
        }

        return builder.ToString();
    }

    private static string Wrap(string text, string marker)
    {
        var inner = text.Trim();
        if (inner.Length == 0)
        {
            return text;
        }

        var lead = text.Length - text.TrimStart().Length > 0 ? " " : string.Empty;
        var trail = text.Length - text.TrimEnd().Length > 0 ? " " : string.Empty;
        return lead + marker + inner + marker + trail;
    }

    private string? LinkTarget(string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
        {
            return null;
        }

        if (!Uri.TryCreate(_baseUri, WebUtility.HtmlDecode(href.Trim()), out var target) || target.Scheme is not ("http" or "https" or "mailto"))
        {
            return null;
        }

        return target.AbsoluteUri.Replace(" ", "%20", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenStyle();
}
