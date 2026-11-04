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
            issues.Add(new ValidationIssue("id", ValidationCode.IdRequired, "An identifier is required."));
        }

        if (string.IsNullOrWhiteSpace(provider.DisplayName))
        {
            issues.Add(new ValidationIssue("displayName", ValidationCode.NameRequired, "A name is required."));
        }

        if (!provider.BaseUrl.IsAbsoluteUri || provider.BaseUrl.Scheme is not ("http" or "https"))
        {
            issues.Add(new ValidationIssue("baseUrl", ValidationCode.BaseUrlInvalid, "The base URL must be an absolute http or https address, e.g. https://api.example.com/v1."));
        }

        if (!string.IsNullOrWhiteSpace(provider.ApiKeyRef) && !ApiKeyResolver.IsEnvironmentReference(provider.ApiKeyRef))
        {
            issues.Add(new ValidationIssue(
                "apiKeyRef",
                ValidationCode.ApiKeyRefInvalid,
                "Use the form ${ENV_VAR}, or leave it empty to use the stored key.",
                new Dictionary<string, object?> { ["example"] = "${ENV_VAR}" }));
        }

        if (provider.ThinkingSend is { Mode: ThinkingSendMode.BodyField, Field: null or "" })
        {
            issues.Add(new ValidationIssue("thinkingSend.field", ValidationCode.ThinkingFieldRequired, "Name the body field that carries the thinking level."));
        }

        if (provider.ThinkingSend is { Mode: ThinkingSendMode.BodyField, Field: { } thinkingField } && thinkingField.Length > 0 && !IsBodyPath(thinkingField))
        {
            issues.Add(new ValidationIssue("thinkingSend.field", ValidationCode.ThinkingFieldInvalid, InvalidPathMessage));
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
            issues.Add(new ValidationIssue($"{path}.id", ValidationCode.ModelIdRequired, "A model id is required."));
        }
        else if (!seen.Add(model.Id))
        {
            issues.Add(new ValidationIssue(
                $"{path}.id",
                ValidationCode.ModelIdDuplicate,
                $"The model \"{model.Id}\" is listed twice.",
                new Dictionary<string, object?> { ["id"] = model.Id }));
        }

        if (model.ContextWindow <= 0)
        {
            issues.Add(new ValidationIssue($"{path}.contextWindow", ValidationCode.ContextWindowInvalid, "The context window must be a positive number of tokens."));
        }

        if (model.MaxOutputTokens <= 0)
        {
            issues.Add(new ValidationIssue($"{path}.maxOutputTokens", ValidationCode.MaxOutputInvalid, "Max output tokens must be a positive number."));
        }
        else if (model.ContextWindow > 0 && model.MaxOutputTokens >= model.ContextWindow)
        {
            issues.Add(new ValidationIssue($"{path}.maxOutputTokens", ValidationCode.MaxOutputTooLarge, "Max output tokens must be less than the context window."));
        }

        ValidateSampling(model.SamplingParams, $"{path}.samplingParams", issues);
        if (model.DefaultLevel is { } defaultLevel ? model.Levels?.ContainsKey(defaultLevel) != true : model.Levels is { Count: > 0 })
        {
            issues.Add(new ValidationIssue($"{path}.defaultLevel", ValidationCode.DefaultLevelInvalid, "Pick a default thinking level from the ones the model supports."));
        }

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
                issues.Add(new ValidationIssue(
                    $"{path}.extra",
                    ValidationCode.ExtraFieldInvalid,
                    $"The field name \"{key}\" is not valid. {InvalidPathMessage}",
                    new Dictionary<string, object?> { ["name"] = key }));
            }
        }

        foreach (var range in SamplingRange.All)
        {
            if (range.Get(sampling) is { } value && (double.IsNaN(value) || value < range.Min || value > range.Max))
            {
                issues.Add(new ValidationIssue(
                    $"{path}.{range.Key}",
                    ValidationCode.SamplingOutOfRange,
                    string.Create(CultureInfo.InvariantCulture, $"{range.Key} must be between {range.Min} and {range.Max}."),
                    new Dictionary<string, object?> { ["key"] = range.Key, ["min"] = range.Min, ["max"] = range.Max }));
            }
        }
    }
}
