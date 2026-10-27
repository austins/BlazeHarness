using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;
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

    private static readonly Dictionary<string, string> LanguageIds = new(StringComparer.OrdinalIgnoreCase)
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
    };

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
        if (code.Length == 0 || code.Length > MaxLength || info is null)
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
