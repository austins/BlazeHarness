using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Text;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace BlazeHarness.Web.Services;

/// <summary>
/// Colors code blocks on the server, so the browser needs no script and the page sends no code anywhere.
/// </summary>
/// <remarks>
/// Only the languages that TextMateSharp has a grammar for are colored. Other code is shown as plain text.
/// The grammars tokenize one line at a time, so no pattern ever sees more than one line.
/// </remarks>
public static class SyntaxHighlighter
{
    private const int MaxLength = 20_000;
    private const int MaxLinesLength = 100_000;

    /// <summary>
    /// The time one piece of code may take to color before it is shown as plain text.
    /// </summary>
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(1);

    private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);

    private static readonly Registry Registry = new(Options);

    /// <summary>
    /// The scope name of each grammar, by language id, alias and file extension.
    /// </summary>
    private static readonly FrozenDictionary<string, string> ScopeNames = CreateScopeNames();

    /// <summary>
    /// The CSS class of each token kind, by the scope name prefix that marks it.
    /// </summary>
    /// <remarks>
    /// The first prefix that matches a token's innermost scope wins, so a more specific prefix comes before a general one.
    /// </remarks>
    private static readonly (string Prefix, string Kind)[] Kinds =
    [
        ("comment", "comment"),
        ("support.type.property-name", "attr"),
        ("entity.other.attribute-name", "attr"),
        ("meta.object-literal.key", "attr"),
        ("markup.underline.link", "attr"),
        ("string", "string"),
        ("constant.character.escape", "string"),
        ("markup.inline.raw", "string"),
        ("constant.numeric", "number"),
        ("constant.language", "keyword"),
        ("keyword.operator", string.Empty),
        ("keyword", "keyword"),
        ("storage", "keyword"),
        ("markup.heading", "keyword"),
        ("entity.name.function", "function"),
        ("support.function", "function"),
        ("entity.name.type", "type"),
        ("entity.name.class", "type"),
        ("entity.name.namespace", "type"),
        ("entity.name.tag", "type"),
        ("entity.other.inherited-class", "type"),
        ("support.type", "type"),
        ("support.class", "type"),
    ];

    private static readonly Lock GrammarsLock = new();

    private static readonly Dictionary<string, IGrammar?> Grammars = new(StringComparer.Ordinal);

    /// <summary>
    /// Color a piece of code.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="info">The info string after the opening code fence, whose first word names the language.</param>
    /// <returns>HTML for the inside of a code element, with token spans, or null when the language is unknown, the code is too long or it can't be colored in time.</returns>
    public static string? TryHighlight(string code, string? info)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Highlight(code, info, MaxLength) is { } lines ? string.Join('\n', lines) : null;
    }

    /// <summary>
    /// Color a piece of code that spans several lines and return the colored HTML of each line.
    /// </summary>
    /// <remarks>
    /// Every line is well-formed HTML on its own: a token that spans lines, such as a block comment, gets a span on each line.
    /// </remarks>
    /// <param name="code">The lines of code, separated by <c>\n</c>.</param>
    /// <param name="info">The language name or file extension.</param>
    /// <returns>One HTML string per line of <paramref name="code"/>, or null when the language is unknown, the code is too long or it can't be colored in time.</returns>
    public static IReadOnlyList<string>? TryHighlightLines(string code, string? info)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Highlight(code, info, MaxLinesLength);
    }

    private static List<string>? Highlight(string code, string? info, int maxLength)
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

        if (!ScopeNames.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var scopeName) || GrammarFor(scopeName) is not { } grammar)
        {
            return null;
        }

        var started = Stopwatch.GetTimestamp();
        var lines = new List<string>();
        var builder = new StringBuilder();
        IStateStack? state = null;
        foreach (var range in code.AsSpan().Split('\n'))
        {
            var remaining = TimeLimit - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var line = code.AsMemory(range);
            var result = grammar.TokenizeLine(line, state, remaining);

            // A line that ran out of time ends with tokens short of its end, and the lines after it would start in the wrong state.
            if (result.Tokens.Length == 0 || result.Tokens[^1].EndIndex < line.Length)
            {
                return null;
            }

            state = result.RuleStack;
            lines.Add(ToHtml(line.Span, result.Tokens, builder));
        }

        return lines;
    }

    private static IGrammar? GrammarFor(string scopeName)
    {
        lock (GrammarsLock)
        {
            if (!Grammars.TryGetValue(scopeName, out var grammar))
            {
                grammar = Registry.LoadGrammar(scopeName);
                Grammars[scopeName] = grammar;
            }

            return grammar;
        }
    }

    private static string ToHtml(ReadOnlySpan<char> line, IToken[] tokens, StringBuilder builder)
    {
        builder.Clear();
        string? open = null;
        foreach (var token in tokens)
        {
            var start = Math.Min(token.StartIndex, line.Length);
            var end = Math.Min(token.EndIndex, line.Length);
            if (end <= start)
            {
                continue;
            }

            var kind = KindOf(token.Scopes);
            if (kind != open)
            {
                if (open is not null)
                {
                    builder.Append("</span>");
                }

                if (kind is not null)
                {
                    builder.Append("<span class=\"tok-").Append(kind).Append("\">");
                }

                open = kind;
            }

            builder.Append(WebUtility.HtmlEncode(line[start..end].ToString()));
        }

        if (open is not null)
        {
            builder.Append("</span>");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Find the CSS class of a token from its scopes, looking from the innermost scope outwards.
    /// </summary>
    /// <returns>The class without its <c>tok-</c> prefix, or null for text that is not colored.</returns>
    private static string? KindOf(List<string> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            foreach (var (prefix, kind) in Kinds)
            {
                if (scopes[i].StartsWith(prefix, StringComparison.Ordinal)
                    && (scopes[i].Length == prefix.Length || scopes[i][prefix.Length] == '.'))
                {
                    return kind.Length == 0 ? null : kind;
                }
            }
        }

        return null;
    }

    private static FrozenDictionary<string, string> CreateScopeNames()
    {
        var languages = Options.GetAvailableLanguages();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // An id wins over an alias, and an alias over an extension, where two languages claim the same name.
        foreach (var id in languages.Select(language => language.Id))
        {
            names.TryAdd(id, Options.GetScopeByLanguageId(id));
        }

        foreach (var language in languages)
        {
            foreach (var alias in language.Aliases ?? [])
            {
                names.TryAdd(alias, Options.GetScopeByLanguageId(language.Id));
            }
        }

        foreach (var language in languages)
        {
            foreach (var extension in language.Extensions ?? [])
            {
                names.TryAdd(extension.TrimStart('.'), Options.GetScopeByLanguageId(language.Id));
            }
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
