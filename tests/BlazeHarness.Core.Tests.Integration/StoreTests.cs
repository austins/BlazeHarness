using BlazeHarness.Core.Config;
using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration;

public sealed class FileSecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bh-secrets-" + Guid.NewGuid().ToString("N"));
    private readonly FileSecretStore _sut;

    public FileSecretStoreTests()
    {
        _sut = new FileSecretStore(Path.Combine(_dir, "secrets.json"));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        _sut.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task SetAsync_ThenGet_ReturnsValue()
    {
        // Act
        await _sut.SetAsync("k", "v", TestContext.Current.CancellationToken);

        // Assert
        (await _sut.GetAsync("k", TestContext.Current.CancellationToken)).Should().Be("v");
        _sut.IsSecure.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesValue()
    {
        // Arrange
        await _sut.SetAsync("k", "v", TestContext.Current.CancellationToken);

        // Act
        await _sut.DeleteAsync("k", TestContext.Current.CancellationToken);

        // Assert
        (await _sut.GetAsync("k", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact(Skip = "Unix file modes only.", SkipWhen = nameof(IsWindows))]
    public async Task SetAsync_OnUnix_FileIsOwnerOnly()
    {
        // Act
        await _sut.SetAsync("k", "v", TestContext.Current.CancellationToken);

        // Assert
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(_dir, "secrets.json")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

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

public sealed class LibsecretStoreTests
{
    public static bool IsUnavailable { get; } = !LibsecretStore.IsAvailableAsync().GetAwaiter().GetResult();

    [Fact(Skip = "Secret Service not available.", SkipWhen = nameof(IsUnavailable))]
    public async Task SetGetDelete_RoundTripsThroughSecretService()
    {
        // Arrange
        using var sut = new LibsecretStore("blazeharness-test-" + Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;

        // Act
        await sut.SetAsync("k", "p@ss \"word\"\nline2", ct);
        var value = await sut.GetAsync("k", ct);
        await sut.DeleteAsync("k", ct);
        var after = await sut.GetAsync("k", ct);

        // Assert
        value.Should().Be("p@ss \"word\"\nline2");
        after.Should().BeNull();
    }
}
