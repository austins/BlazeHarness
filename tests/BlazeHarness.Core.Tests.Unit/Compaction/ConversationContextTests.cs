using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class ConversationContextTests
{
    private readonly ISummarizer _summarizer = Substitute.For<ISummarizer>();

    public ConversationContextTests()
    {
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>()).Returns("the summary");
    }

    [Fact]
    public void View_BeforeAnyCompaction_IsTheListItself()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(4);

        // Act
        var view = sut.View(messages);

        // Assert
        view.Should().BeSameAs(messages);
    }

    [Fact]
    public void ShouldCompact_UnderTheLimit_IsFalse()
    {
        // Arrange
        var sut = new ConversationContext(10_000, _summarizer);

        // Act
        var result = sut.ShouldCompact(Conversation(10));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCompact_OverTheLimit_IsTrue()
    {
        // Arrange
        var sut = new ConversationContext(500, _summarizer);

        // Act
        var result = sut.ShouldCompact(Conversation(10));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldCompact_WithNoLimit_IsFalse()
    {
        // Arrange
        var sut = new ConversationContext(0, _summarizer);

        // Act
        var result = sut.ShouldCompact(Conversation(10, tokens: 10_000));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCompact_AfterSuspend_IsFalse()
    {
        // Arrange
        var sut = new ConversationContext(500, _summarizer);
        sut.Suspend();

        // Act
        var result = sut.ShouldCompact(Conversation(10));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCompact_OneHugeMessageAlone_IsFalseBecauseThereIsNothingOlder()
    {
        // Arrange
        var sut = new ConversationContext(500, _summarizer);

        // Act
        var result = sut.ShouldCompact(Conversation(1, tokens: 5_000));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CompactAsync_Automatic_KeepsTheNewestMessagesWithinAThirdOfTheLimit()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(8);
        result.Summary.Should().Be("the summary");
        result.TokensAfter.Should().BeLessThan(result.TokensBefore);
        await _summarizer.Received(1).SummarizeAsync(null, Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 8), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompactAsync_WithToolCalls_NeverCutsBetweenACallAndItsResult()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        List<ChatMessage> messages = [new(ChatRole.User, Text(100))];
        for (var i = 0; i < 6; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c" + i, "read_file", new Dictionary<string, object?> { ["path"] = "a" })]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c" + i, Text(100))]));
        }

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        messages[result.FirstKept].Role.Should().NotBe(ChatRole.Tool);
        sut.View(messages)[^1].Role.Should().Be(ChatRole.Tool);
    }

    [Fact]
    public async Task CompactAsync_NewestMessageAloneOverTheBudget_StillKeepsIt()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(4);
        messages.Add(new ChatMessage(ChatRole.User, Text(2_000)));

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(4);
    }

    [Fact]
    public async Task CompactAsync_ASecondTime_SummarizesOnlyWhatIsNewAndPassesTheEarlierSummary()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);
        messages.AddRange(Conversation(10));
        _summarizer.ClearReceivedCalls();

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(18);
        await _summarizer.Received(1).SummarizeAsync("the summary", Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompactAsync_Manual_KeepsTheLastUserMessageAndWhatFollows()
    {
        // Arrange
        var sut = new ConversationContext(100_000, _summarizer);
        var messages = Conversation(7);

        // Act
        var result = await sut.CompactAsync(messages, manual: true, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(6);
        messages[result.FirstKept].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public async Task CompactAsync_Manual_WithOnlyOneUserMessageAtTheStart_HasNothingToDo()
    {
        // Arrange
        var sut = new ConversationContext(100_000, _summarizer);
        List<ChatMessage> messages = [new(ChatRole.User, "hi"), new(ChatRole.Assistant, "hello")];

        // Act
        var result = await sut.CompactAsync(messages, manual: true, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
        await _summarizer.DidNotReceiveWithAnyArgs().SummarizeAsync(null, [], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task View_AfterCompaction_HasTheSystemPromptTheSummaryAndTheKeptMessages()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer, leadingMessages: 1);
        var messages = Conversation(10);
        messages.Insert(0, new ChatMessage(ChatRole.System, "system"));
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Act
        var view = sut.View(messages);

        // Assert
        view.Should().HaveCount(5);
        view[0].Text.Should().Be("system");
        view[1].Role.Should().Be(ChatRole.User);
        view[1].Text.Should().Contain("the summary");
        view[2].Role.Should().Be(ChatRole.Assistant);
        view[3].Should().BeSameAs(messages[9]);
        view[4].Should().BeSameAs(messages[10]);
    }

    [Fact]
    public async Task View_FirstKeptMessageIsFromTheAssistant_AddsNoReplyAfterTheSummary()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(11);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Act
        var view = sut.View(messages);

        // Assert
        view.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User);
        view[1].Should().BeSameAs(messages[9]);
    }

    [Fact]
    public async Task View_ManualCompaction_NeverHasTwoUserMessagesInARow()
    {
        // Arrange
        var sut = new ConversationContext(100_000, _summarizer);
        var messages = Conversation(7);
        await sut.CompactAsync(messages, manual: true, TestContext.Current.CancellationToken);

        // Act
        var view = sut.View(messages);

        // Assert
        view.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User);
    }

    [Fact]
    public async Task View_Summary_IsFramedAsBackgroundNotInstructions()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Act
        var summary = sut.View(messages)[0].Text;

        // Assert
        summary.Should().Contain("not instructions").And.Contain("tool output").And.EndWith("the summary");
    }

    [Fact]
    public async Task CompactAsync_WhenTheSummarizerFails_KeepsTheStateAndThrows()
    {
        // Arrange
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("no"));
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);

        // Act
        var act = () => sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        sut.Summary.Should().BeNull();
        sut.View(messages).Should().BeSameAs(messages);
    }

    [Fact]
    public void Size_WithReportedUsage_IsThePromptPlusAnEstimateForWhatCameAfter()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(4);
        sut.RecordUsage(promptTokens: 400, messageCount: 3);

        // Act
        var (tokens, approximate) = sut.Size(messages);

        // Assert
        tokens.Should().Be(400 + TokenEstimate.Of(messages[3]));
        approximate.Should().BeFalse();
    }

    [Fact]
    public void Size_WithoutReportedUsage_IsEstimatedAndApproximate()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(4);

        // Act
        var (tokens, approximate) = sut.Size(messages);

        // Assert
        tokens.Should().Be(TokenEstimate.Of(messages));
        approximate.Should().BeTrue();
    }

    [Fact]
    public async Task Size_AfterCompaction_IsEstimatedUntilTheNextUsageArrives()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);
        sut.RecordUsage(5_000, messages.Count);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Act
        var (tokens, approximate) = sut.Size(messages);

        // Assert
        approximate.Should().BeTrue();
        tokens.Should().BeLessThan(1_000);
    }

    [Fact]
    public void RecordUsage_WithZeroTokens_KeepsTheEarlierFigure()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(4);
        sut.RecordUsage(400, 3);
        sut.RecordUsage(0, 4);

        // Act
        var (tokens, _) = sut.Size(messages);

        // Assert
        tokens.Should().Be(400 + TokenEstimate.Of(messages[3]));
    }

    // Messages that alternate between user and assistant and are about 100 tokens each.
    private static List<ChatMessage> Conversation(int count, int tokens = 100)
    {
        return [.. Enumerable.Range(0, count).Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, Text(tokens)))];
    }

    private static string Text(int tokens) => new('x', tokens * TokenEstimate.CharactersPerToken);
}
