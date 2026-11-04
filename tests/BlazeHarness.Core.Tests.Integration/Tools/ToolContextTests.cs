using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ToolContextTests : IDisposable
{
    private readonly TempFolder _temp = new("context-");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TryResolve_ProtectedFile_IsRefusedAtExecution()
    {
        // Arrange
        var path = _temp.Combine("private.txt");
        var context = new ToolContext(_temp.Root).WithOutsideAllowed().WithSensitive(new SensitivePaths([path]));

        // Act
        var allowed = context.TryResolve(path, out _, out var error);

        // Assert
        allowed.Should().BeFalse();
        error.Should().Contain("protected");
    }

    [Fact]
    public void TryResolve_RefusedFile_IsRefusedAtExecution()
    {
        // Arrange
        var rules = new[] { new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "private.txt") };
        var context = new ToolContext(_temp.Root);
        context = context.WithRefused(new RefusedPaths(rules, context.Root));

        // Act
        var allowed = context.TryResolve("private.txt", out _, out var error);

        // Assert
        allowed.Should().BeFalse();
        error.Should().Contain("refused");
    }

    [Fact]
    public void TryResolve_TargetOutsideCheckedPaths_IsRefused()
    {
        // Arrange
        var context = new ToolContext(_temp.Root).WithCheckedPaths([_temp.Combine("approved.txt")]);

        // Act
        var allowed = context.TryResolve("other.txt", out _, out var error);

        // Assert
        allowed.Should().BeFalse();
        error.Should().Contain("changed");
    }
}
