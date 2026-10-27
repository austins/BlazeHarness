using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Reads sampling and thinking settings from a pasted JSON snippet.
/// </summary>
/// <remarks>
/// The accepted shape is <c>samplingParams</c> (defaults), <c>samplingParamsByThinkingLevel</c> (per-level overrides)
/// and <c>thinkingLevels</c> (level to provider value, where null means unsupported).
/// A bare object of sampling values, such as <c>{"temperature": 0.6, "top_p": 0.95}</c>, is read as the defaults.
/// Names may be snake case or camel case; unknown names become extra body fields.
/// </remarks>
public static class SamplingImport
{
    private static readonly HashSet<string> Wrappers = new(StringComparer.OrdinalIgnoreCase)
    {
        "samplingParams",
        "samplingParamsByThinkingLevel",
        "thinkingLevels",
    };

    /// <summary>
    /// Read a snippet.
    /// </summary>
    /// <param name="json">The pasted JSON.</param>
    /// <returns>The settings it describes, or the problems that stopped it.</returns>
    public static SamplingImportResult Parse(string json)
    {
        var errors = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            return Fail($"This is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("Expected a JSON object.");
            }

            var wrapped = Properties(root).Any(p => Wrappers.Contains(p.Name));
            SamplingParams? defaults = null;
            Dictionary<ThinkingLevel, LevelConfig>? levels = null;

            if (wrapped)
            {
                foreach (var property in Properties(root))
                {
                    switch (property.Name.ToUpperInvariant())
                    {
                        case "SAMPLINGPARAMS":
                            defaults = ReadSampling(property.Value, "samplingParams", errors);
                            break;
                        case "SAMPLINGPARAMSBYTHINKINGLEVEL":
                            levels = ReadOverrides(property.Value, levels, errors);
                            break;
                        case "THINKINGLEVELS":
                            levels = ReadLevelValues(property.Value, levels, errors);
                            break;
                        default:
                            errors.Add($"Unknown top-level field \"{property.Name}\".");
                            break;
                    }
                }
            }
            else
            {
                defaults = ReadSampling(root, "$", errors);
            }

            return errors.Count > 0 ? new SamplingImportResult(null, null, errors) : new SamplingImportResult(defaults, levels, errors);
        }
    }

    private static List<JsonProperty> Properties(JsonElement element)
    {
        var list = new List<JsonProperty>();
        using var enumerator = element.EnumerateObject();
        foreach (var property in enumerator)
        {
            list.Add(property);
        }

        return list;
    }

    private static SamplingImportResult Fail(string message) => new(null, null, [message]);

    private static Dictionary<ThinkingLevel, LevelConfig>? ReadOverrides(
        JsonElement element,
        Dictionary<ThinkingLevel, LevelConfig>? levels,
        List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("samplingParamsByThinkingLevel must be an object.");
            return levels;
        }

        levels ??= [];
        foreach (var property in Properties(element))
        {
            if (!TryLevel(property.Name, out var level))
            {
                errors.Add($"Unknown thinking level \"{property.Name}\".");
                continue;
            }

            var overrides = ReadSampling(property.Value, $"samplingParamsByThinkingLevel.{property.Name}", errors);
            levels[level] = (levels.TryGetValue(level, out var existing) ? existing : new LevelConfig()) with { Overrides = overrides };
        }

        return levels;
    }

    private static Dictionary<ThinkingLevel, LevelConfig>? ReadLevelValues(
        JsonElement element,
        Dictionary<ThinkingLevel, LevelConfig>? levels,
        List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("thinkingLevels must be an object.");
            return levels;
        }

        levels ??= [];
        foreach (var property in Properties(element))
        {
            if (!TryLevel(property.Name, out var level))
            {
                errors.Add($"Unknown thinking level \"{property.Name}\".");
                continue;
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Null:
                    levels.Remove(level);
                    break;
                case JsonValueKind.String:
                    levels[level] = (levels.TryGetValue(level, out var s) ? s : new LevelConfig()) with { Value = property.Value.GetString() };
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Number:
                    levels[level] = (levels.TryGetValue(level, out var o) ? o : new LevelConfig()) with { Value = property.Value.GetRawText() };
                    break;
                default:
                    errors.Add($"thinkingLevels.{property.Name} must be a string, number, boolean or null.");
                    break;
            }
        }

        return levels;
    }

    private static SamplingParams? ReadSampling(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} must be an object.");
            return null;
        }

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        Dictionary<string, JsonElement>? extra = null;
        foreach (var property in Properties(element))
        {
            var range = SamplingRange.All.FirstOrDefault(r => Normalize(r.WireName) == Normalize(property.Name));
            if (range is null)
            {
                extra ??= [];
                extra[property.Name] = property.Value.Clone();
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var number))
            {
                errors.Add($"{path}.{property.Name} must be a number.");
            }
            else if (range.IsInteger && !double.IsInteger(number))
            {
                errors.Add($"{path}.{property.Name} must be a whole number.");
            }
            else
            {
                values[range.Key] = number;
            }
        }

        return new SamplingParams
        {
            Temperature = Get(values, "temperature"),
            TopP = Get(values, "topP"),
            TopK = Get(values, "topK") is { } topK ? (int)topK : null,
            MinP = Get(values, "minP"),
            PresencePenalty = Get(values, "presencePenalty"),
            FrequencyPenalty = Get(values, "frequencyPenalty"),
            RepetitionPenalty = Get(values, "repetitionPenalty"),
            Extra = extra,
        };
    }

    private static double? Get(Dictionary<string, double> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    private static string Normalize(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private static bool TryLevel(string name, out ThinkingLevel level)
    {
        return Enum.TryParse(name, ignoreCase: true, out level) && Enum.IsDefined(level) && !int.TryParse(name, CultureInfo.InvariantCulture, out _);
    }
}
