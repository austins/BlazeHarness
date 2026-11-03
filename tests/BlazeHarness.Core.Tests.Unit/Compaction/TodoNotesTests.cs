using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class TodoNotesTests
{
    [Fact]
    public void Append_ConversationWithAList_AddsItAfterTheSummary()
    {
        // Arrange
        var messages = Messages("""{"items":[{"text":"read","status":"completed"},{"text":"edit","status":"in_progress"},{"text":"test","status":"pending"}]}""");

        // Act
        var result = TodoNotes.Append("the summary", messages);

        // Assert
        result.Should().StartWith("the summary\n\n[Todo list").And.Contain("- done: read").And.Contain("- in progress: edit").And.Contain("- not started: test");
    }

    [Fact]
    public void Append_ConversationWithoutAList_ReturnsTheSummaryAsItWas()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "go")];

        // Act
        var result = TodoNotes.Append("the summary", messages);

        // Assert
        result.Should().Be("the summary");
    }

    [Fact]
    public void Strip_AfterAppend_GivesTheSummaryBack()
    {
        // Arrange
        var appended = TodoNotes.Append("the summary", Messages("""{"items":[{"text":"a","status":"pending"}]}"""));

        // Act
        var result = TodoNotes.Strip(appended);

        // Assert
        result.Should().Be("the summary");
    }

    [Fact]
    public void Strip_NullOrPlainSummary_IsUnchanged()
    {
        // Arrange & Act & Assert
        TodoNotes.Strip(null).Should().BeNull();
        TodoNotes.Strip("plain").Should().Be("plain");
    }

    [Fact]
    public async Task Append_WithTheExtractiveSummarizer_KeepsBothAndSurvivesASecondRound()
    {
        // Arrange
        var summarizer = new ExtractiveSummarizer();
        var messages = Messages("""{"items":[{"text":"a","status":"pending"}]}""");
        messages.Insert(0, new ChatMessage(ChatRole.User, "fix the bug"));
        var first = TodoNotes.Append(await summarizer.SummarizeAsync(null, messages, TestContext.Current.CancellationToken), messages);

        // Act
        var second = TodoNotes.Append(await summarizer.SummarizeAsync(TodoNotes.Strip(first), messages, TestContext.Current.CancellationToken), messages);

        // Assert
        first.Should().Contain("fix the bug").And.Contain("- not started: a");
        second.Split("[Todo list").Should().HaveCount(2);
    }

    private static List<ChatMessage> Messages(string listJson)
    {
        var arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(listJson);
        return
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", TodoList.ToolName, arguments)]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "Todo list updated: 0 of 1 done.")]),
        ];
    }
}
