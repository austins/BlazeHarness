using System.Globalization;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// The sampling values and extra body fields that will actually be sent for one request.
/// </summary>
/// <param name="Sampling">The effective sampling values.</param>
/// <param name="BodyFields">Fields to merge into the request body: non-standard sampling fields and the thinking value.</param>
public sealed record ResolvedRequestParams(SamplingParams Sampling, JsonObject BodyFields)
{
    /// <summary>
    /// Work out the effective parameters for a request.
    /// </summary>
    /// <remarks>
    /// Effective params are the model defaults, then the active thinking level's overrides.
    /// Non-standard sampling fields and the thinking value end up in <see cref="BodyFields"/>.
    /// </remarks>
    /// <param name="provider">The provider, which supplies the default thinking wire format.</param>
    /// <param name="model">The model, which supplies defaults, levels and an optional thinking wire format.</param>
    /// <param name="level">The selected thinking level, or null for none. A level the model does not list sends nothing.</param>
    /// <returns>The resolved parameters.</returns>
    public static ResolvedRequestParams Resolve(
        ProviderConfig provider,
        ModelConfig model,
        ThinkingLevel? level)
    {
        var sampling = model.SamplingParams ?? new SamplingParams();
        LevelConfig? levelConfig = null;
        if (level is { } l && model.Levels is not null && model.Levels.TryGetValue(l, out levelConfig))
        {
            sampling = sampling.MergeOver(levelConfig.Overrides);
        }

        var body = new JsonObject();
        if (sampling.TopK is { } topK)
        {
            body[Wire("topK")] = topK;
        }

        if (sampling.MinP is { } minP)
        {
            body[Wire("minP")] = minP;
        }

        if (sampling.RepetitionPenalty is { } repetition)
        {
            body[Wire("repetitionPenalty")] = repetition;
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

    /// <summary>
    /// Build the request fields these parameters add, for showing to the user.
    /// </summary>
    /// <returns>The standard sampling fields followed by the body fields, using the names that go on the wire.</returns>
    public JsonObject ToRequestJson()
    {
        var json = new JsonObject();
        if (Sampling.Temperature is { } temperature)
        {
            json[Wire("temperature")] = temperature;
        }

        if (Sampling.TopP is { } topP)
        {
            json[Wire("topP")] = topP;
        }

        if (Sampling.PresencePenalty is { } presence)
        {
            json[Wire("presencePenalty")] = presence;
        }

        if (Sampling.FrequencyPenalty is { } frequency)
        {
            json[Wire("frequencyPenalty")] = frequency;
        }

        foreach (var (key, value) in BodyFields)
        {
            json[key] = value?.DeepClone();
        }

        return json;
    }

    private static string Wire(string key) => SamplingRange.All.First(r => r.Key == key).WireName;

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
