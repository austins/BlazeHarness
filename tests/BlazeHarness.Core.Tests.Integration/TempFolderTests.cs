using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration;

public sealed class TempFolderTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public void Dispose_ReadOnlyFile_DeletesTheFolder()
    {
        // Act
        string root;
        using (var sut = new TempFolder("bh-cleanup-"))
        {
            root = sut.Root;
            var path = sut.Combine("object.bin");
            File.WriteAllText(path, "x");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        // Assert
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact(Skip = "Windows file locking only.", SkipUnless = nameof(IsWindows))]
    public void Dispose_FileOpenForAMoment_WaitsAndDeletesTheFolder()
    {
        // Arrange
        using var sut = new TempFolder("bh-cleanup-");
        using var busy = new FileStream(sut.Combine("busy.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var release = new Timer(_ => busy.Dispose(), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

        // Act
#pragma warning disable IDISP016, IDISP017 // Disposing explicitly is the act under test; the second dispose at scope end does nothing.
        sut.Dispose();
#pragma warning restore IDISP016, IDISP017

        // Assert
        Directory.Exists(sut.Root).Should().BeFalse();
    }

    [Fact(Skip = "Windows file attributes only.", SkipUnless = nameof(IsWindows))]
    public void Dispose_HiddenGitFolderWithAReadOnlyObject_DeletesTheFolder()
    {
        // Act
        string root;
        using (var sut = new TempFolder("bh-cleanup-"))
        {
            root = sut.Root;
            var git = sut.CreateDirectory(".git");
            File.SetAttributes(git, File.GetAttributes(git) | FileAttributes.Hidden);
            var path = Path.Combine(sut.CreateDirectory(".git", "objects", "ab"), "object.bin");
            File.WriteAllText(path, "x");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        // Assert
        Directory.Exists(root).Should().BeFalse();
    }
}
