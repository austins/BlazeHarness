using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

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

    [Fact]
    public async Task GetAsync_CorruptFile_MovesItAsideAndTreatsItAsEmpty()
    {
        // Arrange
        var path = Path.Combine(_dir, "secrets.json");
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);

        // Act
        var value = await _sut.GetAsync("k", TestContext.Current.CancellationToken);
        await _sut.SetAsync("k", "v", TestContext.Current.CancellationToken);

        // Assert
        value.Should().BeNull();
        File.Exists(path + ".corrupt").Should().BeTrue();
        (await _sut.GetAsync("k", TestContext.Current.CancellationToken)).Should().Be("v");
    }

    [Fact]
    public async Task SetAsync_Twice_LeavesNoTempFiles()
    {
        // Act
        await _sut.SetAsync("a", "1", TestContext.Current.CancellationToken);
        await _sut.SetAsync("b", "2", TestContext.Current.CancellationToken);

        // Assert
        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().Equal("secrets.json");
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
