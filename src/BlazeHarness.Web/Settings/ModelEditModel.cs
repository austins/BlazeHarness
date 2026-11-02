using BlazeHarness.Core.Providers;
using System.Text.Json;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// A mutable copy of a <see cref="ModelConfig"/> for the editing form.
/// </summary>
public sealed class ModelEditModel
{
    /// <summary>
    /// Gets or sets the model id sent to the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name; empty shows the id.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context window in tokens.
    /// </summary>
    public int ContextWindow { get; set; }

    /// <summary>
    /// Gets or sets the cap on a single response in tokens.
    /// </summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the model produces reasoning output.
    /// </summary>
    public bool Reasoning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the model accepts images.
    /// </summary>
    public bool Image { get; set; }

    /// <summary>
    /// Gets or sets the model's default sampling values.
    /// </summary>
    public SamplingEditModel Sampling { get; set; } = new();

    /// <summary>
    /// Gets or sets the sampling values applied on top of <see cref="Sampling"/> whenever a thinking level other than off is selected.
    /// </summary>
    public SamplingEditModel ThinkingSampling { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether any level other than off is supported.
    /// </summary>
    public bool HasThinkingLevels => Reasoning && Levels.Any(l => l.Key != ThinkingLevel.Off && l.Value.Supported);

    /// <summary>
    /// Gets the thinking levels, keyed by level; every level is present, and only supported ones are stored.
    /// </summary>
    public Dictionary<ThinkingLevel, LevelEditModel> Levels { get; } = NewLevels();

    /// <summary>
    /// Gets or sets a value indicating whether this model sends thinking levels differently from its provider.
    /// </summary>
    public bool OverrideSend { get; set; }

    /// <summary>
    /// Gets or sets how a level is sent when <see cref="OverrideSend"/> is set.
    /// </summary>
    public ThinkingSendMode SendMode { get; set; } = ThinkingSendMode.ReasoningEffort;

    /// <summary>
    /// Gets or sets the body path used when <see cref="SendMode"/> is <see cref="ThinkingSendMode.BodyField"/>.
    /// </summary>
    public string SendField { get; set; } = string.Empty;

    /// <summary>
    /// Gets the levels the model supports, from lowest to highest.
    /// </summary>
    public IEnumerable<ThinkingLevel> SupportedLevels => Levels.Where(l => l.Value.Supported).Select(l => l.Key).Order();

    /// <summary>
    /// Create an editable copy of a model.
    /// </summary>
    /// <param name="model">The model to copy.</param>
    /// <returns>The editing model.</returns>
    public static ModelEditModel FromConfig(ModelConfig model)
    {
        var edit = new ModelEditModel
        {
            Id = model.Id,
            DisplayName = model.DisplayName ?? string.Empty,
            ContextWindow = model.ContextWindow,
            MaxOutputTokens = model.MaxOutputTokens,
            Reasoning = model.Reasoning,
            Image = model.Input.HasFlag(InputTypes.Image),
            Sampling = SamplingEditModel.FromConfig(model.SamplingParams),
            OverrideSend = model.ThinkingSend is not null,
            SendMode = model.ThinkingSend?.Mode ?? ThinkingSendMode.ReasoningEffort,
            SendField = model.ThinkingSend?.Field ?? string.Empty,
        };

        foreach (var level in Enum.GetValues<ThinkingLevel>())
        {
            edit.Levels[level] = LevelEditModel.FromConfig(model.Levels is not null && model.Levels.TryGetValue(level, out var config) ? config : null);
        }

        var thinking = model.Levels?.Where(l => l.Key != ThinkingLevel.Off).Select(l => l.Value.Overrides).FirstOrDefault(o => o is not null);
        edit.ThinkingSampling = SamplingEditModel.FromConfig(thinking);

        return edit;
    }

    /// <summary>
    /// Create a model with the limits of a well-known model prefilled.
    /// </summary>
    /// <param name="id">The model id.</param>
    /// <returns>The new editing model; limits are left at zero when the model is not known.</returns>
    public static ModelEditModel FromId(string id)
    {
        var model = new ModelEditModel { Id = id };
        if (ModelPreset.Find(id) is { } preset)
        {
            model.ContextWindow = preset.ContextWindow;
            model.MaxOutputTokens = preset.MaxOutputTokens;
            model.Reasoning = preset.Reasoning;
            model.Image = preset.Image;
        }

        return model;
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <returns>The model config.</returns>
    public ModelConfig ToConfig()
    {
        var levels = new Dictionary<ThinkingLevel, LevelConfig>();
        var thinking = ThinkingSampling.ToConfig();
        foreach (var (level, edit) in Levels)
        {
            if (edit.ToConfig(level == ThinkingLevel.Off ? null : thinking) is { } config)
            {
                levels[level] = config;
            }
        }

        return new ModelConfig
        {
            Id = Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
            ContextWindow = ContextWindow,
            MaxOutputTokens = MaxOutputTokens,
            Reasoning = Reasoning,
            Input = Image ? InputTypes.Text | InputTypes.Image : InputTypes.Text,
            SamplingParams = Sampling.ToConfig(),
            Levels = levels.Count == 0 ? null : levels,
            ThinkingSend = OverrideSend
                ? new ThinkingSend { Mode = SendMode, Field = SendMode == ThinkingSendMode.BodyField ? SendField.Trim() : null }
                : null,
        };
    }

    /// <summary>
    /// Gets the value most servers expect for a level.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>"none" for <see cref="ThinkingLevel.Off"/>, otherwise the level's lowercase name.</returns>
    public static string SuggestedValue(ThinkingLevel level)
    {
        return level == ThinkingLevel.Off ? "none" : JsonNamingPolicy.CamelCase.ConvertName(level.ToString());
    }

    /// <summary>
    /// Turn a level on or off, filling in <see cref="SuggestedValue"/> when it has no value yet.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="supported">Whether the model supports the level.</param>
    public void SetLevelSupported(ThinkingLevel level, bool supported)
    {
        var edit = Levels[level];
        edit.Supported = supported;
        if (supported && string.IsNullOrWhiteSpace(edit.Value))
        {
            edit.Value = SuggestedValue(level);
        }
    }

    private static Dictionary<ThinkingLevel, LevelEditModel> NewLevels()
    {
        return Enum.GetValues<ThinkingLevel>().ToDictionary(l => l, _ => new LevelEditModel());
    }
}
