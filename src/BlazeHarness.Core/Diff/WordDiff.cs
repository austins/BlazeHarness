namespace BlazeHarness.Core.Diff;

/// <summary>
/// Finds which words changed between two versions of a line.
/// </summary>
public static class WordDiff
{
    private const int MaxLength = 2000;
    private const int MaxTokenProduct = 250_000;
    private const double MaxChangedShare = 0.7;

    /// <summary>
    /// Compare two lines word by word.
    /// </summary>
    /// <remarks>
    /// A word is a run of letters, digits and underscores; a run of white space is one token and every other character is its own token.
    /// Nothing is marked when the lines are identical, when they are too long to compare cheaply, or when more than 70% of the longer line
    /// changed, because marking nearly everything is only noise.
    /// </remarks>
    /// <param name="oldText">The line before the change.</param>
    /// <param name="newText">The line after the change.</param>
    /// <returns>The ranges of <paramref name="oldText"/> that were removed and of <paramref name="newText"/> that were added, each in ascending order and without overlaps.</returns>
    public static (IReadOnlyList<TextRange> Removed, IReadOnlyList<TextRange> Added) Compare(string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        if (oldText.Length > MaxLength || newText.Length > MaxLength || oldText == newText)
        {
            return ([], []);
        }

        var oldTokens = Tokenize(oldText);
        var newTokens = Tokenize(newText);
        if ((long)oldTokens.Count * newTokens.Count > MaxTokenProduct)
        {
            return ([], []);
        }

        var (oldKept, newKept) = MatchTokens(oldText, oldTokens, newText, newTokens);
        var removed = Changed(oldTokens, oldKept);
        var added = Changed(newTokens, newKept);
        var changed = Math.Max(removed.Sum(range => range.Length), added.Sum(range => range.Length));
        return changed > Math.Max(oldText.Length, newText.Length) * MaxChangedShare ? ([], []) : (removed, added);
    }

    private static List<TextRange> Tokenize(string text)
    {
        List<TextRange> tokens = [];
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var c = text[i];
            if (IsWordChar(c))
            {
                while (i < text.Length && IsWordChar(text[i]))
                {
                    i++;
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }
            else
            {
                i += char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            }

            tokens.Add(new TextRange(start, i - start));
        }

        return tokens;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Find the longest common run of tokens, in order.
    /// </summary>
    /// <returns>For each side, which tokens belong to the common run.</returns>
    private static (bool[] OldKept, bool[] NewKept) MatchTokens(string oldText, List<TextRange> oldTokens, string newText, List<TextRange> newTokens)
    {
        var n = oldTokens.Count;
        var m = newTokens.Count;
        var lengths = new int[n + 1][];
        for (var i = 0; i <= n; i++)
        {
            lengths[i] = new int[m + 1];
        }

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[i][j] = Same(oldText, oldTokens[i], newText, newTokens[j])
                    ? lengths[i + 1][j + 1] + 1
                    : Math.Max(lengths[i + 1][j], lengths[i][j + 1]);
            }
        }

        var oldKept = new bool[n];
        var newKept = new bool[m];
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (Same(oldText, oldTokens[a], newText, newTokens[b]))
            {
                oldKept[a++] = true;
                newKept[b++] = true;
            }
            else if (lengths[a + 1][b] >= lengths[a][b + 1])
            {
                a++;
            }
            else
            {
                b++;
            }
        }

        return (oldKept, newKept);
    }

    private static bool Same(string left, TextRange leftToken, string right, TextRange rightToken)
        => leftToken.Length == rightToken.Length
        && string.CompareOrdinal(left, leftToken.Start, right, rightToken.Start, leftToken.Length) == 0;

    private static List<TextRange> Changed(List<TextRange> tokens, bool[] kept)
    {
        List<TextRange> ranges = [];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (kept[i])
            {
                continue;
            }

            var token = tokens[i];
            if (ranges.Count > 0 && ranges[^1].Start + ranges[^1].Length == token.Start)
            {
                ranges[^1] = ranges[^1] with { Length = ranges[^1].Length + token.Length };
            }
            else
            {
                ranges.Add(token);
            }
        }

        return ranges;
    }
}
