using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Chats.Events;

/// <summary>
/// Reads and writes a <see cref="UserMessageOrigin"/> by its camel-case name, reading a name it does not know as <see cref="UserMessageOrigin.Unknown"/>
/// so one line from a newer version does not make the whole message disappear from the chat.
/// </summary>
internal sealed class UserMessageOriginConverter : JsonConverter<UserMessageOrigin>
{
    /// <inheritdoc />
    public override UserMessageOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("An origin is a name.");
        }

        return Enum.TryParse<UserMessageOrigin>(reader.GetString(), ignoreCase: true, out var origin) && Enum.IsDefined(origin)
            ? origin
            : UserMessageOrigin.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UserMessageOrigin value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
