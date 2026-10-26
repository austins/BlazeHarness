using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Config;

/// <summary>
/// The JSON serializer settings shared by every file the app reads and writes.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Gets the shared options: web defaults, camelCase enums, indented output, null values omitted,
    /// and comments and trailing commas tolerated on read so users can hand-edit files.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    /// <summary>
    /// Gets the same options with single-line output, for line-based files such as a session's message log.
    /// </summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
