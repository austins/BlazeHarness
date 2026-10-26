using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// The arguments of one tool call, read with checks that produce messages the model can act on.
/// </summary>
public sealed class ToolArguments
{
    private readonly JsonElement _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArguments"/> class.
    /// </summary>
    /// <param name="root">The arguments value; anything but an object reads as having no arguments.</param>
    public ToolArguments(JsonElement root)
    {
        _root = root;
    }

    /// <summary>
    /// Parse arguments from JSON text.
    /// </summary>
    /// <param name="json">The JSON text; blank text means no arguments.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ToolArgumentException">The text is not valid JSON.</exception>
    public static ToolArguments Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ToolArguments(default);
        }

        try
        {
            return new ToolArguments(JsonDocument.Parse(json).RootElement.Clone());
        }
        catch (JsonException ex)
        {
            throw new ToolArgumentException($"The arguments are not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a required text argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The text, which may be empty.</returns>
    /// <exception cref="ToolArgumentException">The argument is missing or is not text.</exception>
    public string RequiredString(string name)
    {
        return OptionalString(name) ?? throw new ToolArgumentException($"The '{name}' argument is required.");
    }

    /// <summary>
    /// Gets an optional text argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>The text, or null when absent or null.</returns>
    /// <exception cref="ToolArgumentException">The argument is present but is not text.</exception>
    public string? OptionalString(string name)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new ToolArgumentException($"The '{name}' argument must be a string.");
    }

    /// <summary>
    /// Gets an optional whole-number argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="minimum">The smallest accepted value.</param>
    /// <param name="maximum">The largest accepted value.</param>
    /// <returns>The number, or null when absent or null.</returns>
    /// <exception cref="ToolArgumentException">The argument is not a whole number in range.</exception>
    public int? OptionalInt(string name, int minimum = int.MinValue, int maximum = int.MaxValue)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new ToolArgumentException($"The '{name}' argument must be a whole number.");
        }

        return number < minimum || number > maximum
            ? throw new ToolArgumentException(string.Create(CultureInfo.InvariantCulture, $"The '{name}' argument must be between {minimum} and {maximum}."))
            : number;
    }

    /// <summary>
    /// Gets an optional true or false argument.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="defaultValue">The value when the argument is absent.</param>
    /// <returns>The flag.</returns>
    /// <exception cref="ToolArgumentException">The argument is present but is not true or false.</exception>
    public bool OptionalBool(string name, bool defaultValue = false)
    {
        if (!TryGet(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ToolArgumentException($"The '{name}' argument must be true or false.");
    }

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return _root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty(name, out value)
            && value.ValueKind != JsonValueKind.Null;
    }
}

/// <summary>
/// A tool call's arguments were missing or the wrong shape, with a message that tells the model what to fix.
/// </summary>
public sealed class ToolArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the arguments.</param>
    public ToolArgumentException(string message)
        : base(message)
    {
    }
}
