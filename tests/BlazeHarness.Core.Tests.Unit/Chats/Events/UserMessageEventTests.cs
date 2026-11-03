using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Config;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Chats.Events;

public sealed class UserMessageEventTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Serialize_WithSteerOrigin_WritesItAsAString()
    {
        // Arrange
        ChatEvent sut = new UserMessageEvent(DateTimeOffset.UnixEpoch, Id, "hi", null, UserMessageOrigin.Steer);

        // Act
        var json = JsonSerializer.Serialize(sut, JsonDefaults.Compact);

        // Assert
        json.Should().Contain("\"origin\":\"steer\"");
    }

    [Fact]
    public void Serialize_WithoutOrigin_LeavesItOut()
    {
        // Arrange
        ChatEvent sut = new UserMessageEvent(DateTimeOffset.UnixEpoch, Id, "hi");

        // Act
        var json = JsonSerializer.Serialize(sut, JsonDefaults.Compact);

        // Assert
        json.Should().NotContain("origin");
    }

    [Fact]
    public void Deserialize_WithSteerOrigin_ReadsItBack()
    {
        // Arrange
        ChatEvent original = new UserMessageEvent(DateTimeOffset.UnixEpoch, Id, "hi", null, UserMessageOrigin.Steer);
        var json = JsonSerializer.Serialize(original, JsonDefaults.Compact);

        // Act
        var sut = JsonSerializer.Deserialize<ChatEvent>(json, JsonDefaults.Compact);

        // Assert
        sut.Should().BeOfType<UserMessageEvent>().Which.Origin.Should().Be(UserMessageOrigin.Steer);
    }

    [Fact]
    public void Deserialize_LineFromBeforeOriginExisted_LoadsWithNoOrigin()
    {
        // Arrange
        const string json = """{"type":"user","at":"1970-01-01T00:00:00+00:00","entryId":"11111111-1111-1111-1111-111111111111","text":"hi"}""";

        // Act
        var sut = JsonSerializer.Deserialize<ChatEvent>(json, JsonDefaults.Compact);

        // Assert
        var user = sut.Should().BeOfType<UserMessageEvent>().Subject;
        user.Text.Should().Be("hi");
        user.Origin.Should().BeNull();
    }
}
