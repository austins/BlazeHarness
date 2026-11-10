using System.Text;

namespace BlazeHarness.Core.Git;

/// <summary>
/// Checks and suggests branch names.
/// </summary>
public static class GitBranchName
{
    private const int MaxSuggestedLength = 40;

    /// <summary>
    /// Check a branch name against git's rules (<c>git check-ref-format --branch</c>).
    /// </summary>
    /// <remarks>Names that start with <c>-</c> are refused too, so a name can never be read as an option.</remarks>
    /// <param name="name">The name to check.</param>
    /// <returns>True when git would accept the name as a new branch.</returns>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "@" || name[0] is '-' or '/' || name[^1] is '/' or '.'
            || name.Contains("..", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal) || name.Contains("@{", StringComparison.Ordinal))
        {
            return false;
        }

        if (name.Any(c => c <= ' ' || c == '\x7f' || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            return false;
        }

        return !name.Split('/').Any(part => part.StartsWith('.') || part.EndsWith(".lock", StringComparison.Ordinal));
    }

    /// <summary>
    /// Make a branch name from free text such as a chat title.
    /// </summary>
    /// <param name="text">The text, or null.</param>
    /// <param name="fallback">The name to use when the text has no letters or digits.</param>
    /// <returns>Lowercase words joined by <c>-</c>, at most 40 characters.</returns>
    public static string Suggest(string? text, string fallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);
        var name = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                name.Append(char.ToLowerInvariant(c));
            }
            else if (name.Length > 0 && name[^1] != '-')
            {
                name.Append('-');
            }
        }

        var result = name.ToString().TrimEnd('-');
        if (result.Length > MaxSuggestedLength)
        {
            var cut = result.LastIndexOf('-', MaxSuggestedLength);
            result = cut > 0 ? result[..cut] : result[..MaxSuggestedLength];
        }

        return result.Length == 0 ? fallback : result;
    }
}
