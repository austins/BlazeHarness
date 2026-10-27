using BlazeHarness.Core.Config;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-store-");
    private readonly JsonFileStore<Doc> _sut;

    public JsonFileStoreTests()
    {
        _sut = new JsonFileStore<Doc>(Path.Combine(_temp.Root, "doc.json"), new LoggerFake<JsonFileStoreTests>());
    }

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task UpdateAsync_WritesFile_AndLeavesNoTempFiles()
    {
        // Act
        await _sut.UpdateAsync(d => d with { Count = 3 }, TestContext.Current.CancellationToken);

        // Assert
        _sut.Current.Count.Should().Be(3);
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().BeEquivalentTo("doc.json");
    }

    [Fact]
    public async Task UpdateAsync_Concurrent_DoesNotLoseUpdates()
    {
        // Act
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _sut.UpdateAsync(d => d with { Count = d.Count + 1 }, TestContext.Current.CancellationToken)));

        // Assert
        _sut.Current.Count.Should().Be(20);
    }

    [Fact]
    public async Task ExternalEdit_IsPickedUp()
    {
        // Arrange
        var changed = new TaskCompletionSource<Doc>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.ExternalChange += (_, e) => changed.TrySetResult(e.Value);

        // Act
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "doc.json"), """{"count": 99}""", TestContext.Current.CancellationToken);
        var result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        result.Count.Should().Be(99);
        _sut.Current.Count.Should().Be(99);
    }

    [Fact]
    public async Task OnExternalChange_HandlerThrows_IsLoggedAndDoesNotEscape()
    {
        // Arrange
        var logger = new LoggerFake<JsonFileStoreTests>();
        using var sut = new JsonFileStore<Doc>(Path.Combine(_temp.Root, "handler.json"), logger, watch: false);
        sut.ExternalChange += (_, _) => throw new InvalidOperationException("boom");
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "handler.json"), """{"count": 5}""", TestContext.Current.CancellationToken);

        // Act
        var act = sut.OnExternalChange;

        // Assert
        act.Should().NotThrow();
        sut.Current.Count.Should().Be(5);
        logger.Entries.Should().ContainSingle(e => e.LogLevel == LogLevel.Error);
    }

    [Fact]
    public async Task OnExternalChange_HandlerUpdatesTheStore_DoesNotDeadlock()
    {
        // Arrange
        using var sut = new JsonFileStore<Doc>(Path.Combine(_temp.Root, "reenter.json"), new LoggerFake<JsonFileStoreTests>(), watch: false);
#pragma warning disable VSTHRD002 // The event handler is synchronous.
        sut.ExternalChange += (_, e) => sut.UpdateAsync(d => d with { Count = e.Value.Count + 1 }).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "reenter.json"), """{"count": 5}""", TestContext.Current.CancellationToken);

        // Act
        await Task.Run(sut.OnExternalChange, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        sut.Current.Count.Should().Be(6);
    }

    [Fact]
    public void OnExternalChange_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var sut = new JsonFileStore<Doc>(Path.Combine(_temp.Root, "gone.json"), new LoggerFake<JsonFileStoreTests>(), watch: false);
#pragma warning disable IDISP016, IDISP017 // Raising the callback after disposal is the scenario.
        sut.Dispose();

        // Act
        var act = sut.OnExternalChange;
#pragma warning restore IDISP016, IDISP017

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_InvalidJson_FallsBackToDefault()
    {
        // Arrange
        var path = Path.Combine(_temp.Root, "bad", "doc.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        // Act
        using var sut = new JsonFileStore<Doc>(path, new LoggerFake<JsonFileStoreTests>(), watch: false);

        // Assert
        sut.Current.Count.Should().Be(0);
    }

    internal sealed record Doc
    {
        public int Count { get; init; }
    }
}
