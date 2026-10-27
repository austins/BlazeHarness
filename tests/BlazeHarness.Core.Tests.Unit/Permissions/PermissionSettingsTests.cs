using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class PermissionSettingsTests
{
    [Theory]
    [InlineData(true, AccessLevel.WriteInFolder)]
    [InlineData(false, AccessLevel.ReadOnly)]
    public void DefaultFor_FolderOrNot_GivesTheStartingAccess(bool hasFolder, AccessLevel expected)
    {
        // Arrange

        // Act
        var settings = PermissionSettings.DefaultFor(hasFolder);

        // Assert
        settings.Access.Should().Be(expected);
        settings.Approvals.Should().Be(ApprovalMode.AskFirst);
        settings.IsRisky.Should().BeFalse();
    }
}
