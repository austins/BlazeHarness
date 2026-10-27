using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class ModelSummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_SendsTheTranscriptAndReturnsTheModelsText()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Goal: "), ScriptedChatClient.Text("fix the build.")]);
        var sut = new ModelSummarizer(client, new ChatOptions(), 0);
        List<ChatMessage> messages = [new(ChatRole.User, "please fix the build"), new(ChatRole.Assistant, "done")];

        // Act
        var summary = await sut.SummarizeAsync("older summary", messages, TestContext.Current.CancellationToken);

        // Assert
        summary.Should().Be("Goal: fix the build.");
        var request = client.Requests.Single();
        request[0].Role.Should().Be(ChatRole.System);
        request[1].Text.Should().Contain("older summary").And.Contain("User: please fix the build").And.Contain("Assistant: done");
        client.Options.Single()!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SummarizeAsync_AnEmptyAnswer_Throws()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("  ")]);
        var sut = new ModelSummarizer(client, new ChatOptions(), 0);

        // Act
        var act = async () => await sut.SummarizeAsync(null, [new ChatMessage(ChatRole.User, "hi")], TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SummarizeAsync_AFailingRequest_Throws()
    {
        // Arrange
        using var client = new ScriptedChatClient([], failure: new InvalidOperationException("server down"));
        var sut = new ModelSummarizer(client, new ChatOptions(), 0);

        // Act
        var act = async () => await sut.SummarizeAsync(null, [new ChatMessage(ChatRole.User, "hi")], TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*server down*");
    }

    [Fact]
    public void Transcript_ALongToolResult_IsCutShort()
    {
        // Arrange
        List<ChatMessage> messages =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("1", "read_file", new Dictionary<string, object?> { ["path"] = "a.txt" })]),
            new(ChatRole.Tool, [new FunctionResultContent("1", new string('x', 10_000))]),
        ];

        // Act
        var text = ModelSummarizer.Transcript(null, messages, 0);

        // Assert
        text.Should().Contain("read_file").And.Contain("a.txt");
        text.Should().Contain("8500 more characters left out");
        text.Length.Should().BeLessThan(3_000);
    }

    [Fact]
    public void Transcript_TooLongForTheInputLimit_LeavesOutTheOldestMessages()
    {
        // Arrange
        var messages = Enumerable.Range(0, 20).Select(i => new ChatMessage(ChatRole.User, "message " + i + " " + new string('x', 400))).ToList();

        // Act
        var text = ModelSummarizer.Transcript(null, messages, 1_500);

        // Assert
        text.Should().Contain("earlier messages are left out");
        text.Should().Contain("message 19");
        text.Should().NotContain("message 0 ");
    }
}
