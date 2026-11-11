using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Splits text into the words the built-in searches compare, without a language model.
/// </summary>
/// <remarks>
/// <para>
/// Text is split on anything that is not part of a word and on camelCase. A combining mark stays in its word, so scripts such as Devanagari,
/// which write vowels as marks, keep their words whole. Accents on Latin, Greek and Cyrillic letters are removed, so <c>café</c> matches <c>cafe</c>.
/// </para>
/// <para>
/// Chinese, Japanese, Korean and Thai do not separate words, so their runs are matched by pairs of characters.
/// Plural endings and filler words are handled for English only; other languages are matched by the word as written.
/// </para>
/// </remarks>
internal static class SearchWords
{
    /// <summary>
    /// The English filler words left out by default.
    /// </summary>
    public static readonly FrozenSet<string> EnglishStopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "can", "do", "for", "from", "in", "into", "is", "it", "me", "of", "on", "or", "that", "the", "this", "to", "with",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The highest code point that <see cref="Fold"/> removes accents after: the end of the Cyrillic block.
    /// </summary>
    private const int LastFoldedBase = 0x052F;

    /// <summary>
    /// Split text into lower-cased, accent-free words.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="stopWords">The words to leave out, or null for <see cref="EnglishStopWords"/>.</param>
    /// <returns>The words in the order they appear, with stop words left out.</returns>
    public static List<string> Tokenize(string text, IReadOnlySet<string>? stopWords = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        stopWords ??= EnglishStopWords;
        List<string> words = [];
        // Each unit is a letter or digit with the marks that follow it.
        List<string> current = [];
        var currentIsPaired = false;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            if (currentIsPaired)
            {
                if (current.Count == 1)
                {
                    words.Add(current[0]);
                }
                else
                {
                    for (var i = 0; i + 1 < current.Count; i++)
                    {
                        words.Add(current[i] + current[i + 1]);
                    }
                }

                current.Clear();
                return;
            }

            var word = Stem(Fold(string.Concat(current)));
            current.Clear();
            if (word.Length > 0 && !stopWords.Contains(word) && (word.Length > 1 || char.IsDigit(word[0])))
            {
                words.Add(word);
            }
        }

        var previous = default(Rune);
#pragma warning disable IDISP004 // foreach disposes the enumerator.
        foreach (var rune in text.EnumerateRunes())
#pragma warning restore IDISP004
        {
            if (IsMark(rune))
            {
                // A mark belongs to the letter before it; one with nothing before it is dropped.
                if (current.Count > 0)
                {
                    current[^1] += rune.ToString();
                }

                continue;
            }

            if (!Rune.IsLetterOrDigit(rune))
            {
                Flush();
                previous = default;
                continue;
            }

            var paired = IsPairedScript(rune);
            if (paired != currentIsPaired || (!paired && Rune.IsUpper(rune) && Rune.IsLower(previous)))
            {
                Flush();
            }

            currentIsPaired = paired;
            current.Add(rune.ToString());
            previous = rune;
        }

        Flush();
        return words;
    }

    /// <summary>
    /// Reduce an English plural to its singular, so <c>files</c> matches <c>file</c>.
    /// </summary>
    /// <param name="word">A lower-case word.</param>
    /// <returns>The word without its plural ending.</returns>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        return word.Length > 3 && word[^1] == 's' && word[^2] != 's' ? word[..^1] : word;
    }

    /// <summary>
    /// Lower-case a word and remove the accents from its Latin, Greek and Cyrillic letters.
    /// </summary>
    /// <remarks>Marks after letters of other scripts are kept, because there they are part of the spelling, not an accent.</remarks>
    /// <param name="word">The word.</param>
    /// <returns>The folded word.</returns>
    private static string Fold(string word)
    {
        // Decomposing first lets a capital with a dot or accent (Turkish İ) lower-case to its base letter.
#pragma warning disable CA1308 // Words are compared in lower case; the text is never shown.
        var decomposed = word.Normalize(NormalizationForm.FormD).ToLowerInvariant();
#pragma warning restore CA1308
        var folded = new StringBuilder(decomposed.Length);
        var lastBase = 0;
#pragma warning disable IDISP004 // foreach disposes the enumerator.
        foreach (var rune in decomposed.EnumerateRunes())
#pragma warning restore IDISP004
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                if (lastBase <= LastFoldedBase)
                {
                    continue;
                }
            }
            else
            {
                lastBase = rune.Value;
            }

            folded.Append(rune);
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsMark(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsPairedScript(Rune rune)
    {
        return rune.Value is (>= 0x3040 and <= 0x30FF) or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or (>= 0xAC00 and <= 0xD7AF)
            or (>= 0x0E00 and <= 0x0E7F) or (>= 0xF900 and <= 0xFAFF) or (>= 0x20000 and <= 0x3134F);
    }
}
