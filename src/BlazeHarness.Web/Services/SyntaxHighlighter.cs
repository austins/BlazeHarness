using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;
using System.Collections.Frozen;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Web.Services;

/// <summary>
/// Colors code blocks on the server, so the browser needs no script and the page sends no code anywhere.
/// </summary>
/// <remarks>
/// Only the languages that the ColorCode library knows are colored. Other code is shown as plain text.
/// </remarks>
public static class SyntaxHighlighter
{
    private const int MaxLength = 20_000;
    private const int MaxLinesLength = 100_000;

    private static readonly FrozenDictionary<string, string> LanguageIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = LanguageId.CSharp,
        ["cs"] = LanguageId.CSharp,
        ["csharp"] = LanguageId.CSharp,
        ["js"] = LanguageId.JavaScript,
        ["jsx"] = LanguageId.JavaScript,
        ["mjs"] = LanguageId.JavaScript,
        ["javascript"] = LanguageId.JavaScript,
        ["ts"] = LanguageId.TypeScript,
        ["tsx"] = LanguageId.TypeScript,
        ["typescript"] = LanguageId.TypeScript,
        ["py"] = LanguageId.Python,
        ["python"] = LanguageId.Python,
        ["json"] = LanguageId.Json,
        ["jsonc"] = LanguageId.Json,
        ["html"] = LanguageId.Html,
        ["htm"] = LanguageId.Html,
        ["xml"] = LanguageId.Xml,
        ["xaml"] = LanguageId.Xml,
        ["csproj"] = LanguageId.Xml,
        ["svg"] = LanguageId.Xml,
        ["css"] = LanguageId.Css,
        ["sql"] = LanguageId.Sql,
        ["java"] = LanguageId.Java,
        ["php"] = LanguageId.Php,
        ["powershell"] = LanguageId.PowerShell,
        ["pwsh"] = LanguageId.PowerShell,
        ["ps1"] = LanguageId.PowerShell,
        ["c"] = LanguageId.Cpp,
        ["h"] = LanguageId.Cpp,
        ["cpp"] = LanguageId.Cpp,
        ["c++"] = LanguageId.Cpp,
        ["hpp"] = LanguageId.Cpp,
        ["f#"] = LanguageId.FSharp,
        ["fsharp"] = LanguageId.FSharp,
        ["haskell"] = LanguageId.Haskell,
        ["hs"] = LanguageId.Haskell,
        ["md"] = LanguageId.Markdown,
        ["markdown"] = LanguageId.Markdown,
        ["vb"] = LanguageId.VbDotNet,
        ["vbnet"] = LanguageId.VbDotNet,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ReaderWriterLockSlim CompileLock = new();

    private static readonly LanguageParser Parser = new(
        new LanguageCompiler(new Dictionary<string, CompiledLanguage>(), CompileLock),
        new LanguageRepository());

