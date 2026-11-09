using BlazeHarness.Core.Config;
using BlazeHarness.Tests.Shared;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class PrivateStorageTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-private-");

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenWrite_PrivateWrites_LeaveDirectoryAndOtherFilePermissionsAlone(bool append)
    {
        // Arrange
        var path = _temp.CreateDirectory("private");
        var script = Path.Combine(path, "script");
        await File.WriteAllTextAsync(script, "content", TestContext.Current.CancellationToken);
        var executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead;
        var directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        string? directoryAcl = null;
        string? scriptAcl = null;
        if (OperatingSystem.IsWindows())
        {
            directoryAcl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            scriptAcl = new FileInfo(script).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        }
        else
        {
            File.SetUnixFileMode(path, directoryMode);
            File.SetUnixFileMode(script, executable);
        }

        // Act
        var target = Path.Combine(path, "private.txt");
        if (append)
        {
            await using var stream = PrivateStorage.OpenWrite(target, FileMode.Append);
            await stream.WriteAsync(Encoding.UTF8.GetBytes("private"), TestContext.Current.CancellationToken);
        }
        else
        {
            await AtomicFile.WriteAllOwnerOnlyTextAsync(target, "private", TestContext.Current.CancellationToken);
        }

        // Assert
        new FileInfo(target).ShouldBeOwnerOnly();
        (await File.ReadAllTextAsync(script, TestContext.Current.CancellationToken)).Should().Be("content");
        if (OperatingSystem.IsWindows())
        {
            new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access).Should().Be(directoryAcl);
            new FileInfo(script).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access).Should().Be(scriptAcl);
        }
        else
        {
            File.GetUnixFileMode(path).Should().Be(directoryMode);
            File.GetUnixFileMode(script).Should().Be(executable);
        }
    }

    [Fact]
    public void OpenWrite_MissingFile_IsOwnerOnlyBeforeContentIsWritten()
    {
        // Arrange
        var path = _temp.Combine("private.txt");

        // Act
        using var stream = PrivateStorage.OpenWrite(path, FileMode.CreateNew);

        // Assert
        stream.Length.Should().Be(0);
        new FileInfo(path).ShouldBeOwnerOnly();
    }

    [Fact]
    public async Task OpenWrite_Append_RestrictsPermissionsAndKeepsContents()
    {
        // Arrange
        var path = _temp.Combine("private.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        if (OperatingSystem.IsWindows())
        {
            var security = new FileInfo(path).GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Read,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        // Act
        await using (var stream = PrivateStorage.OpenWrite(path, FileMode.Append))
        {
            new FileInfo(path).ShouldBeOwnerOnly();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("after"), TestContext.Current.CancellationToken);
        }

        // Assert
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("beforeafter");
    }

    [Fact]
    public void OpenWrite_ExclusiveCreationCannotReplaceFile_PreservesContents()
    {
        // Arrange
        var path = _temp.Combine("private.txt");
        File.WriteAllText(path, "kept");

        // Act
        var act = () => PrivateStorage.OpenWrite(path, FileMode.CreateNew);

        // Assert
        act.Should().Throw<IOException>();
        File.ReadAllText(path).Should().Be("kept");
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipType = typeof(SymlinkSupport), SkipUnless = nameof(SymlinkSupport.CanCreate))]
    public void OpenWrite_Link_ThrowsWithoutChangingTarget()
    {
        // Arrange
        var target = _temp.Combine("target");
        File.WriteAllText(target, "kept");
        var link = _temp.Combine("link");
        File.CreateSymbolicLink(link, target);
        var mode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(target);

        // Act
        var act = () => PrivateStorage.OpenWrite(link, FileMode.Append);

        // Assert
        act.Should().Throw<IOException>();
        File.ReadAllText(target).Should().Be("kept");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(target).Should().Be(mode);
        }
    }
}
