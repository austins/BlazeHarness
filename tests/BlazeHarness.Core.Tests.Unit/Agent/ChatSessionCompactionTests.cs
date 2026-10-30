using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Tests.Shared.Agent;
using BlazeHarness.Tests.Shared.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSessionCompactionTests
{
    private readonly RecordingSessionLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private int _clientsCreated;

    [Fact]
    public async Task CompactAsync_AfterTwoExchanges_PutsAMarkerBeforeTheLastUserMessageAndKeepsTheHistory()
    {
        // Arrange
        using var client = Exchanges("sum");
        using var sut = Create(client);
        await Send(sut, "one", "two");

        // Act
        var compacted = await sut.CompactAsync(TestTargets.Create());

        // Assert
        compacted.Should().BeTrue();
        sut.Entries.Select(e => e.Compaction is not null ? "marker" : e.Text).Should().Equal("one", "a", "marker", "two", "b");
        sut.Entries[2].Compaction!.Summary.Should().Be("sum");
        sut.History.Should().HaveCount(4);
    }

    [Fact]
    public async Task CompactAsync_ExtractiveMode_MakesNoModelRequestAndNeedsNoClient()
    {
        // Arrange
        using var client = Exchanges();
        using var sut = CreateExtractive(client);
        await Send(sut, "one", "two");
        var clientsBefore = _clientsCreated;

        // Act
        var compacted = await sut.CompactAsync(TestTargets.Create());

        // Assert
        compacted.Should().BeTrue();
        _clientsCreated.Should().Be(clientsBefore);
        client.Requests.Should().HaveCount(2);
        sut.Entries[2].Compaction!.Summary.Should().Contain("[Opening asks]").And.Contain("- one");
    }

    [Fact]
    public async Task CompactAsync_SavesTheCompaction()
    {
        // Arrange
        using var client = Exchanges("sum");
        using var sut = Create(client);
        await Send(sut, "one", "two");

        // Act
        await sut.CompactAsync(TestTargets.Create());

        // Assert
        var saved = _log.Events.OfType<CompactionEvent>().Single();
        saved.Summary.Should().Be("sum");
        saved.EntryId.Should().Be(sut.Entries[2].Id);
        saved.FirstKeptEntryId.Should().Be(sut.Entries[3].Id);
        saved.At.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task SendAsync_AfterCompacting_SendsTheSummaryInsteadOfTheOlderMessages()
    {
        // Arrange
        using var client = Exchanges("sum").Enqueue([ScriptedChatClient.Text("c")]);
        using var sut = Create(client);
        await Send(sut, "one", "two");
        await sut.CompactAsync(TestTargets.Create());

        // Act
        await sut.SendAsync("three", TestTargets.Create());

        // Assert
        var sent = client.Requests[^1];
        sent.Select(m => m.Text).Should().HaveCount(5);
        sent[0].Text.Should().Contain("sum");
        sent.Skip(2).Select(m => m.Text).Should().Equal("two", "b", "three");
        sut.History.Should().HaveCount(6);
    }

    [Fact]
    public async Task CompactAsync_OnlyOneExchange_ReturnsFalseAndChangesNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a")]);
        using var sut = Create(client);
        await sut.SendAsync("one", TestTargets.Create());

        // Act
        var compacted = await sut.CompactAsync(TestTargets.Create());

        // Assert
        compacted.Should().BeFalse();
        sut.Entries.Should().HaveCount(2);
        client.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task CompactAsync_NoMessages_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient();
        using var sut = Create(client);

        // Act
        var compacted = await sut.CompactAsync(TestTargets.Create());

        // Assert
        compacted.Should().BeFalse();
        sut.CanCompact.Should().BeFalse();
    }

    [Fact]
    public async Task CompactAsync_TheModelFails_ThrowsAndLeavesTheConversationAlone()
    {
        // Arrange
        using var client = Exchanges().Enqueue([], new InvalidOperationException("boom"));
        using var sut = Create(client);
        await Send(sut, "one", "two");

        // Act
        var act = async () => await sut.CompactAsync(TestTargets.Create());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        sut.Entries.Should().HaveCount(4);
        sut.Entries.Should().OnlyContain(e => e.Compaction == null);
        sut.CanCompact.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ThePromptGrowsPastTheLimit_CompactsBeforeTheReply()
    {
        // Arrange
        var target = SmallWindow();
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a")])
            .Enqueue([ScriptedChatClient.Text("sum")])
            .Enqueue([ScriptedChatClient.Text("b")]);
        using var sut = Create(client);
        await sut.SendAsync(new string('x', 800), target);

        // Act
        await sut.SendAsync("two", target);

        // Assert
        sut.Entries.Select(e => e.Compaction is not null ? "marker" : e.Text.Length > 10 ? "long" : e.Text).Should().Equal("long", "marker", "a", "two", "b");
        client.Requests.Should().HaveCount(3);
        client.Requests[2][0].Text.Should().Contain("sum");
        client.Requests[2].Select(m => m.Text).Should().NotContain(t => t.Length > 700 && !t.Contains("sum", StringComparison.Ordinal));
        _log.Events.OfType<CompactionEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Restore_ASavedCompaction_PutsTheMarkerBackAndSendsTheSummary()
    {
        // Arrange
        using var first = Exchanges("sum");
        using var original = Create(first);
        await Send(original, "one", "two");
        await original.CompactAsync(TestTargets.Create());
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("c")]);
        using var sut = Create(client);

        // Act
        sut.Restore(await _log.ReadAsync(TestContext.Current.CancellationToken));
        await sut.SendAsync("three", TestTargets.Create());

        // Assert
        sut.Entries.Select(e => e.Compaction is not null ? "marker" : e.Text).Should().Equal("one", "a", "marker", "two", "b", "three", "c");
        sut.Entries[2].Compaction!.Summary.Should().Be("sum");
        client.Requests.Single()[0].Text.Should().Contain("sum");
        client.Requests.Single().Should().HaveCount(5);
    }

    [Fact]
    public async Task RetryAsync_AfterCompacting_KeepsTheCompaction()
    {
        // Arrange
        using var client = Exchanges("sum").Enqueue([], new InvalidOperationException("boom")).Enqueue([ScriptedChatClient.Text("c")]);
        using var sut = Create(client);
        await Send(sut, "one", "two");
        await sut.CompactAsync(TestTargets.Create());
        await sut.SendAsync("three", TestTargets.Create());

        // Act
        await sut.RetryAsync(TestTargets.Create());

        // Assert
        sut.Entries.Select(e => e.Compaction is not null ? "marker" : e.Text).Should().Equal("one", "a", "marker", "two", "b", "three", "c");
        client.Requests[^1][0].Text.Should().Contain("sum");
        client.Requests[^1].Select(m => m.Text).Skip(2).Should().Equal("two", "b", "three");
    }

    [Fact]
    public async Task EditAsync_AMessageBeforeTheMarker_DropsTheCompaction()
    {
        // Arrange
        using var client = Exchanges("sum").Enqueue([ScriptedChatClient.Text("a2")]);
        using var sut = Create(client);
        await Send(sut, "one", "two");
        await sut.CompactAsync(TestTargets.Create());

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "one!", TestTargets.Create());

        // Assert
        sut.Entries.Should().OnlyContain(e => e.Compaction == null);
        client.Requests[^1].Select(m => m.Text).Should().Equal("one!");
    }

    [Fact]
    public async Task EditAsync_TheFirstKeptMessage_KeepsTheCompaction()
    {
        // Arrange
        using var client = Exchanges("sum").Enqueue([ScriptedChatClient.Text("b2")]);
        using var sut = Create(client);
        await Send(sut, "one", "two");
        await sut.CompactAsync(TestTargets.Create());

        // Act
        await sut.EditAsync(sut.Entries[3].Id, "two!", TestTargets.Create());

        // Assert
        sut.Entries.Select(e => e.Compaction is not null ? "marker" : e.Text).Should().Equal("one", "a", "marker", "two!", "b2");
        client.Requests[^1].Select(m => m.Text).Last().Should().Be("two!");
        client.Requests[^1][0].Text.Should().Contain("sum");
    }

    [Fact]
    public async Task GetContextStatus_AfterAReplyWithUsage_UsesTheReportedPromptSize()
    {
        // Arrange
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 10, CachedInputTokenCount = 250 });
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a"), new ChatResponseUpdate(ChatRole.Assistant, [usage])]);
        using var sut = Create(client);
        await sut.SendAsync("one", TestTargets.Create());

        // Act
        var status = sut.GetContextStatus(TestTargets.Create());

        // Assert
        status.Approximate.Should().BeFalse();
        status.Tokens.Should().BeInRange(1000, 1020);
        status.Window.Should().Be(8000);
        status.Limit.Should().Be(5600);
        status.CacheHitRate.Should().Be(0.25);
    }

    [Fact]
    public async Task GetContextStatus_NoUsageReported_IsAnApproximateEstimateWithoutACacheRate()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a")]);
        using var sut = Create(client);
        await sut.SendAsync(new string('x', 400), TestTargets.Create());

        // Act
        var status = sut.GetContextStatus(TestTargets.Create());

        // Assert
        status.Approximate.Should().BeTrue();
        status.Tokens.Should().BeInRange(100, 130);
        status.CacheHitRate.Should().BeNull();
    }

    [Fact]
    public async Task GetContextStatus_AfterCompacting_IsEstimatedFromTheSmallerPrompt()
    {
        // Arrange
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 5000, OutputTokenCount = 10 });
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a"), new ChatResponseUpdate(ChatRole.Assistant, [usage])])
            .Enqueue([ScriptedChatClient.Text("b"), new ChatResponseUpdate(ChatRole.Assistant, [usage])])
            .Enqueue([ScriptedChatClient.Text("sum")]);
        using var sut = Create(client);
        await Send(sut, "one", "two");
        await sut.CompactAsync(TestTargets.Create());

        // Act
        var status = sut.GetContextStatus(TestTargets.Create());

        // Assert
        status.Approximate.Should().BeTrue();
        status.Tokens.Should().BeLessThan(100);
    }

    private static ChatTarget SmallWindow()
    {
        var model = new ModelConfig { Id = "tiny", ContextWindow = 400 };
        var provider = new ProviderConfig { Id = "p1", DisplayName = "P1", BaseUrl = new Uri("http://localhost/v1"), Models = [model] };
        return new ChatTarget(provider, model, null);
    }

    private static ScriptedChatClient Exchanges(params string[] extra)
    {
        var client = new ScriptedChatClient([ScriptedChatClient.Text("a")]).Enqueue([ScriptedChatClient.Text("b")]);
        foreach (var text in extra)
        {
            client.Enqueue([ScriptedChatClient.Text(text)]);
        }

        return client;
    }

    private static async Task Send(ChatSession session, params string[] messages)
    {
        foreach (var message in messages)
        {
            await session.SendAsync(message, TestTargets.Create());
        }
    }

    private ChatSession Create(ScriptedChatClient client) =>
        new((_, _) => Task.FromResult<IChatClient>(client), _time, _log);

    private ChatSession CreateExtractive(ScriptedChatClient client)
    {
        return new ChatSession(
            (_, _) =>
            {
                _clientsCreated++;
                return Task.FromResult<IChatClient>(client);
            },
            _time,
            _log)
        {
            CompactionModeSource = () => CompactionMode.Extractive,
        };
    }
}
