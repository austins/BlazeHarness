using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class SearchChatToolTests
{
    private readonly ToolContext _context = new(Path.GetTempPath());
    private readonly List<ChatEntry> _entries = [];
    private readonly List<(string Title, IReadOnlyList<ChatEntry> Entries)> _others = [];
    private readonly SearchChatTool _sut;

    public SearchChatToolTests()
    {
        _sut = new SearchChatTool(() => _entries, _ => Task.FromResult<IReadOnlyList<(string, IReadOnlyList<ChatEntry>)>>(_others));
    }

    [Fact]
    public async Task ExecuteAsync_WordsInAMessage_ReturnsNewestFirstWithNumbers()
    {
        // Arrange
        _entries.Add(new ChatEntry(ChatRole.User, "the build fails with CS0246"));
        _entries.Add(new ChatEntry(ChatRole.Assistant, "unrelated"));
        _entries.Add(new ChatEntry(ChatRole.Assistant, "CS0246 means a type is missing"));

        // Act
        var result = await RunAsync("""{"query":"cs0246"}""");

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Split('\n').Should().Equal("2 matches, newest first.", "#3 assistant: CS0246 means a type is missing", "#1 user: the build fails with CS0246");
    }

    [Fact]
    public async Task ExecuteAsync_ToolCallAndOutput_AreSearched()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(new ToolCallEntry("c1", "shell", """{"command":"dotnet test"}""", "dotnet test") { Output = "Failed: 3 tests" });
        _entries.Add(entry);

        // Act
        var result = await RunAsync("""{"query":"failed tests"}""");

        // Assert
        result.Output.Should().Contain("#1 shell output: Failed: 3 tests");
    }

    [Fact]
    public async Task ExecuteAsync_ReasoningAndSummaryMarkers_AreNotSearched()
    {
        // Arrange
        var thinking = new ChatEntry(ChatRole.Assistant, "visible");
        thinking.AppendReasoning("secret plan");
        _entries.Add(thinking);
        _entries.Add(new ChatEntry(ChatRole.Assistant, "secret plan summary") { Compaction = new CompactionInfo("secret plan", 10, 5) });

        // Act
        var result = await RunAsync("""{"query":"secret plan"}""");

        // Assert
        result.Output.Should().Be("No matches.");
    }

    [Fact]
    public async Task ExecuteAsync_MoreMatchesThanTheLimit_SaysHowManyWereLeftOut()
    {
        // Arrange
        _entries.AddRange(Enumerable.Range(1, 5).Select(i => new ChatEntry(ChatRole.User, $"note {i}")));

        // Act
        var result = await RunAsync("""{"query":"note","max_results":2}""");

        // Assert
        result.Output.Split('\n').Should().Equal("Showing 2 of 5 matches, newest first.", "#5 user: note 5", "#4 user: note 4");
    }

    [Fact]
    public async Task ExecuteAsync_EarlierSearchCalls_AreNotSearched()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(new ToolCallEntry("c1", "search_chat", """{"query":"needle"}""", "needle") { Output = "#1 user: needle" });
        _entries.Add(entry);

        // Act
        var result = await RunAsync("""{"query":"needle"}""");

        // Assert
        result.Output.Should().Be("No matches.");
    }

    [Fact]
    public async Task ExecuteAsync_ChatScope_IgnoresOtherChats()
    {
        // Arrange
        _others.Add(("Old", [new ChatEntry(ChatRole.User, "needle")]));

        // Act
        var result = await RunAsync("""{"query":"needle"}""");

        // Assert
        result.Output.Should().Be("No matches.");
    }

    [Fact]
    public async Task ExecuteAsync_FolderScope_AlsoSearchesOtherChatsAndNamesThem()
    {
        // Arrange
        _entries.Add(new ChatEntry(ChatRole.User, "needle here"));
        _others.Add(("Old chat", [new ChatEntry(ChatRole.User, "another needle")]));

        // Act
        var result = await RunAsync("""{"query":"needle","scope":"folder"}""");

        // Assert
        result.Output.Split('\n').Should().Equal("2 matches, newest first.", "#1 user: needle here", "[Old chat] #1 user: another needle");
    }

    [Fact]
    public async Task ExecuteAsync_LongMessage_ReturnsAShortenedSnippet()
    {
        // Arrange
        _entries.Add(new ChatEntry(ChatRole.User, new string('a', 1000) + " needle " + new string('b', 1000)));

        // Act
        var result = await RunAsync("""{"query":"needle"}""");

        // Assert
        result.Output.Should().Contain("…").And.Contain("needle");
        result.Output.Length.Should().BeLessThan(500);
    }

    [Theory]
    [InlineData("""{"query":"  "}""")]
    [InlineData("""{"query":"x","scope":"everything"}""")]
    public async Task ExecuteAsync_BadArguments_IsAnError(string json)
    {
        // Act
        var result = await RunAsync(json);

        // Assert
        result.IsError.Should().BeTrue();
    }

    private Task<ToolResult> RunAsync(string json) => _sut.ExecuteAsync(ToolArguments.Parse(json), _context, TestContext.Current.CancellationToken);
}
