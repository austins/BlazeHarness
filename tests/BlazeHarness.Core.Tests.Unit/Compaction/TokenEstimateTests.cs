using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class TokenEstimateTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    public void Of_Text_IsAboutOneTokenPerFourCharactersRoundedUp(string text, int expected)
    {
        // Act
        var result = TokenEstimate.Of(text);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Of_ANullText_IsZero()
    {
        // Act
        var result = TokenEstimate.Of((string?)null);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void Of_AToolCall_CountsItsNameAndArguments()
    {
        // Arrange
        var call = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("1", "read_file", new Dictionary<string, object?> { ["path"] = new string('p', 400) })]);
        var plain = new ChatMessage(ChatRole.Assistant, "x");

        // Act
        var result = TokenEstimate.Of(call);

        // Assert
        result.Should().BeGreaterThan(100);
        result.Should().BeGreaterThan(TokenEstimate.Of(plain));
    }

    [Fact]
    public void Of_AToolResult_CountsItsOutput()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("1", new string('x', 4_000))]);

        // Act
        var result = TokenEstimate.Of(message);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(1_000);
    }

    [Fact]
    public void Of_AnImage_CountsAFlatAmount()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, [new DataContent(new byte[100_000], "image/png")]);

        // Act
        var result = TokenEstimate.Of(message);

        // Assert
        result.Should().BeInRange(1_000, 1_100);
    }

    [Fact]
    public void Of_Messages_IsTheSumOfTheMessages()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "abcd"), new(ChatRole.Assistant, "abcdefgh")];

        // Act
        var result = TokenEstimate.Of(messages);

        // Assert
        result.Should().Be(TokenEstimate.Of(messages[0]) + TokenEstimate.Of(messages[1]));
    }
}
