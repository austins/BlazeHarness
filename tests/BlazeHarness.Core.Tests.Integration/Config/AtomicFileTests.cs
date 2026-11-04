using BlazeHarness.Core.Config;
using BlazeHarness.Tests.Shared;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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

    [Theory(Skip = "Windows ACLs only.", SkipUnless = nameof(IsWindows))]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("windows")]
    public async Task WriteAllOwnerOnlyTextAsync_OnWindows_OnlyGrantsTheCurrentUserAccess(bool replace)
    {
        // Arrange
        var path = _temp.Combine("key.txt");
        if (replace)
        {
            await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        }

        // Act
        await AtomicFile.WriteAllOwnerOnlyTextAsync(path, "secret", TestContext.Current.CancellationToken);

        // Assert
        using var identity = WindowsIdentity.GetCurrent();
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        security.AreAccessRulesProtected.Should().BeTrue();
        var rule = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Should().ContainSingle().Which;
        rule.IdentityReference.Should().Be(identity.User);
        rule.AccessControlType.Should().Be(AccessControlType.Allow);
        rule.FileSystemRights.Should().Be(FileSystemRights.FullControl);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("secret");
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().Equal("key.txt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAllTextAsync_Cancelled_LeavesTheOldContentAndNoTempFile(bool ownerOnly)
    {
        // Arrange
        var path = _temp.Combine("doc.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);

        // Act
        var act = () => ownerOnly
            ? AtomicFile.WriteAllOwnerOnlyTextAsync(path, "new", new CancellationToken(canceled: true))
            : AtomicFile.WriteAllTextAsync(path, "new", new CancellationToken(canceled: true));

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("old");
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().Equal("doc.txt");
    }
}
