using BlazeHarness.Core.Chats;

namespace BlazeHarness.Core.Tests.Unit.Chats;

public sealed class ChatReferenceTests
{
    [Fact]
    public void IdOf_FormattedReference_ReturnsTheId()
    {
        // Act
        var id = ChatReference.IdOf(ChatReference.Format("0123abcd"));

        // Assert
        id.Should().Be("0123abcd");
    }

    [Theory]
    [InlineData("  chat:abc  ", "abc")]
    [InlineData("abc", "abc")]
    public void IdOf_PaddedOrBareText_ReturnsTheId(string text, string expected)
    {
        // Act
        var id = ChatReference.IdOf(text);

        // Assert
        id.Should().Be(expected);
    }
}
