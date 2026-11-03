using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Config;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Chats;

public sealed class UserMessageOriginConverterTests
{
    private const string Prefix = """{"type":"user","at":"1970-01-01T00:00:00+00:00","entryId":"11111111-1111-1111-1111-111111111111","text":"hi","origin":""";

    [Fact]
    public void Serialize_SteerOrigin_WritesItsCamelCaseName()
    {
        // Arrange
        ChatEvent sut = new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "hi", null, UserMessageOrigin.Steer);

        // Act
        var json = JsonSerializer.Serialize(sut, JsonDefaults.Compact);

        // Assert
        json.Should().Contain("\"origin\":\"steer\"");
    }

    [Theory]
    [InlineData("\"steer\"", UserMessageOrigin.Steer)]
    [InlineData("\"futureOrigin\"", UserMessageOrigin.Unknown)]
    [InlineData("\"99\"", UserMessageOrigin.Unknown)]
    public void Deserialize_OriginName_ReadsItOrFallsBackToUnknown(string origin, UserMessageOrigin expected)
    {
        // Act
        var sut = JsonSerializer.Deserialize<ChatEvent>(Prefix + origin + "}", JsonDefaults.Compact);

        // Assert
        sut.Should().BeOfType<UserMessageEvent>().Which.Origin.Should().Be(expected);
    }

    [Fact]
    public void Deserialize_OriginThatIsNotAName_Throws()
    {
        // Act
        var act = () => JsonSerializer.Deserialize<ChatEvent>(Prefix + "5}", JsonDefaults.Compact);

        // Assert
        act.Should().Throw<JsonException>();
    }
}
