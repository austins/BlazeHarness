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
