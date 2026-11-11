using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Tools;
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
    public async Task CompactAsync_ConversationWithATodoList_AddsItToTheSummaryAndNotToWhatTheSummarizerSees()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(10);
        messages.Insert(1, new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("t1", "update_todos", new Dictionary<string, object?>
            {
                ["items"] = new[] { new Dictionary<string, object?> { ["text"] = "write tests", ["status"] = "in_progress" } },
            })
        ]));
        messages.Insert(2, new ChatMessage(ChatRole.Tool, [new FunctionResultContent("t1", "Todo list updated: 0 of 1 done.")]));

        // Act
        var first = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);
        messages.AddRange(Conversation(10));
        var second = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first.Summary.Should().StartWith("the summary").And.Contain("- in progress: write tests");
        second.Summary.Split("[Todo list").Should().HaveCount(2);
        await _summarizer.Received(1).SummarizeAsync("the summary", Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompactAsync_TodosOff_LeavesTheListOutOfTheSummary()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer) { KeepsTodos = false };
        var messages = Conversation(10);
        messages.Insert(1, new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("t1", "update_todos", new Dictionary<string, object?>
            {
                ["items"] = new[] { new Dictionary<string, object?> { ["text"] = "write tests", ["status"] = "in_progress" } },
            })
        ]));
        messages.Insert(2, new ChatMessage(ChatRole.Tool, [new FunctionResultContent("t1", "Todo list updated: 0 of 1 done.")]));

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Summary.Should().Be("the summary");
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

    [Fact]
    public void Size_WithOverhead_AddsItToTheEstimate()
    {
        // Arrange
        var sut = new ConversationContext(10_000, _summarizer, overheadTokens: 700);
        var messages = Conversation(4);

        // Act
        var (tokens, approximate) = sut.Size(messages);

        // Assert
        approximate.Should().BeTrue();
        tokens.Should().Be(700 + TokenEstimate.Of(messages));
    }

    [Fact]
    public async Task CompactAsync_WithOverhead_CountsItInTheSizeAfter()
    {
        // Arrange
        var sut = new ConversationContext(2_000, _summarizer, overheadTokens: 700);
        var messages = Conversation(10);

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.TokensAfter.Should().BeGreaterThanOrEqualTo(700);
        sut.Size(messages).Tokens.Should().Be(result.TokensAfter);
    }

    [Fact]
    public async Task CompactAsync_Automatic_TurnFitsInTheTail_KeepsTheWholeTurnWithItsRequest()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(5);
        AddToolRounds(messages, 2, resultTokens: 100);

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(4);
    }

    [Fact]
    public async Task CompactAsync_Automatic_TurnTooBigToKeep_CutsInsideIt()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        var messages = Conversation(5);
        AddToolRounds(messages, 2, resultTokens: 300);

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(7);
        messages[result.FirstKept].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public async Task CompactAsync_AfterAStepMessage_NeverCutsAtIt()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer);
        List<ChatMessage> messages =
        [
            new(ChatRole.User, Text(100)),
            new(ChatRole.Assistant, Text(100)),
            new(ChatRole.User, Text(100)),
            new(ChatRole.Assistant, Text(800)),
            StepMessage.Create([new TextContent(ChatRunner.TextCallNotice)]),
            new(ChatRole.Assistant, Text(100)),
        ];

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().Be(5);
    }

    [Fact]
    public async Task CompactAsync_OverheadLeavesLittleRoom_KeepsASmallerTail()
    {
        // Arrange
        var withoutOverhead = new ConversationContext(1_000, _summarizer);
        var withOverhead = new ConversationContext(1_000, _summarizer, overheadTokens: 500);
        var messages = Conversation(10);

        // Act
        var plain = await withoutOverhead.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);
        var crowded = await withOverhead.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        plain.Should().NotBeNull();
        crowded.Should().NotBeNull();
        crowded.FirstKept.Should().BeGreaterThan(plain.FirstKept);
    }

    [Fact]
    public async Task ShouldCompact_JustAfterCompactingStillOverTheLimit_WaitsForSomeGrowth()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer, overheadTokens: 950);
        var messages = Conversation(10);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);
        messages.Add(new ChatMessage(ChatRole.User, Text(10)));
        messages.Add(new ChatMessage(ChatRole.Assistant, Text(10)));

        // Act
        var result = sut.ShouldCompact(messages);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldCompact_AfterCompactingAndGrowingEnough_IsTrue()
    {
        // Arrange
        var sut = new ConversationContext(1_000, _summarizer, overheadTokens: 950);
        var messages = Conversation(10);
        await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);
        messages.Add(new ChatMessage(ChatRole.User, Text(100)));
        messages.Add(new ChatMessage(ChatRole.Assistant, Text(100)));

        // Act
        var result = sut.ShouldCompact(messages);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void View_WithAStubBoundary_ReplacesLargeResultsBeforeItAndKeepsTheCallIds()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 4, 1_000);
        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: 5);

        // Act
        var view = sut.View(messages);

        // Assert
        view.Should().HaveCount(messages.Count);
        view.Select(m => m.Role).Should().Equal(messages.Select(m => m.Role));
        view.SelectMany(m => m.Contents.OfType<FunctionCallContent>()).Select(c => c.CallId).Should().Equal("c0", "c1", "c2", "c3");
        view.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => (r.CallId, r.Result)).Should().Equal(
            ("c0", OutputStub.Text("c0")),
            ("c1", OutputStub.Text("c1")),
            ("c2", Text(1_000)),
            ("c3", Text(1_000)));
    }

    [Fact]
    public void View_WithAStubBoundary_NeverChangesTheMessagesOfTheList()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 3, 1_000);
        var before = messages.ToList();
        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: 7);

        // Act
        var view = sut.View(messages);

        // Assert
        messages.Should().Equal(before);
        messages.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Should().OnlyContain(r => (string?)r.Result == Text(1_000));
        view[2].Should().NotBeSameAs(messages[2]);
        view[1].Should().BeSameAs(messages[1]);
    }

    [Fact]
    public void View_CalledTwice_GivesTheSameRequest()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 4, 1_000);
        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: 5);

        // Act
        var first = sut.View(messages);
        var second = sut.View(messages);

        // Assert
        TextOf(second).Should().Equal(TextOf(first));
    }

    [Fact]
    public void View_ResultsOfTheTodoSkillAndQuestionTools_AreNeverStubbed()
    {
        // Arrange
        List<ChatMessage> messages = [new ChatMessage(ChatRole.User, "go")];
        foreach (var name in new[] { "update_todos", "load_skill", "ask_user", "shell" })
        {
            messages.AddRange(ToolRound("id-" + name, name, Text(1_000)));
        }

        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: messages.Count);

        // Act
        var view = sut.View(messages);

        // Assert
        view.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => (r.CallId, Stubbed: OutputStub.IsStub((string?)r.Result))).Should().Equal(
            ("id-update_todos", false),
            ("id-load_skill", false),
            ("id-ask_user", false),
            ("id-shell", true));
    }

    [Fact]
    public void View_SmallResultsAndFailedOnes_KeepWhatTheModelNeeds()
    {
        // Arrange
        List<ChatMessage> messages = [new ChatMessage(ChatRole.User, "go")];
        messages.AddRange(ToolRound("small", "shell", "ok"));
        messages.AddRange(ToolRound("failed", "shell", "Error: exit code 1\n" + Text(1_000)));
        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: messages.Count);

        // Act
        var view = sut.View(messages);

        // Assert
        var results = view.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).ToDictionary(r => r.CallId, r => (string?)r.Result);
        results["small"].Should().Be("ok");
        results["failed"].Should().Be("Error: exit code 1\n" + OutputStub.Text("failed"));
        view[1].Should().BeSameAs(messages[1]);
    }

    [Fact]
    public void View_AfterASummary_StubsOnlyTheMessagesBetweenTheKeptStartAndTheBoundary()
    {
        // Arrange
        List<ChatMessage> messages = [new ChatMessage(ChatRole.System, "system"), new ChatMessage(ChatRole.User, "go")];
        for (var i = 0; i < 6; i++)
        {
            messages.AddRange(ToolRound("c" + i, "read_file", Text(1_000)));
        }

        var sut = new ConversationContext(10_000, _summarizer, 1, "the notes", keptFrom: 5, stubbedBefore: 9);

        // Act
        var view = sut.View(messages);

        // Assert
        view[0].Should().BeSameAs(messages[0]);
        view[1].Text.Should().Contain("the notes");
        view.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => (r.CallId, Stubbed: OutputStub.IsStub((string?)r.Result))).Should().Equal(
            ("c2", true),
            ("c3", true),
            ("c4", false),
            ("c5", false));
    }

    [Fact]
    public void TrimOutput_OldResultsAreEnoughToReachTheTarget_StubsThemAndNeverAsksTheSummarizer()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        var sut = new ConversationContext(5_000, _summarizer);

        // Act
        var trimmed = sut.TrimOutput(messages);

        // Assert
        trimmed.Should().NotBeNull();
        trimmed.TrimmedBefore.Should().Be(11);
        trimmed.TokensAfter.Should().BeLessThan(trimmed.TokensBefore / 2);
        sut.View(messages).SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Count(r => OutputStub.IsStub((string?)r.Result)).Should().Be(5);
        sut.Summary.Should().BeNull();
        sut.ShouldCompact(messages).Should().BeFalse();
        _summarizer.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void TrimOutput_OldToolPicture_IsReplacedByANoteAndANewOneStays()
    {
        // Arrange
        var messages = Conversation(1);
        AddPictureRound(messages, "s0", "0000aaaa");
        AddToolRounds(messages, 6, 1_000);
        AddPictureRound(messages, "s1", "0000bbbb");
        var sut = new ConversationContext(5_000, _summarizer);

        // Act
        var trimmed = sut.TrimOutput(messages);

        // Assert
        trimmed.Should().NotBeNull();
        var view = sut.View(messages);
        view.SelectMany(m => m.Contents.OfType<DataContent>()).Should().ContainSingle();
        string.Concat(TextOf(view)).Should()
            .Contain("[Picture from browser_screenshot call s0, attachment 0000aaaa was removed to save context. Read it with read_attachment and the id 0000aaaa if you need it.]")
            .And.Contain("[Picture from browser_screenshot call s1, attachment 0000bbbb:]");
        messages.SelectMany(m => m.Contents.OfType<DataContent>()).Should().HaveCount(2);
    }

    [Fact]
    public void TrimOutput_StubbingIsNotEnough_ChangesNothing()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        var sut = new ConversationContext(5_000, _summarizer, overheadTokens: 2_900);
        var before = TextOf(sut.View(messages));

        // Act
        var trimmed = sut.TrimOutput(messages);

        // Assert
        trimmed.Should().BeNull();
        TextOf(sut.View(messages)).Should().Equal(before);
    }

    [Fact]
    public void TrimOutput_NoResultIsLargeEnough_ReturnsNull()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 40, 100);
        var sut = new ConversationContext(2_000, _summarizer);

        // Act
        var trimmed = sut.TrimOutput(messages);

        // Assert
        trimmed.Should().BeNull();
    }

    [Fact]
    public void TrimOutput_WithNoLimit_ReturnsNull()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        var sut = new ConversationContext(0, _summarizer);

        // Act
        var trimmed = sut.TrimOutput(messages);

        // Assert
        trimmed.Should().BeNull();
    }

    [Fact]
    public void TrimOutput_CalledAgainAfterTheConversationGrew_MovesTheBoundaryForwardOnly()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        var sut = new ConversationContext(5_000, _summarizer);
        var first = sut.TrimOutput(messages)!;
        var viewAfterFirst = TextOf(sut.View(messages));
        var again = sut.TrimOutput(messages);

        // Act
        messages.AddRange(Enumerable.Range(10, 6).SelectMany(i => ToolRound("c" + i, "read_file", Text(1_000))));
        var second = sut.TrimOutput(messages);

        // Assert
        again.Should().BeNull();
        TextOf(sut.View(messages)).Take(first.TrimmedBefore).Should().Equal(viewAfterFirst.Take(first.TrimmedBefore));
        second.Should().NotBeNull();
        second.TrimmedBefore.Should().BeGreaterThan(first.TrimmedBefore);
    }

    [Fact]
    public async Task CompactAsync_AfterATrimThenGrowth_SummarizesTheFullResultsAndCoversTheStubs()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        var sut = new ConversationContext(5_000, _summarizer);
        var trimmed = sut.TrimOutput(messages)!;
        messages.AddRange(Enumerable.Range(10, 6).SelectMany(i => ToolRound("c" + i, "read_file", Text(1_000))));
        IReadOnlyList<ChatMessage>? seen = null;
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Do<IReadOnlyList<ChatMessage>>(m => seen = m), Arg.Any<CancellationToken>()).Returns("later summary");

        // Act
        var result = await sut.CompactAsync(messages, manual: false, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FirstKept.Should().BeGreaterThan(trimmed.TrimmedBefore);
        seen.Should().NotBeNull();
        seen.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Should().OnlyContain(r => (string?)r.Result == Text(1_000));
        var view = sut.View(messages);
        view[0].Text.Should().Contain("later summary");
        view.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Should().OnlyContain(r => (string?)r.Result == Text(1_000));
    }

    [Fact]
    public async Task CompactAsync_Manual_KeepsTheStubsThatAlreadyAppliedToTheKeptMessages()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 6, 1_000);
        messages.Add(new ChatMessage(ChatRole.User, "next"));
        var sut = new ConversationContext(5_000, _summarizer, stubbedBefore: 11);

        // Act
        var result = await sut.CompactAsync(messages, manual: true, TestContext.Current.CancellationToken);

        // Assert
        result!.FirstKept.Should().Be(13);
        await _summarizer.Received(1).SummarizeAsync(null, Arg.Is<IReadOnlyList<ChatMessage>>(m => m.SelectMany(x => x.Contents.OfType<FunctionResultContent>()).All(r => (string?)r.Result == Text(1_000))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Size_WithAStubBoundary_CountsTheStubsNotTheOutput()
    {
        // Arrange
        var messages = Conversation(1);
        AddToolRounds(messages, 4, 1_000);
        var full = new ConversationContext(10_000, _summarizer).Size(messages).Tokens;
        var sut = new ConversationContext(10_000, _summarizer, stubbedBefore: 7);

        // Act
        var size = sut.Size(messages);

        // Assert
        size.Approximate.Should().BeTrue();
        size.Tokens.Should().BeLessThan(full - 2_000);
    }

    private static List<string> TextOf(IList<ChatMessage> messages)
    {
        return [
            .. messages.Select(m => string.Concat(m.Contents.Select(c => c switch
            {
                TextContent text => text.Text,
                FunctionResultContent result => result.CallId + ":" + result.Result,
                FunctionCallContent call => call.CallId + "=" + call.Name,
                _ => string.Empty,
            })))
        ];
    }

    private static ChatMessage[] ToolRound(string callId, string tool, string result)
    {
        return
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, result)]),
        ];
    }

    /// <summary>
    /// Create messages that alternate between user and assistant roles.
    /// </summary>
    /// <param name="count">The number of messages.</param>
    /// <param name="tokens">The approximate tokens per message, defaulting to 100.</param>
    private static List<ChatMessage> Conversation(int count, int tokens = 100)
    {
        return [.. Enumerable.Range(0, count).Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, Text(tokens)))];
    }

    private static void AddToolRounds(List<ChatMessage> messages, int rounds, int resultTokens)
    {
        for (var i = 0; i < rounds; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c" + i, "read_file", new Dictionary<string, object?> { ["path"] = "a" })]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c" + i, Text(resultTokens))]));
        }
    }

    private static void AddPictureRound(List<ChatMessage> messages, string callId, string attachmentId)
    {
        var picture = new ToolImage(new DataContent(new byte[] { 1, 2, 3 }, "image/png"), new AttachmentInfo(attachmentId, "shot.png", "image/png", 3, 0));
        messages.AddRange(ToolRound(callId, "browser_screenshot", "saved"));
        messages.Add(AttachmentMessage.ForToolImages([("browser_screenshot", callId, [picture])])!);
    }

    private static string Text(int tokens) => new('x', tokens * TokenEstimate.CharactersPerToken);
}
