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
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
