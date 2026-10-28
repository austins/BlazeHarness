using BlazeHarness.Core.Secrets;
using BlazeHarness.Tests.Shared;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class FileSecretStoreTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-secrets-");
    private readonly FileSecretStore _sut;

    public FileSecretStoreTests()
    {
        _sut = new FileSecretStore(Path.Combine(_temp.Root, "secrets.json"));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
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
        var path = Path.Combine(_temp.Root, "secrets.json");
        Directory.CreateDirectory(_temp.Root);
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
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().Equal("secrets.json");
    }

    [Fact(Skip = "Unix file modes only.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task SetAsync_OnUnix_FileIsOwnerOnly()
    {
        // Act
        await _sut.SetAsync("k", "v", TestContext.Current.CancellationToken);

        // Assert
        File.GetUnixFileMode(Path.Combine(_temp.Root, "secrets.json")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
