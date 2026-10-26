using BlazeHarness.Core.Config;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bh-store-" + Guid.NewGuid().ToString("N"));
    private readonly JsonFileStore<Doc> _sut;

    public JsonFileStoreTests()
    {
        _sut = new JsonFileStore<Doc>(Path.Combine(_dir, "doc.json"), new LoggerFake<JsonFileStoreTests>());
    }

    public void Dispose()
    {
        _sut.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task UpdateAsync_WritesFile_AndLeavesNoTempFiles()
    {
        // Act
        await _sut.UpdateAsync(d => d with { Count = 3 }, TestContext.Current.CancellationToken);

        // Assert
        _sut.Current.Count.Should().Be(3);
        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().BeEquivalentTo("doc.json");
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
        await File.WriteAllTextAsync(Path.Combine(_dir, "doc.json"), """{"count": 99}""", TestContext.Current.CancellationToken);
        var result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        result.Count.Should().Be(99);
        _sut.Current.Count.Should().Be(99);
    }

    [Fact]
    public void Constructor_InvalidJson_FallsBackToDefault()
    {
        // Arrange
        var path = Path.Combine(_dir, "bad", "doc.json");
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
