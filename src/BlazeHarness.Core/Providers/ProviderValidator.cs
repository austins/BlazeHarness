using BlazeHarness.Core.Secrets;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Checks provider settings before they are saved.
/// </summary>
internal static class ProviderValidator
{
    private const string InvalidPathMessage = "Use letters, digits, dashes and underscores, with dots to reach into an object; no spaces or brackets.";

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

        if (provider.ThinkingSend is { Mode: ThinkingSendMode.BodyField, Field: { } thinkingField } && thinkingField.Length > 0 && !IsBodyPath(thinkingField))
        {
            issues.Add(new ValidationIssue("thinkingSend.field", InvalidPathMessage));
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

    private static bool IsBodyPath(string path)
    {
        return path.Trim().Length > 0 && !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '[' or ']' or '"' or '\'' or '\\' or '$' or '*');
    }

    private static void ValidateSampling(SamplingParams? sampling, string path, List<ValidationIssue> issues)
    {
        if (sampling is null)
        {
            return;
        }

        if (sampling.Extra is not null)
        {
            foreach (var key in sampling.Extra.Keys.Where(k => !IsBodyPath(k)))
            {
                issues.Add(new ValidationIssue($"{path}.extra", $"The field name \"{key}\" is not valid. {InvalidPathMessage}"));
            }
        }

        foreach (var range in SamplingRange.All)
        {
            if (range.Get(sampling) is { } value && (double.IsNaN(value) || value < range.Min || value > range.Max))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.{range.Key}",
                    string.Create(CultureInfo.InvariantCulture, $"{range.Key} must be between {range.Min} and {range.Max}.")));
            }
        }
    }
}
