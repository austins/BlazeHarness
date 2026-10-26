using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bh-log-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly SessionLog _sut;

    public SessionLogTests()
    {
        _path = Path.Combine(_dir, "messages.jsonl");
        _sut = new SessionLog(_path, NullLogger.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
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
            new RewindEvent(at, user),
        ];

        // Act
        await _sut.AppendAsync(written, TestContext.Current.CancellationToken);
        var read = await _sut.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        read.Should().Equal(written);
        (await File.ReadAllLinesAsync(_path, TestContext.Current.CancellationToken)).Should().HaveCount(5);
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
        Directory.CreateDirectory(_dir);
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
}
