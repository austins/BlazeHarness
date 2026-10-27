using BlazeHarness.Web.Hosting;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazeHarness.Web.Services;

/// <summary>
/// Turns model output into safe HTML.
/// </summary>
/// <remarks>
/// Model text is untrusted. Raw HTML is shown as text, only http, https and mailto links are kept, and images are never
/// loaded from a URL, because a URL chosen by the model can leak data through its query string.
/// </remarks>
public sealed partial class MarkdownRenderer
{
    private const int CacheCapacity = 256;

    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _cache = [];
    private readonly LinkedList<KeyValuePair<string, string>> _recent = new();
    private readonly object _gate = new();

    /// <summary>
    /// Convert markdown to HTML.
    /// </summary>
    /// <param name="markdown">The markdown text.</param>
    /// <param name="sessionId">The chat the text belongs to. An image that points to a path inside its folder is shown through the chat's file endpoint. Without it, no image is shown.</param>
    /// <returns>Safe HTML.</returns>
    public string ToHtml(string markdown, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length == 0)
        {
            return string.Empty;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new CopyableCodeBlockRenderer());

        var document = Markdown.Parse(markdown, _pipeline);
        Sanitize(document, sessionId);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// Convert the markdown of a finished message, remembering recent results so the same message is not parsed twice.
    /// </summary>
    /// <param name="markdown">The markdown text.</param>
    /// <param name="sessionId">The chat the text belongs to. An image that points to a path inside its folder is shown through the chat's file endpoint. Without it, no image is shown.</param>
    /// <returns>Safe HTML.</returns>
    public string ToCachedHtml(string markdown, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var key = sessionId is null ? markdown : sessionId + '\0' + markdown;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);
                return hit.Value.Value;
            }
        }

        var html = ToHtml(markdown, sessionId);
        lock (_gate)
        {
            if (!_cache.ContainsKey(key))
            {
                _cache[key] = _recent.AddFirst(new KeyValuePair<string, string>(key, html));
                if (_cache.Count > CacheCapacity && _recent.Last is { } oldest)
                {
                    _cache.Remove(oldest.Value.Key);
                    _recent.RemoveLast();
                }
            }
        }

        return html;
    }

    // A path rather than a URL: no scheme (a single letter before the colon is a Windows drive) and not protocol-relative.
    private static bool IsFolderPath(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
            && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith(@"\\", StringComparison.Ordinal)
            && !SchemePattern().IsMatch(url);
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.\\-]+:", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SchemePattern();

    private static bool IsAllowed(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && AllowedSchemes.Contains(uri.Scheme);
    }

    private static void Sanitize(MarkdownDocument document, string? sessionId)
    {
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            var url = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
            if (IsAllowed(url))
            {
                var link = new LinkInline(url, string.Empty);
                link.AppendChild(new LiteralInline(autolink.Url));
                MakeExternal(link);
                autolink.ReplaceBy(link);
            }
            else
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (sessionId is not null && link.IsImage && IsFolderPath(link.Url))
            {
                link.Url = AttachmentEndpoints.FileAddressOf(sessionId, link.Url!);
                var attributes = link.GetAttributes();
                attributes.AddClass("msg-image");
                attributes.AddProperty("loading", "lazy");
                continue;
            }

            if (!IsAllowed(link.Url))
            {
                Unwrap(link);
                continue;
            }

            if (link.IsImage)
            {
                link.IsImage = false;
                if (link.FirstChild is null)
                {
                    link.AppendChild(new LiteralInline(link.Url ?? string.Empty));
                }
            }

            MakeExternal(link);
        }
    }

    private static void MakeExternal(LinkInline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddProperty("rel", "noopener noreferrer nofollow");
        attributes.AddProperty("target", "_blank");
    }

    // Keep the visible text of a link that is not allowed, without the link.
    private static void Unwrap(LinkInline link)
    {
        foreach (var child in link.ToList())
        {
            child.Remove();
            link.InsertBefore(child);
        }

        link.Remove();
    }

    private sealed class CopyableCodeBlockRenderer : CodeBlockRenderer
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock obj)
        {
            renderer.Write("<div class=\"code\"><button type=\"button\" class=\"btn btn-sm btn-ghost code-copy\" data-copy>Copy</button>");
            if (obj is FencedCodeBlock { Info: { Length: > 0 } info } && SyntaxHighlighter.TryHighlight(obj.Lines.ToString(), info) is { } html)
            {
                renderer.Write("<pre><code class=\"language-").WriteEscape(info.Split(' ', '\t', '{')[0]).Write("\">").Write(html).WriteLine("</code></pre>");
            }
            else
            {
                base.Write(renderer, obj);
            }

            renderer.WriteLine("</div>");
        }
    }
}
