using BlazeHarness.Core.Providers;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// One custom request field in the sampling editor.
/// </summary>
public sealed class ExtraParamRow
{
    /// <summary>
    /// Gets or sets the field name; a dotted name such as <c>chat_template_kwargs.x</c> sets a nested field.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value as JSON text; text that is not valid JSON is sent as a string.
    /// </summary>
    public string Json { get; set; } = string.Empty;
}

/// <summary>
/// A mutable set of sampling values for the editing form, where every value can be unset.
/// </summary>
public sealed class SamplingEditModel
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the custom request fields.
    /// </summary>
    public Collection<ExtraParamRow> Extra { get; } = [];

    /// <summary>
    /// Gets the number of standard values that are set.
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// Gets a value indicating whether nothing is set.
    /// </summary>
    public bool IsEmpty => _values.Count == 0 && Extra.All(e => string.IsNullOrWhiteSpace(e.Name));

    /// <summary>
    /// Create an editable copy of sampling values.
    /// </summary>
    /// <param name="sampling">The values to copy, or null for none.</param>
    /// <returns>The editing model.</returns>
    public static SamplingEditModel FromConfig(SamplingParams? sampling)
    {
        var model = new SamplingEditModel();
        if (sampling is null)
        {
            return model;
        }

        foreach (var range in SamplingRange.All)
        {
            model.Set(range.Key, range.Get(sampling));
        }

        foreach (var (name, value) in sampling.Extra ?? new Dictionary<string, JsonElement>())
        {
            model.Extra.Add(new ExtraParamRow { Name = name, Json = value.GetRawText() });
        }

        return model;
    }

    /// <summary>
    /// Get one value.
    /// </summary>
    /// <param name="key">The parameter key, as in <see cref="SamplingRange.Key"/>.</param>
    /// <returns>The value, or null when it is unset.</returns>
    public double? Get(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Set or unset one value.
    /// </summary>
    /// <param name="key">The parameter key, as in <see cref="SamplingRange.Key"/>.</param>
    /// <param name="value">The value, or null to unset it.</param>
    public void Set(string key, double? value)
    {
        if (value is { } v)
        {
            _values[key] = v;
        }
        else
        {
            _values.Remove(key);
        }
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <returns>The sampling values, or null when nothing is set.</returns>
    public SamplingParams? ToConfig()
    {
        if (IsEmpty)
        {
            return null;
        }

        Dictionary<string, JsonElement>? extra = null;
        foreach (var row in Extra.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
        {
            extra ??= [];
            extra[row.Name.Trim()] = ParseValue(row.Json);
        }

        return new SamplingParams
        {
            Temperature = Get("temperature"),
            TopP = Get("topP"),
            TopK = Get("topK") is { } topK ? (int)Math.Round(topK) : null,
            MinP = Get("minP"),
            PresencePenalty = Get("presencePenalty"),
            FrequencyPenalty = Get("frequencyPenalty"),
            RepetitionPenalty = Get("repetitionPenalty"),
            Extra = extra,
        };
    }

    private static JsonElement ParseValue(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }
}

/// <summary>
/// A mutable copy of one thinking level for the editing form.
/// </summary>
public sealed class LevelEditModel
{
    /// <summary>
    /// Gets or sets a value indicating whether the model supports this level.
    /// </summary>
    public bool Supported { get; set; }

    /// <summary>
    /// Gets or sets the value sent to the provider for this level; empty sends nothing.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sampling values that apply only while this level is selected.
    /// </summary>
    public SamplingEditModel Overrides { get; set; } = new();

    /// <summary>
    /// Create an editable copy of a level.
    /// </summary>
    /// <param name="level">The level to copy, or null for an unsupported level.</param>
    /// <returns>The editing model.</returns>
    public static LevelEditModel FromConfig(LevelConfig? level)
    {
        return level is null
            ? new LevelEditModel()
            : new LevelEditModel
            {
                Supported = true,
                Value = level.Value ?? string.Empty,
                Overrides = SamplingEditModel.FromConfig(level.Overrides),
            };
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <returns>The level, or null when the model does not support it.</returns>
    public LevelConfig? ToConfig()
    {
        return Supported
            ? new LevelConfig
            {
                Value = string.IsNullOrWhiteSpace(Value) ? null : Value.Trim(),
                Overrides = Overrides.ToConfig(),
            }
            : null;
    }
}
