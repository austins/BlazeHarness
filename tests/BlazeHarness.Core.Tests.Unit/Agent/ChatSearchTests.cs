using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSearchTests
{
    [Fact]
    public void Find_TextMatchesIgnoringCase_ReturnsThoseMessagesInOrder()
    {
        // Arrange
        var first = new ChatEntry(ChatRole.User, "Where is the Config file?");
        var other = new ChatEntry(ChatRole.Assistant, "Nothing relevant.");
        var last = new ChatEntry(ChatRole.Assistant, "It is called CONFIG.json");

        // Act
        var found = ChatSearch.Find([first, other, last], "config");

        // Assert
        found.Should().Equal(first.Id, last.Id);
    }

    [Fact]
    public void Find_ToolCallNameOrArguments_MatchesTheMessage()
    {
        // Arrange
        var byName = new ChatEntry(ChatRole.Assistant);
        byName.AddToolCall(new ToolCallEntry("1", "read_file", "{}", string.Empty));
        var byArguments = new ChatEntry(ChatRole.Assistant);
        byArguments.AddToolCall(new ToolCallEntry("2", "list", "{\"path\":\"src/Program.cs\"}", string.Empty));
        var neither = new ChatEntry(ChatRole.Assistant, "plain");

        // Act
        var named = ChatSearch.Find([byName, byArguments, neither], "read_file");
        var argued = ChatSearch.Find([byName, byArguments, neither], "program.cs");

        // Assert
        named.Should().Equal(byName.Id);
        argued.Should().Equal(byArguments.Id);
    }

    [Fact]
    public void Find_ThinkingText_IsNotSearched()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant, "Answer");
        entry.AppendReasoning("secret plan");

        // Act
        var found = ChatSearch.Find([entry], "secret");

        // Assert
        found.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_BlankQuery_MatchesNothing(string query)
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.User, "hello");

        // Act
        var found = ChatSearch.Find([entry], query);

        // Assert
        found.Should().BeEmpty();
    }

    [Fact]
    public void Find_QueryWithSurroundingSpaces_IsTrimmed()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.User, "hello world");

        // Act
        var found = ChatSearch.Find([entry], "  world ");

        // Assert
        found.Should().Equal(entry.Id);
    }
}
