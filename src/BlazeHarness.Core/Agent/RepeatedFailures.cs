using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Remembers the tool calls of a run that failed, so an unchanged call that keeps failing is refused instead of run again.
/// </summary>
/// <remarks>
/// A call is the tool that runs and its arguments with object keys sorted, so the same arguments in another order count as the same call.
/// A successful call that may change things (one that is not read-only) clears the memory, since an earlier failure may not happen again;
/// so does a message from the user.
/// </remarks>
internal sealed class RepeatedFailures
{
    /// <summary>
    /// The number of times an unchanged call may fail before it is refused.
    /// </summary>
    internal const int MaxFailures = 2;

    /// <summary>
    /// The result a refused call gets instead of running.
    /// </summary>
    internal static readonly string RefusedError = string.Create(
        CultureInfo.InvariantCulture,
        $"This call was not run: the same call with the same arguments already failed {MaxFailures} times. Change the arguments or try a different approach.");

    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);

    /// <summary>
    /// Get whether a call has failed often enough to be refused.
    /// </summary>
    /// <param name="tool">The name of the tool that runs.</param>
    /// <param name="argumentsJson">The arguments of the tool that runs.</param>
    /// <returns>True when the call failed <see cref="MaxFailures"/> times since the memory was last cleared.</returns>
    public bool Refuses(string tool, string argumentsJson)
    {
        return _failures.GetValueOrDefault(Key(tool, argumentsJson)) >= MaxFailures;
    }

    /// <summary>
    /// Record how a call that ran ended.
    /// </summary>
    /// <param name="tool">The name of the tool that ran.</param>
    /// <param name="argumentsJson">The arguments of the tool that ran.</param>
    /// <param name="failed">True when the call failed.</param>
    /// <param name="readOnly">True when the tool only reads, so its success changes nothing another call depends on.</param>
    public void Record(string tool, string argumentsJson, bool failed, bool readOnly)
    {
        var key = Key(tool, argumentsJson);
        if (failed)
        {
            _failures[key] = _failures.GetValueOrDefault(key) + 1;
        }
        else if (readOnly)
        {
            _failures.Remove(key);
        }
        else
        {
            _failures.Clear();
        }
    }

    /// <summary>
    /// Forget every failure, after something changed that may make the calls work.
    /// </summary>
    public void Clear() => _failures.Clear();

    private static string Key(string tool, string argumentsJson)
    {
        string arguments;
        try
        {
            arguments = Sorted(JsonNode.Parse(argumentsJson))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            arguments = argumentsJson;
        }

        return tool + "\n" + arguments;
    }

    private static JsonNode? Sorted(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
            JsonArray array => new JsonArray([.. array.Select(Sorted)]),
            null => null,
            _ => node.DeepClone(),
        };
    }
}
