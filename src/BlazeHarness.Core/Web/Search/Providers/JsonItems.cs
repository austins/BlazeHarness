using System.Text.Json;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Reads the items of a JSON array without the caller holding an enumerator.
/// </summary>
internal static class JsonItems
{
    /// <summary>
    /// Get the items of an array.
    /// </summary>
    /// <param name="element">A JSON value.</param>
    /// <returns>The items, or none when the value is not an array.</returns>
    public static List<JsonElement> Of(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array ? [.. element.EnumerateArray()] : [];
    }

    /// <summary>
    /// Get a text property.
    /// </summary>
    /// <param name="element">A JSON value.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The text, or an empty string when the value is not an object or the property is not text.</returns>
    public static string Text(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