    /// <summary>
    /// Color a piece of code.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="info">The info string after the opening code fence, whose first word names the language.</param>
    /// <returns>HTML for the inside of a code element, with token spans, or null when the language is unknown, the code is too long or it can't be parsed.</returns>
    public static string? TryHighlight(string code, string? info)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Highlight(code, info, MaxLength);
    }

    /// <summary>
    /// Color a piece of code that spans several lines and return the colored HTML of each line.
    /// </summary>
    /// <remarks>
    /// A token that spans lines, such as a block comment, is closed at the end of each line and opened again on the next,
    /// so every line is well-formed HTML on its own.
    /// </remarks>
    /// <param name="code">The lines of code, separated by <c>\n</c>.</param>
    /// <param name="info">The language name or file extension.</param>
    /// <returns>One HTML string per line of <paramref name="code"/>, or null when the language is unknown, the code is too long or it can't be parsed.</returns>
    public static IReadOnlyList<string>? TryHighlightLines(string code, string? info)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Highlight(code, info, MaxLinesLength) is { } html ? SplitLines(html) : null;
    }

    private static string? Highlight(string code, string? info, int maxLength)
    {
        if (code.Length == 0 || code.Length > maxLength || info is null)
        {
            return null;
        }

        var name = info.AsSpan().Trim();
        var space = name.IndexOfAny(' ', '\t', '{');
        if (space >= 0)
        {
            name = name[..space];
        }

        if (!LanguageIds.TryGetValue(name.ToString(), out var id) || Languages.FindById(id) is not { } language)
        {
            return null;
        }

        // The parser reports the code in consecutive pieces, each with scopes relative to that piece.
        var builder = new StringBuilder(code.Length * 2);
        try
        {
            Parser.Parse(code, language, (text, scopes) =>
            {
                var position = 0;
                Write(builder, text, scopes, ref position, text.Length);
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return builder.ToString();
    }

    private static List<string> SplitLines(string html)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        var open = new List<string>();
        var i = 0;
        while (i < html.Length)
        {
            switch (html[i])
            {
                // Text never contains a raw '<': the highlighter encodes it, so this is always a tag.
                case '<':
                    var end = html.IndexOf('>', i);
                    var tag = html[i..(end + 1)];
                    if (tag.StartsWith("</", StringComparison.Ordinal))
                    {
                        open.RemoveAt(open.Count - 1);
                    }
                    else
                    {
                        open.Add(tag);
                    }

                    line.Append(tag);
                    i = end + 1;
                    break;
                case '\n':
                    line.Insert(line.Length, "</span>", open.Count);
                    lines.Add(line.ToString());
                    line.Clear();
                    open.ForEach(openTag => line.Append(openTag));
                    i++;
                    break;
                default:
                    line.Append(html[i]);
                    i++;
                    break;
            }
        }

        lines.Add(line.ToString());
        return lines;
    }

    private static void Write(StringBuilder builder, string text, IList<Scope> scopes, ref int position, int end)
    {
        foreach (var scope in scopes.OrderBy(s => s.Index))
        {
            if (scope.Index < position || scope.Index + scope.Length > end)
            {
                continue;
            }

            Append(builder, text, position, scope.Index);
            position = scope.Index;
            var kind = ClassOf(scope.Name);
            if (kind is not null)
            {
                builder.Append("<span class=\"tok-").Append(kind).Append("\">");
            }

            Write(builder, text, scope.Children, ref position, scope.Index + scope.Length);
            Append(builder, text, position, scope.Index + scope.Length);
            position = scope.Index + scope.Length;
            if (kind is not null)
            {
                builder.Append("</span>");
            }
        }

        if (end == text.Length)
        {
            Append(builder, text, position, end);
            position = end;
        }
    }

    private static void Append(StringBuilder builder, string text, int from, int to)
    {
        if (to > from)
        {
            builder.Append(WebUtility.HtmlEncode(text[from..to]));
        }
    }

    private static string? ClassOf(string scopeName) => scopeName switch
    {
        ScopeName.Comment or ScopeName.XmlDocComment or ScopeName.HtmlComment or ScopeName.XmlComment => "comment",
        ScopeName.String or ScopeName.StringCSharpVerbatim or ScopeName.JsonString or ScopeName.HtmlAttributeValue
            or ScopeName.XmlAttributeValue or ScopeName.CssPropertyValue or ScopeName.StringEscape => "string",
        ScopeName.Keyword or ScopeName.ControlKeyword or ScopeName.PreprocessorKeyword or ScopeName.PseudoKeyword
            or ScopeName.BuiltinValue or ScopeName.JsonConst or ScopeName.Predefined => "keyword",
        ScopeName.ClassName or ScopeName.Type or ScopeName.TypeVariable or ScopeName.NameSpace or ScopeName.Constructor
            or ScopeName.PowerShellType or ScopeName.HtmlElementName or ScopeName.XmlName or ScopeName.CssSelector => "type",
        ScopeName.Number or ScopeName.JsonNumber => "number",
        ScopeName.HtmlAttributeName or ScopeName.XmlAttribute or ScopeName.Attribute or ScopeName.CssPropertyName
            or ScopeName.JsonKey or ScopeName.PowerShellParameter or ScopeName.PowerShellVariable or ScopeName.PowerShellAttribute => "attr",
        ScopeName.BuiltinFunction or ScopeName.SqlSystemFunction or ScopeName.PowerShellCommand or ScopeName.Intrinsic => "function",
        _ => null,
    };

    private sealed class LanguageRepository : ILanguageRepository
    {
        public IEnumerable<ILanguage> All => Languages.All;

        public ILanguage? FindById(string languageId) => Languages.FindById(languageId);

        public void Load(ILanguage language) => Languages.Load(language);
    }
}
