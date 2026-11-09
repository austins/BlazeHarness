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
    public async Task ExecuteAsync_WordsInAMessage_ReturnsNewestFirstWithNumbersAndRefs()
    {
        // Arrange
        var first = Add(ChatRole.User, "the build fails with CS0246");
        Add(ChatRole.Assistant, "unrelated");
        var third = Add(ChatRole.Assistant, "CS0246 means a type is missing");

        // Act
        var result = await RunAsync("""{"query":"cs0246"}""");

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Split('\n').Should().Equal(
            "2 matches, best first.",
            $"#3 assistant [{SearchChatTool.RefOf(third)}]: CS0246 means a type is missing",
            $"#1 user [{SearchChatTool.RefOf(first)}]: the build fails with CS0246");
    }

    [Fact]
    public async Task ExecuteAsync_AnyWord_PutsMessagesWithMoreOfTheWordsFirst()
    {
        // Arrange
        Add(ChatRole.User, "the build fails with CS0246");
        Add(ChatRole.Assistant, "CS0246 means a type is missing");

        // Act
        var result = await RunAsync("""{"query":"build CS0246"}""");

        // Assert
        result.Output.Split('\n')[1].Should().StartWith("#1 user");
    }

    [Fact]
    public async Task ExecuteAsync_AnyWord_PutsTheRarerWordFirst()
    {
        // Arrange
        Add(ChatRole.User, "zebra");
        Add(ChatRole.User, "file one");
        Add(ChatRole.User, "file two");
        Add(ChatRole.User, "file three");

        // Act
        var result = await RunAsync("""{"query":"file zebra"}""");

        // Assert
        result.Output.Split('\n')[1].Should().StartWith("#1 user");
    }

    [Theory]
    [InlineData("cafe", "Café au lait")]
    [InlineData("config", "the configuration file")]
    [InlineData("entry", "two entries")]
    [InlineData("Files", "one file")]
    public async Task ExecuteAsync_OtherFormsOfAWord_AreFound(string query, string text)
    {
        // Arrange
        Add(ChatRole.User, text);

        // Act
        var result = await RunAsync($$"""{"query":"{{query}}"}""");

        // Assert
        result.Output.Should().StartWith("1 matches").And.Contain(text);
    }

    [Fact]
    public async Task ExecuteAsync_AllWords_ReturnsOnlyMessagesWithEveryWordNewestFirst()
    {
        // Arrange
        Add(ChatRole.User, "red apple");
        Add(ChatRole.User, "red car");
        Add(ChatRole.User, "the apple is red");

        // Act
        var result = await RunAsync("""{"query":"red apple","match":"all"}""");

        // Assert
        result.Output.Split('\n').Select(l => l.Split(' ')[0]).Should().Equal("2", "#3", "#1");
        result.Output.Should().StartWith("2 matches, newest first.");
    }

    [Fact]
    public async Task ExecuteAsync_OnlyFillerWords_MatchesThemAsWritten()
    {
        // Arrange
        Add(ChatRole.User, "to be or not to be");

        // Act
        var result = await RunAsync("""{"query":"to"}""");

        // Assert
        result.Output.Should().StartWith("1 matches, newest first.");
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
        result.Output.Should().Contain("#1 shell output [c1]: Failed: 3 tests");
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
    public async Task ExecuteAsync_OutputThatWasTrimmedFromTheRequest_IsStillFound()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(new ToolCallEntry("c1", "read_file", """{"path":"a.txt"}""", "a.txt") { Output = "the long file about zebras" });
        _entries.Add(entry);
        _entries.Add(new ChatEntry(ChatRole.Assistant) { Compaction = new CompactionInfo(string.Empty, 900, 300, OutputTrimmed: true) });

        // Act
        var result = await RunAsync("""{"query":"zebras"}""");

        // Assert
        result.Output.Should().Contain("#1 read_file output [c1]: the long file about zebras");
        result.Output.Should().StartWith("1 match");
    }

    [Fact]
    public async Task ExecuteAsync_MoreMatchesThanTheLimit_SaysHowManyWereLeftOut()
    {
        // Arrange
        _entries.AddRange(Enumerable.Range(1, 5).Select(i => new ChatEntry(ChatRole.User, $"note {i}")));

        // Act
        var result = await RunAsync("""{"query":"note","max_results":2}""");

        // Assert
        result.Output.Split('\n').Select(l => l.Split(' ')[0]).Should().Equal("Showing", "#5", "#4");
        result.Output.Should().StartWith("Showing 2 of 5 matches, best first.");
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
        var here = Add(ChatRole.User, "needle here");
        var old = new ChatEntry(ChatRole.User, "another needle");
        _others.Add(("Old chat", [old]));

        // Act
        var result = await RunAsync("""{"query":"needle","scope":"folder"}""");

        // Assert
        result.Output.Split('\n').Should().Equal(
            "2 matches, best first.",
            $"#1 user [{SearchChatTool.RefOf(here)}]: needle here",
            $"[Old chat] #1 user [{SearchChatTool.RefOf(old)}]: another needle");
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

    [Fact]
    public async Task ExecuteAsync_MessageRef_ReturnsTheWholeMessage()
    {
        // Arrange
        var entry = Add(ChatRole.Assistant, "line one\n" + new string('x', 1000) + "\nneedle");

        // Act
        var result = await RunAsync($$"""{"ref":"{{SearchChatTool.RefOf(entry).ToUpperInvariant()}}"}""");

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith($"#1 assistant [{SearchChatTool.RefOf(entry)}]").And.Contain(new string('x', 1000)).And.Contain("needle");
    }

    [Fact]
    public async Task ExecuteAsync_CallRef_ReturnsTheArgumentsAndOutput()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(new ToolCallEntry("call_7", "shell", """{"command":"make"}""", "make") { Output = "all done" });
        _entries.Add(entry);

        // Act
        var result = await RunAsync("""{"ref":"call_7"}""");

        // Assert
        result.Output.Should().Contain("#1 shell call [call_7]").And.Contain("""Arguments: {"command":"make"}""").And.Contain("Output:\nall done");
    }

    [Fact]
    public async Task ExecuteAsync_CallIdUsedTwice_SearchGivesQualifiedRefsThatReadTheRightCall()
    {
        // Arrange
        var older = new ChatEntry(ChatRole.Assistant);
        older.AddToolCall(new ToolCallEntry("call_0", "shell", """{"command":"a"}""", "a") { Output = "needle from the older call" });
        var newer = new ChatEntry(ChatRole.Assistant);
        newer.AddToolCall(new ToolCallEntry("call_0", "shell", """{"command":"b"}""", "b") { Output = "the newer call" });
        _entries.AddRange([older, newer]);
        var olderRef = SearchChatTool.RefOf(older) + "/call_0";

        // Act
        var search = await RunAsync("""{"query":"needle"}""");
        var read = await RunAsync($$"""{"ref":"{{olderRef}}"}""");
        var bare = await RunAsync("""{"ref":"call_0"}""");

        // Assert
        search.Output.Should().Contain($"[{olderRef}]");
        read.Output.Should().Contain("needle from the older call").And.NotContain("the newer call");
        bare.Output.Should().StartWith($"[2 calls in this chat have the ID call_0; this is the newest. The others: {olderRef}.]").And.Contain("the newer call");
    }

    [Fact]
    public async Task ExecuteAsync_LongOutput_IsReadInPages()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        var lines = Enumerable.Range(1, 2000).Select(i => $"line {i} " + new string('y', 40));
        entry.AddToolCall(new ToolCallEntry("c1", "read_file", """{"path":"a"}""", "a") { Output = string.Join('\n', lines) });
        _entries.Add(entry);

        // Act
        var first = await RunAsync("""{"ref":"c1"}""");
        var next = int.Parse(System.Text.RegularExpressions.Regex.Match(first.Output, "offset (\\d+) to read the rest").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var second = await RunAsync($$"""{"ref":"c1","offset":{{next}}}""");

        // Assert
        first.Output.Length.Should().BeLessThanOrEqualTo(ToolSet.MaxOutputCharacters);
        second.Output.Should().StartWith($"#1 read_file call [c1], from line {next}");
        second.Output.Should().Contain($"\nline {next - 3} ").And.NotContain($"\nline {next - 4} ");
    }

    [Fact]
    public async Task ExecuteAsync_RefInAnotherChat_NeedsTheFolderScope()
    {
        // Arrange
        var old = new ChatEntry(ChatRole.User, "old words");
        _others.Add(("Old", [old]));
        var json = $$"""{"ref":"{{SearchChatTool.RefOf(old)}}"}""";

        // Act
        var inChat = await RunAsync(json);
        var inFolder = await RunAsync(json.Replace("}", ""","scope":"folder"}""", StringComparison.Ordinal));

        // Assert
        inChat.IsError.Should().BeTrue();
        inChat.Output.Should().Contain("scope 'folder'");
        inFolder.Output.Should().StartWith("[Old] #1 user").And.Contain("old words");
    }

    [Fact]
    public async Task ExecuteAsync_OffsetPastTheEnd_IsAnError()
    {
        // Arrange
        var entry = Add(ChatRole.User, "one line");

        // Act
        var result = await RunAsync($$"""{"ref":"{{SearchChatTool.RefOf(entry)}}","offset":5}""");

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"query":"  "}""")]
    [InlineData("""{}""")]
    [InlineData("""{"query":"x","scope":"everything"}""")]
    [InlineData("""{"query":"x","match":"some"}""")]
    public async Task ExecuteAsync_BadArguments_IsAnError(string json)
    {
        // Act
        var result = await RunAsync(json);

        // Assert
        result.IsError.Should().BeTrue();
    }

    private ChatEntry Add(ChatRole role, string text)
    {
        var entry = new ChatEntry(role, text);
        _entries.Add(entry);
        return entry;
    }

    private Task<ToolResult> RunAsync(string json) => _sut.ExecuteAsync(ToolArguments.Parse(json), _context, TestContext.Current.CancellationToken);
}
