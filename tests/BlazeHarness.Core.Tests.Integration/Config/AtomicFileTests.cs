using BlazeHarness.Core.Config;
using BlazeHarness.Tests.Shared;
using System.Runtime.Versioning;
using System.Text;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class AtomicFileTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly TempFolder _temp = new("bh-atomic-");

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Write_ExistingFile_ReplacesItAndLeavesNoTempFile()
    {
        // Arrange
        var path = _temp.Combine("data.bin");
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // Act
        AtomicFile.Write(path, [1, 2]);

        // Assert
        File.ReadAllBytes(path).Should().Equal(1, 2);
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().Equal("data.bin");
    }

    [Fact]
    public void Write_MissingFolder_CreatesIt()
    {
        // Arrange
        var path = _temp.Combine("a", "b", "data.bin");

        // Act
        AtomicFile.Write(path, [1]);

        // Assert
        File.ReadAllBytes(path).Should().Equal(1);
    }

    [Fact(Skip = "Unix file modes don't exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public void Write_ExistingFile_KeepsItsUnixMode()
    {
        // Arrange
        var path = _temp.Combine("secret.bin");
        File.WriteAllBytes(path, [0]);
        File.SetUnixFileMode(path, OwnerOnly);

        // Act
        AtomicFile.Write(path, [1]);

        // Assert
        File.GetUnixFileMode(path).Should().Be(OwnerOnly);
    }

    [Fact(Skip = "Unix file modes don't exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public void CreateStagingFile_RestrictiveTarget_IsRestrictedBeforeContentIsWritten()
    {
        // Arrange
        var target = _temp.Combine("private.bin");
        File.WriteAllBytes(target, [0]);
        File.SetUnixFileMode(target, OwnerOnly);
        var staging = _temp.Combine("staging.bin");

        // Act
        using var stream = AtomicFile.CreateStagingFile(staging, target);

        // Assert
        stream.Length.Should().Be(0);
        File.GetUnixFileMode(staging).Should().Be(OwnerOnly);
    }

    [Fact]
    public async Task WriteAllTextAsync_Text_IsUtf8WithoutBomAndLeavesNoTempFile()
    {
        // Arrange
        var path = _temp.Combine("nested", "doc.json");

        // Act
        await AtomicFile.WriteAllTextAsync(path, "caf\u00e9", TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(Encoding.UTF8.GetBytes("caf\u00e9"));
        Directory.GetFiles(_temp.Combine("nested")).Select(Path.GetFileName).Should().Equal("doc.json");
    }

    [Fact(Skip = "Unix file modes don't exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task WriteAllOwnerOnlyTextAsync_NewFile_IsReadableOnlyByTheOwner()
    {
        // Arrange
        var path = _temp.Combine("key.txt");

        // Act
        await AtomicFile.WriteAllOwnerOnlyTextAsync(path, "secret", TestContext.Current.CancellationToken);

        // Assert
        File.GetUnixFileMode(path).Should().Be(OwnerOnly);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("secret");
    }

    [Fact]
    public async Task WriteAllTextAsync_Cancelled_LeavesTheOldContentAndNoTempFile()
    {
        // Arrange
        var path = _temp.Combine("doc.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        // Act
        var act = () => AtomicFile.WriteAllTextAsync(path, "new", new CancellationToken(canceled: true));

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("old");
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().Equal("doc.txt");
    }
}
