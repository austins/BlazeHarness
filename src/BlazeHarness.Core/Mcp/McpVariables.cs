using System.Text;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Replaces <c>${VAR}</c> and <c>${VAR:-default}</c> in text, the way <c>mcp.json</c> files of other tools use them.
/// </summary>
internal static class McpVariables
{
    /// <summary>
    /// Replace every variable reference in a text.
    /// </summary>
    /// <remarks>
    /// A name is letters, digits and <c>_</c>, not starting with a digit. <c>${VAR:-default}</c> uses the default when the variable is unset or empty.
    /// A <c>${</c> that is not a valid reference is left as it is.
    /// </remarks>
    /// <param name="text">The text with references.</param>
    /// <param name="lookup">Returns the value of a variable, or null when it is not set.</param>
    /// <param name="result">The text with references replaced, when this returns true.</param>
    /// <param name="missing">The name of the first variable that is not set and has no default, when this returns false.</param>
    /// <returns>False when a variable has no value and no default.</returns>
    public static bool TryExpand(string text, Func<string, string?> lookup, out string result, out string? missing)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lookup);

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{' && text.IndexOf('}', i + 2) is var end and > 0
                && Parse(text.AsSpan(i + 2, end - i - 2), out var name, out var fallback))
            {
                var value = lookup(name);
                if (fallback is not null && string.IsNullOrEmpty(value))
                {
                    value = fallback;
                }

                if (value is null)
                {
                    result = text;
                    missing = name;
                    return false;
                }

                builder.Append(value);
                i = end + 1;
                continue;
            }

            builder.Append(text[i]);
            i++;
        }

        result = builder.ToString();
        missing = null;
        return true;
    }

    private static bool Parse(ReadOnlySpan<char> body, out string name, out string? fallback)
    {
        var colon = body.IndexOf(":-", StringComparison.Ordinal);
        var nameSpan = colon >= 0 ? body[..colon] : body;
        fallback = colon >= 0 ? body[(colon + 2)..].ToString() : null;
        name = nameSpan.ToString();
        if (nameSpan.IsEmpty || char.IsAsciiDigit(nameSpan[0]))
        {
            return false;
        }

        foreach (var c in nameSpan)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
