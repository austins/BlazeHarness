using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Turns a downloaded body into text for the model, by its content type.
/// </summary>
internal sealed partial class ContentExtractor
{
    private static readonly string[] TextSubtypes = ["json", "xml", "javascript", "yaml", "x-yaml", "csv", "markdown", "x-markdown", "toml", "x-sh", "rss+xml", "atom+xml"];

    private readonly IPdfTextExtractor _pdf;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentExtractor"/> class.
    /// </summary>
    /// <param name="pdf">Reads PDF files.</param>
    public ContentExtractor(IPdfTextExtractor pdf)
    {
        _pdf = pdf;
    }

    /// <summary>
    /// Read a body.
    /// </summary>
    /// <param name="response">The response whose body to read; its status must be a success.</param>
    /// <param name="cancellationToken">A token that stops long conversions.</param>
    /// <returns>The document.</returns>
    /// <exception cref="WebFetchException">The content type is not one this tool reads, or the content can't be read.</exception>
    public ExtractedDocument Extract(FetchResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var kind = KindOf(response);
        switch (kind)
        {
            case Kind.Pdf:
                if (response.Truncated)
                {
                    throw new WebFetchException("The PDF is too large to read.");
                }

                return _pdf.Extract(response.Body, cancellationToken);
            case Kind.Html:
                return Truncation(ReadHtml(response, cancellationToken), response);
            case Kind.Text:
                var text = Decode(response.Body, response.Charset).Trim('\uFEFF');
                return Truncation(new ExtractedDocument(string.Empty, text.Trim(), []), response);
            default:
                throw new WebFetchException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"This tool reads web pages, PDF files and text, but the address returned '{response.ContentType ?? "unknown"}' content."));
        }
    }

    private static ExtractedDocument Truncation(ExtractedDocument document, FetchResponse response)
    {
        return response.Truncated ? document with { Notice = "The download was cut off at its size limit, so the end of the document is missing." } : document;
    }

    private static Kind KindOf(FetchResponse response)
    {
        var type = response.ContentType ?? string.Empty;
        var body = response.Body;
        if (type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) || (body.Length > 4 && body[0] == '%' && body[1] == 'P' && body[2] == 'D' && body[3] == 'F'))
        {
            return Kind.Pdf;
        }

        if (type.Equals("text/html", StringComparison.OrdinalIgnoreCase) || type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            return Kind.Html;
        }

        if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || Array.Exists(TextSubtypes, subtype => type.EndsWith(subtype, StringComparison.OrdinalIgnoreCase)))
        {
            return Kind.Text;
        }

        // No content type at all: sniff for a page, and otherwise treat readable bytes as text.
        return type.Length == 0 && LooksLikeText(body) ? (LooksLikeHtml(body) ? Kind.Html : Kind.Text) : Kind.Other;
    }

    private static bool LooksLikeHtml(byte[] body)
    {
        var head = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 512));
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase) || head.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeText(byte[] body)
    {
        var sample = body.AsSpan(0, Math.Min(body.Length, 2048));
        return !sample.Contains((byte)0);
    }

    private static string Decode(byte[] body, string? charset)
    {
        return EncodingFor(charset).GetString(body);
    }

    private static Encoding EncodingFor(string? charset)
    {
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // An unknown charset name: UTF-8 is the best guess.
            }
        }

        return Encoding.UTF8;
    }

    private static Encoding? SniffMetaCharset(byte[] body)
    {
        var head = Encoding.ASCII.GetString(body, 0, Math.Min(body.Length, 4096));
        var match = MetaCharset().Match(head);
        return match.Success ? EncodingFor(match.Groups[1].Value) : null;
    }

    private static ExtractedDocument ReadHtml(FetchResponse response, CancellationToken cancellationToken)
    {
        try
        {
            return ConvertHtml(response, cancellationToken);
        }
        catch (Exception ex) when (ex is not (WebFetchException or OperationCanceledException))
        {
            throw new WebFetchException("The page could not be read.");
        }
    }

    private static ExtractedDocument ConvertHtml(FetchResponse response, CancellationToken cancellationToken)
    {
        var encoding = string.IsNullOrEmpty(response.Charset) ? SniffMetaCharset(response.Body) ?? Encoding.UTF8 : EncodingFor(response.Charset);
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 500 };
        document.LoadHtml(encoding.GetString(response.Body));
        return HtmlToMarkdown.Convert(document, response.FinalUri, cancellationToken);
    }

    [GeneratedRegex(@"charset\s*=\s*[""']?([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetaCharset();

    private enum Kind
    {
        Pdf,
        Html,
        Text,
        Other,
    }
}
