using BlazeHarness.Core.Providers;

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
        foreach (var (level, edit) in Levels)
        {
            if (edit.ToConfig() is { } config)
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
    /// Replace the sampling and thinking setup with a preset.
    /// </summary>
    /// <param name="preset">The preset to apply.</param>
    public void Apply(ThinkingPreset preset)
    {
        Sampling = SamplingEditModel.FromConfig(preset.Sampling);
        SetLevels(preset.Levels);
        OverrideSend = preset.ThinkingSend is not null;
        SendMode = preset.ThinkingSend?.Mode ?? ThinkingSendMode.ReasoningEffort;
        SendField = preset.ThinkingSend?.Field ?? string.Empty;
        Reasoning = true;
    }

    /// <summary>
    /// Replace the sampling and thinking setup with an imported snippet, keeping what the snippet leaves out.
    /// </summary>
    /// <param name="import">A snippet that was read without errors.</param>
    public void Apply(SamplingImportResult import)
    {
        if (import.Sampling is not null)
        {
            Sampling = SamplingEditModel.FromConfig(import.Sampling);
        }

        if (import.Levels is not null)
        {
            SetLevels(import.Levels);
            Reasoning = true;
        }
    }

    private static Dictionary<ThinkingLevel, LevelEditModel> NewLevels()
    {
        return Enum.GetValues<ThinkingLevel>().ToDictionary(l => l, _ => new LevelEditModel());
    }

    private void SetLevels(IReadOnlyDictionary<ThinkingLevel, LevelConfig> levels)
    {
        foreach (var level in Enum.GetValues<ThinkingLevel>())
        {
            Levels[level] = LevelEditModel.FromConfig(levels.TryGetValue(level, out var config) ? config : null);
        }
    }
}
