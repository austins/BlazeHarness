using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionLogTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-log-");
    private readonly string _path;
    private readonly SessionLog _sut;

    public SessionLogTests()
    {
        _path = Path.Combine(_temp.Root, "messages.jsonl");
        _sut = new SessionLog(_path, NullLogger.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task ReadAsync_NoFile_ReturnsNoEvents()
    {
        // Act
        var events = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_ThenRead_RoundTripsEveryEventType()
    {
        // Arrange
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        SessionEvent[] written =
        [
            new UserMessageEvent(at, user, "Hello\nworld \"quoted\" ✓"),
            new AssistantStartEvent(at, reply, "Local · fast"),
            new AssistantDeltaEvent(at, reply, "Hi", "thinking"),
            new AssistantEndEvent(at, reply, EntryStatus.Done, null, new UsageReported(12, 40, 3)),
            new CompactionEvent(at, Guid.NewGuid(), reply, "Summary\nof \"it\"", 9000, 700),
            new RewindEvent(at, user),
        ];

        // Act
        await _sut.AppendAsync(written, TestContext.Current.CancellationToken);
        var read = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        read.Should().Equal(written);
        (await File.ReadAllLinesAsync(_path, TestContext.Current.CancellationToken)).Should().HaveCount(6);
    }

    [Theory]
    [InlineData("{\"at\":\"2026-01-02T03:04:05+00:00\",\"text\":\"no type\"}")]
    [InlineData("{\"type\":\"from-the-future\",\"at\":\"2026-01-02T03:04:05+00:00\"}")]
    public async Task ReadAsync_LineWithMissingOrUnknownType_SkipsItAndKeepsTheRest(string line)
    {
        // Arrange
        var id = Guid.NewGuid();
        await _sut.AppendAsync([new UserMessageEvent(DateTimeOffset.UnixEpoch, id, "kept")], TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(_path, line + "\n", TestContext.Current.CancellationToken);

        // Act
        var read = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        read.Should().ContainSingle().Which.Should().BeOfType<UserMessageEvent>().Which.EntryId.Should().Be(id);
    }

    [Fact]
    public async Task ReadAsync_TruncatedLastLine_SkipsItAndKeepsTheRest()
    {
        // Arrange
        var id = Guid.NewGuid();
        await _sut.AppendAsync([new UserMessageEvent(DateTimeOffset.UnixEpoch, id, "kept")], TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(_path, "{\"type\":\"user\",\"at\":\"2026-01", TestContext.Current.CancellationToken);

        // Act
        var read = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        read.Should().ContainSingle().Which.Should().BeOfType<UserMessageEvent>().Which.Text.Should().Be("kept");
    }

    [Fact]
    public async Task AppendAsync_AfterTruncatedLine_StartsOnAFreshLine()
    {
        // Arrange
        Directory.CreateDirectory(_temp.Root);
        await File.WriteAllTextAsync(_path, "{\"type\":\"user\",\"at\":\"2026-01", TestContext.Current.CancellationToken);

        // Act
        await _sut.AppendAsync([new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "after")], TestContext.Current.CancellationToken);
        var read = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        read.Should().ContainSingle().Which.Should().BeOfType<UserMessageEvent>().Which.Text.Should().Be("after");
    }

    [Fact]
    public async Task AppendAsync_Concurrent_NeverInterleavesLines()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var reply = Guid.NewGuid();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            _sut.AppendAsync([new AssistantDeltaEvent(DateTimeOffset.UnixEpoch, reply, new string('x', 2000) + i, string.Empty)], ct)));
        var read = await _sut.ReadAsync(ct);

        // Assert
        read.Should().HaveCount(40);
    }

    [Fact]
    public async Task AppendAsync_LongToolResult_KeepsOnlyAPreviewInTheLog()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var output = new string('x', SessionLog.OutputFileThreshold + 1);

        // Act
        await _sut.AppendAsync([new ToolResultEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "c1", ToolCallStatus.Succeeded, output)], ct);

        // Assert
        (await File.ReadAllTextAsync(_path, ct)).Length.Should().BeLessThan(2_000);
        var saved = Directory.GetFiles(Path.Combine(_temp.Root, SessionLog.OutputsFolder)).Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(saved, ct)).Should().Be(output);
    }

    [Fact]
    public async Task ReadAsync_LongToolResult_ReturnsTheWholeText()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var output = string.Concat(Enumerable.Range(0, 3_000).Select(i => $"line {i}\n"));
        await _sut.AppendAsync([new ToolResultEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "c1", ToolCallStatus.Failed, output)], ct);

        // Act
        var read = await _sut.ReadAsync(ct);

        // Assert
        var result = read.Should().ContainSingle().Which.Should().BeOfType<ToolResultEvent>().Subject;
        result.Output.Should().Be(output);
        result.Status.Should().Be(ToolCallStatus.Failed);
    }

    [Fact]
    public async Task AppendAsync_ShortToolResult_StaysInTheLog()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        await _sut.AppendAsync([new ToolResultEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "c1", ToolCallStatus.Succeeded, new string('x', SessionLog.OutputFileThreshold))], ct);

        // Assert
        Directory.Exists(Path.Combine(_temp.Root, SessionLog.OutputsFolder)).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_SavedOutputMissing_ReturnsThePreviewWithANote()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _sut.AppendAsync([new ToolResultEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "c1", ToolCallStatus.Succeeded, new string('x', SessionLog.OutputFileThreshold + 1))], ct);
        Directory.Delete(Path.Combine(_temp.Root, SessionLog.OutputsFolder), recursive: true);

        // Act
        var read = await _sut.ReadAsync(ct);

        // Assert
        read.Should().ContainSingle().Which.Should().BeOfType<ToolResultEvent>().Which.Output.Should().EndWith("could not be read.]");
    }

    [Fact]
    public async Task ReadAsync_OutputFileNamePointsOutsideTheFolder_IsNotRead()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_temp.Root);
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "secret.txt"), "secret", ct);
        await _sut.AppendAsync([new ToolResultEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "c1", ToolCallStatus.Succeeded, "preview", "../secret.txt")], ct);

        // Act
        var read = await _sut.ReadAsync(ct);

        // Assert
        read.Should().ContainSingle().Which.Should().BeOfType<ToolResultEvent>().Which.Output.Should().Be("preview");
    }
}
