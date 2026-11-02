using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// Ranks tools against a short query with BM25 over their names, servers, descriptions and parameters.
/// </summary>
/// <remarks>
/// Matching is by word, so a query has to use the words the tools are described in. Text is split on anything that is not a letter or digit and on
/// camelCase, and runs of Chinese, Japanese, Korean or Thai text are matched by pairs of characters because those scripts do not separate words.
/// </remarks>
internal static class McpToolRanker
{
    // How much a word counts in each place; a name says more about what a tool does than one of its parameters.
    private const double NameWeight = 4;
    private const double ServerWeight = 3;
    private const double DescriptionWeight = 2;
    private const double ParameterWeight = 1;
    private const double SynonymWeight = 0.5;

    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int MaxSchemaDepth = 4;
    private const int MaxParameterWords = 400;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "can", "do", "for", "from", "in", "into", "is", "it", "me", "of", "on", "or", "please", "that", "the", "this", "to", "tool", "use", "with",
    };

    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["add"] = ["create", "insert", "new"],
        ["create"] = ["add", "new"],
        ["delete"] = ["remove"],
        ["remove"] = ["delete"],
        ["doc"] = ["documentation"],
        ["documentation"] = ["doc"],
        ["edit"] = ["modify", "update"],
        ["modify"] = ["edit", "update"],
        ["update"] = ["edit", "modify"],
        ["fetch"] = ["get", "read", "retrieve"],
        ["get"] = ["fetch", "read", "retrieve"],
        ["read"] = ["fetch", "get"],
        ["find"] = ["lookup", "query", "search"],
        ["lookup"] = ["find", "search"],
        ["query"] = ["find", "search"],
        ["search"] = ["find", "lookup", "query"],
        ["list"] = ["browse", "enumerate"],
        ["browse"] = ["list"],
        ["summarize"] = ["summary"],
        ["summary"] = ["summarize"],
    };

    /// <summary>
    /// Rank tools by how well they match a query.
    /// </summary>
    /// <param name="tools">The tools to rank.</param>
    /// <param name="query">What the model is looking for.</param>
    /// <returns>The tools that match, best first; empty when no word of the query appears in any tool.</returns>
    public static IReadOnlyList<(McpToolListing Tool, double Score)> Rank(IReadOnlyList<McpToolListing> tools, string query)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(query);

        var terms = QueryTerms(query);
        if (terms.Count == 0 || tools.Count == 0)
        {
            return [];
        }

        var documents = tools.Select(Index).ToList();
        var averageLength = Math.Max(1, documents.Average(d => d.Length));
        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var term in document.Terms.Keys)
            {
                frequency[term] = frequency.GetValueOrDefault(term) + 1;
            }
        }

        List<(McpToolListing, double)> ranked = [];
        for (var i = 0; i < documents.Count; i++)
        {
            var score = 0.0;
            foreach (var (term, weight) in terms)
            {
                if (!documents[i].Terms.TryGetValue(term, out var tf))
                {
                    continue;
                }

                var df = frequency[term];
                var idf = Math.Log(1 + ((documents.Count - df + 0.5) / (df + 0.5)));
                score += weight * idf * (tf * (K1 + 1)) / (tf + (K1 * (1 - B + (B * documents[i].Length / averageLength))));
            }

            if (string.Equals(tools[i].Name, query.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(tools[i].RemoteName, query.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            if (score > 0)
            {
                ranked.Add((tools[i], score));
            }
        }

        return [.. ranked.OrderByDescending(r => r.Item2).ThenBy(r => r.Item1.Name, StringComparer.Ordinal).Select(r => (r.Item1, r.Item2))];
    }

    /// <summary>
    /// Split text into the lower-cased words a search compares.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The words, with common filler words left out.</returns>
    internal static List<string> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> words = [];
        var current = new StringBuilder();
        var currentIsPaired = false;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var word = current.ToString();
            current.Clear();
            if (currentIsPaired)
            {
                if (word.Length == 1)
                {
                    words.Add(word);
                }
                else
                {
                    for (var i = 0; i + 1 < word.Length; i++)
                    {
                        words.Add(word.Substring(i, 2));
                    }
                }

                return;
            }

#pragma warning disable CA1308 // Words are compared in lower case, which is how the synonym table is written; the text is never shown.
            word = Stem(word.ToLowerInvariant());
#pragma warning restore CA1308
            if (!StopWords.Contains(word) && (word.Length > 1 || char.IsDigit(word[0])))
            {
                words.Add(word);
            }
        }

        var previous = '\0';
        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                previous = '\0';
                continue;
            }

            var paired = IsPairedScript(c);
            if (paired != currentIsPaired || (!paired && char.IsUpper(c) && char.IsLower(previous)))
            {
                Flush();
            }

            currentIsPaired = paired;
            current.Append(c);
            previous = c;
        }

        Flush();
        return words;
    }

    private static List<(string Term, double Weight)> QueryTerms(string query)
    {
        Dictionary<string, double> terms = new(StringComparer.Ordinal);
        foreach (var word in Tokenize(query))
        {
            terms[word] = 1;
        }

        foreach (var word in terms.Keys.ToList())
        {
            if (!Synonyms.TryGetValue(word, out var related))
            {
                continue;
            }

            foreach (var synonym in related)
            {
                terms.TryAdd(Stem(synonym), SynonymWeight);
            }
        }

        return [.. terms.Select(t => (t.Key, t.Value))];
    }

    private static Document Index(McpToolListing tool)
    {
        Dictionary<string, double> terms = new(StringComparer.Ordinal);
        var length = 0;

        void Add(IEnumerable<string> words, double weight)
        {
            foreach (var word in words)
            {
                terms[word] = terms.GetValueOrDefault(word) + weight;
                length++;
            }
        }

        Add(Tokenize(tool.RemoteName), NameWeight);
        Add(Tokenize(tool.Server), ServerWeight);
        Add(Tokenize(tool.Description), DescriptionWeight);
        Add(ParameterWords(tool.InputSchema), ParameterWeight);
        return new Document(terms, length);
    }

    private static List<string> ParameterWords(string schema)
    {
        List<string> words = [];
        try
        {
            using var document = JsonDocument.Parse(schema);
            Collect(document.RootElement, words, 0);
        }
        catch (JsonException)
        {
            // A schema that can't be read adds no words; the tool is still found by its name and description.
        }

        return words;
    }

    private static void Collect(JsonElement schema, List<string> words, int depth)
    {
        if (depth > MaxSchemaDepth || words.Count > MaxParameterWords || schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
#pragma warning disable IDISP004 // The enumerator of a JSON object holds nothing that needs disposing.
            foreach (var property in properties.EnumerateObject())
#pragma warning restore IDISP004
            {
                words.AddRange(Tokenize(property.Name));
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    if (property.Value.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
                    {
                        words.AddRange(Tokenize(description.GetString() ?? string.Empty));
                    }

                    Collect(property.Value, words, depth + 1);
                }
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            Collect(items, words, depth + 1);
        }
    }

    private static string Stem(string word)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        return word.Length > 3 && word[^1] == 's' && word[^2] != 's' ? word[..^1] : word;
    }

    private static bool IsPairedScript(char c)
    {
        return c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uAC00' and <= '\uD7AF') or (>= '\u0E00' and <= '\u0E7F');
    }

    private sealed record Document(Dictionary<string, double> Terms, int Length);
}
