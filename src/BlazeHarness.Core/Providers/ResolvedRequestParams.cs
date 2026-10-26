using System.Globalization;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Providers;

/// <summary>The sampling values and extra body fields that will actually be sent for one request.</summary>
public sealed record ResolvedRequestParams(SamplingParams Sampling, JsonObject BodyFields)
{
    /// <summary>
    /// Effective params = model defaults, then the active thinking level's overrides, then the per-session override.
    /// Non-standard sampling fields and the thinking value end up in <see cref="BodyFields"/>.
    /// </summary>
    public static ResolvedRequestParams Resolve(
        ProviderConfig provider,
        ModelConfig model,
        ThinkingLevel? level,
        SamplingParams? sessionOverride)
    {
        var sampling = model.SamplingParams ?? new SamplingParams();
        LevelConfig? levelConfig = null;
        if (level is { } l && model.Levels is not null && model.Levels.TryGetValue(l, out levelConfig))
        {
            sampling = sampling.MergeOver(levelConfig.Overrides);
        }

        sampling = sampling.MergeOver(sessionOverride);

        var body = new JsonObject();
        if (sampling.TopK is { } topK)
        {
            body["top_k"] = topK;
        }

        if (sampling.MinP is { } minP)
        {
            body["min_p"] = minP;
        }

        if (sampling.RepetitionPenalty is { } repetition)
        {
            body["repetition_penalty"] = repetition;
        }

        if (sampling.Extra is not null)
        {
            foreach (var (key, value) in sampling.Extra)
            {
                SetPath(body, key, JsonNode.Parse(value.GetRawText()));
            }
        }

        if (levelConfig?.Value is { } wireValue)
        {
            var send = model.ThinkingSend ?? provider.ThinkingSend;
            switch (send.Mode)
            {
                case ThinkingSendMode.ReasoningEffort:
                    body["reasoning_effort"] = wireValue;
                    break;
                case ThinkingSendMode.ReasoningObject:
                    body["reasoning"] = new JsonObject { ["effort"] = wireValue };
                    break;
                case ThinkingSendMode.BodyField when !string.IsNullOrWhiteSpace(send.Field):
                    SetPath(body, send.Field, ParseLiteral(wireValue));
                    break;
                default:
                    break;
            }
        }

        return new ResolvedRequestParams(sampling, body);
    }

    private static JsonNode? ParseLiteral(string value)
    {
        if (bool.TryParse(value, out var b))
        {
            return b;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return value;
    }

    private static void SetPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next)
            {
                next = [];
                current[parts[i]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value;
    }
}
