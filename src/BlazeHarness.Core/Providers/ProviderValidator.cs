using BlazeHarness.Core.Secrets;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Checks provider settings before they are saved.
/// </summary>
public static class ProviderValidator
{
    private static readonly (string Name, double Min, double Max, Func<SamplingParams, double?> Get)[] Ranges =
    [
        ("temperature", 0, 2, s => s.Temperature),
        ("topP", 0, 1, s => s.TopP),
        ("topK", 0, int.MaxValue, s => s.TopK),
        ("minP", 0, 1, s => s.MinP),
        ("presencePenalty", -2, 2, s => s.PresencePenalty),
        ("frequencyPenalty", -2, 2, s => s.FrequencyPenalty),
        ("repetitionPenalty", 0.01, 2, s => s.RepetitionPenalty),
    ];

    /// <summary>
    /// Validate a provider and its models.
    /// </summary>
    /// <param name="provider">The provider to check.</param>
    /// <returns>Every problem found, or an empty list when the provider is valid.</returns>
    public static IReadOnlyList<ValidationIssue> Validate(ProviderConfig provider)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(provider.Id))
        {
            issues.Add(new ValidationIssue("id", "An identifier is required."));
        }

        if (string.IsNullOrWhiteSpace(provider.DisplayName))
        {
            issues.Add(new ValidationIssue("displayName", "A name is required."));
        }

        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            issues.Add(new ValidationIssue("baseUrl", "The base URL must be an absolute http or https address, e.g. https://api.openai.com/v1."));
        }

        if (!string.IsNullOrWhiteSpace(provider.ApiKeyRef) && !ApiKeyResolver.IsEnvironmentReference(provider.ApiKeyRef))
        {
            issues.Add(new ValidationIssue("apiKeyRef", "Use the form ${ENV_VAR}, or leave it empty to use the stored key."));
        }

        if (provider.ThinkingSend is { Mode: ThinkingSendMode.BodyField, Field: null or "" })
        {
            issues.Add(new ValidationIssue("thinkingSend.field", "Name the body field that carries the thinking level."));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < provider.Models.Count; i++)
        {
            ValidateModel(provider.Models[i], $"models[{i}]", seen, issues);
        }

        return issues;
    }

    private static void ValidateModel(ModelConfig model, string path, HashSet<string> seen, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            issues.Add(new ValidationIssue($"{path}.id", "A model id is required."));
        }
        else if (!seen.Add(model.Id))
        {
            issues.Add(new ValidationIssue($"{path}.id", $"The model \"{model.Id}\" is listed twice."));
        }

        if (model.ContextWindow <= 0)
        {
            issues.Add(new ValidationIssue($"{path}.contextWindow", "The context window must be a positive number of tokens."));
        }

        if (model.MaxOutputTokens <= 0)
        {
            issues.Add(new ValidationIssue($"{path}.maxOutputTokens", "Max output tokens must be a positive number."));
        }
        else if (model.ContextWindow > 0 && model.MaxOutputTokens >= model.ContextWindow)
        {
            issues.Add(new ValidationIssue($"{path}.maxOutputTokens", "Max output tokens must be less than the context window."));
        }

        ValidateSampling(model.SamplingParams, $"{path}.samplingParams", issues);
        if (model.Levels is null)
        {
            return;
        }

        foreach (var (level, config) in model.Levels)
        {
            ValidateSampling(config.Overrides, $"{path}.levels.{JsonNamingPolicy.CamelCase.ConvertName(level.ToString())}", issues);
        }
    }

    private static void ValidateSampling(SamplingParams? sampling, string path, List<ValidationIssue> issues)
    {
        if (sampling is null)
        {
            return;
        }

        foreach (var (name, min, max, get) in Ranges)
        {
            if (get(sampling) is { } value && (double.IsNaN(value) || value < min || value > max))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.{name}",
                    string.Create(CultureInfo.InvariantCulture, $"{name} must be between {min} and {max}.")));
            }
        }
    }
}
