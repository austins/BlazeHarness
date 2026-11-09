using System.Security.AccessControl;
using System.Security.Principal;

namespace BlazeHarness.Core.Tests.Integration.Config;

internal static class PrivateStorageAssertions
{
    extension(FileInfo entry)
    {
        public void ShouldBeOwnerOnly()
        {
            if (OperatingSystem.IsWindows())
            {
                var security = entry.GetAccessControl(AccessControlSections.Access);
                security.AreAccessRulesProtected.Should().BeTrue();
                var rule = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>().Should().ContainSingle().Which;
                using var identity = WindowsIdentity.GetCurrent();
                rule.IdentityReference.Should().Be(identity.User);
                rule.AccessControlType.Should().Be(AccessControlType.Allow);
                rule.FileSystemRights.Should().Be(FileSystemRights.FullControl);
                rule.InheritanceFlags.Should().Be(InheritanceFlags.None);
            }
            else
            {
                File.GetUnixFileMode(entry.FullName).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }
}
