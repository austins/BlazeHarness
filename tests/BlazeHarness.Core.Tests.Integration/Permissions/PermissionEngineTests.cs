using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Permissions;

public sealed class PermissionEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly string _elsewhere = Path.Combine(Path.GetTempPath(), "bh-else-" + Guid.NewGuid().ToString("N"));
    private readonly PermissionEngine _sut = new();

    public PermissionEngineTests()
    {
        Directory.CreateDirectory(_elsewhere);
        File.WriteAllText(Path.Combine(_elsewhere, "note.txt"), "hi");
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static SandboxStatus Available { get; } = new(true, "test", "A test sandbox.");

    public void Dispose()
    {
        _workspace.Dispose();
        Directory.Delete(_elsewhere, recursive: true);
    }

    [Fact]
    public void Evaluate_ReadInsideTheFolder_Allows()
    {
        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = "a.txt" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ToolWithoutPathsOrCommands_Allows()
    {
        // Arrange
        var attachments = new Core.Attachments.SessionAttachments(_workspace.Root);
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AskFirst);

        // Act
        var decision = Evaluate(new ReadAttachmentTool(attachments), new { id = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_ReadOutsideTheFolder_AsksAndKeysByFolder()
    {
        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_elsewhere, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.AllowOutside.Should().BeTrue();
        decision.GrantKey.Should().Be("dir:" + WorkspacePath.CanonicalRoot(_elsewhere));
    }

    [Fact]
    public void Evaluate_ReadOutsideAfterGrant_AllowsThatFolderOnly()
    {
        // Arrange
        var first = Evaluate(new ReadFileTool(), new { path = Path.Combine(_elsewhere, "note.txt") });
        _sut.Remember(first.GrantKey!);

        // Act
        var again = Evaluate(new ReadFileTool(), new { path = Path.Combine(_elsewhere, "other.txt") });
        var elsewhere = Evaluate(new ReadFileTool(), new { path = Path.GetTempPath() });

        // Assert
        again.Should().Be(PermissionDecision.Allow with { AllowOutside = true });
        elsewhere.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Theory]
    [InlineData(AccessLevel.Unrestricted, ApprovalMode.AskFirst)]
    [InlineData(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk)]
    public void Evaluate_ReadOutsideWhenSettingsAllowIt_Allows(AccessLevel access, ApprovalMode approvals)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(access, approvals);

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_elsewhere, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WriteInsideAskFirst_AsksAndCanBeRemembered()
    {
        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = "a.txt", content = "x" });
        _sut.Remember(decision.GrantKey!);
        var again = Evaluate(new WriteFileTool(), new { path = "b.txt", content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("tool:write_file");
        again.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_WriteInsideAutoApprove_Allows()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = "a.txt", content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_WriteWhenReadOnly_Denies()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = "a.txt", content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
        decision.Reason.Should().Contain("Read only");
    }

    [Fact]
    public void Evaluate_WriteOutsideBelowUnrestricted_Denies()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_elsewhere, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_WriteOutsideUnrestricted_AsksWithoutAGrantKey()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.AskFirst);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_elsewhere, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
        decision.AllowOutside.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WriteOutsideUnrestrictedNeverAsk_AllowsOutside()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_elsewhere, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk)]
    [InlineData(AccessLevel.Unrestricted, ApprovalMode.NeverAsk)]
    public void Evaluate_ProtectedPath_DeniesAtEveryLevel(AccessLevel access, ApprovalMode approvals)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(access, approvals);
        _sut.Sensitive = new SensitivePaths([_elsewhere]);

        // Act
        var read = Evaluate(new ReadFileTool(), new { path = Path.Combine(_elsewhere, "note.txt") });
        var write = Evaluate(new WriteFileTool(), new { path = Path.Combine(_elsewhere, "note.txt"), content = "x" });

        // Assert
        read.Verdict.Should().Be(PermissionVerdict.Deny);
        write.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact(Skip = "Creating a symbolic link needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public void Evaluate_SymlinkIntoProtectedFolder_Denies()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "innocent"), _elsewhere);
        _sut.Sensitive = new SensitivePaths([_elsewhere]);

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = "innocent/note.txt" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact(Skip = "Creating a symbolic link needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public void Evaluate_SymlinkOutOfTheFolder_CountsAsOutside()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), _elsewhere);

        // Act
        var read = Evaluate(new ReadFileTool(), new { path = "link/note.txt" });
        var write = Evaluate(new WriteFileTool(), new { path = "link/new.txt", content = "x" });

        // Assert
        read.Verdict.Should().Be(PermissionVerdict.Ask);
        write.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_DotDotEscape_CountsAsOutside()
    {
        // Arrange
        var escape = "../" + Path.GetFileName(_elsewhere) + "/note.txt";

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = escape });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.WriteInFolder)]
    public void Evaluate_ShellWithoutSandbox_DeniesAndExplainsWhy(AccessLevel access)
    {
        // Arrange
        _sut.Sandbox = SandboxStatus.Unavailable("Intel Macs are not supported.");
        _sut.Settings = new PermissionSettings(access, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
        decision.Reason.Should().Contain("Intel Macs are not supported.").And.Contain("Unrestricted");
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, ShellMode.ReadOnly)]
    [InlineData(AccessLevel.WriteInFolder, ShellMode.Writable)]
    public void Evaluate_ShellWithSandbox_RunsConfinedToTheAccessLevel(AccessLevel access, ShellMode expected)
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Settings = new PermissionSettings(access, ApprovalMode.AutoApprove);

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.Shell.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_ShellWithSandboxAskFirst_Asks()
    {
        // Arrange
        _sut.Sandbox = Available;

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("shell:ls");
        decision.Shell.Should().Be(ShellMode.Writable);
    }

    [Theory]
    [InlineData(ApprovalMode.AskFirst, PermissionVerdict.Ask)]
    [InlineData(ApprovalMode.AutoApprove, PermissionVerdict.Allow)]
    [InlineData(ApprovalMode.NeverAsk, PermissionVerdict.Allow)]
    public void Evaluate_ShellUnrestricted_FollowsTheApprovalModeWithoutASandbox(ApprovalMode approvals, PermissionVerdict expected)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, approvals);

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.Verdict.Should().Be(expected);
        decision.Shell.Should().Be(ShellMode.Unconfined);
    }

    [Fact]
    public void Evaluate_ShellGrant_AllowsOnlyThatCommand()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Remember("shell:ls");

        // Act
        var same = Evaluate(NewShell(), new { command = "ls " });
        var other = Evaluate(NewShell(), new { command = "rm -rf x" });

        // Assert
        same.Verdict.Should().Be(PermissionVerdict.Allow);
        same.Shell.Should().Be(ShellMode.Writable);
        other.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Theory]
    [InlineData(ApprovalMode.AskFirst)]
    [InlineData(ApprovalMode.AutoApprove)]
    [InlineData(ApprovalMode.NeverAsk)]
    public void Evaluate_ForbiddenShellCommand_AsksUnderEveryApprovalMode(ApprovalMode approvals)
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, approvals);

        // Act
        var decision = Evaluate(NewShell(), new { command = "echo ok && rm -rf build" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.Reason.Should().Contain("every time");
        decision.GrantKey.Should().BeNull();
        decision.Shell.Should().Be(ShellMode.Writable);
    }

    [Fact]
    public void Evaluate_ForbiddenShellCommandWithGrantForSameText_StillAsks()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Remember("shell:rm -rf build");

        // Act
        var decision = Evaluate(NewShell(), new { command = "rm -rf build" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ForbiddenShellCommandUnrestricted_FollowsTheApprovalMode()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk);

        // Act
        var decision = Evaluate(NewShell(), new { command = "rm -rf build" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.Shell.Should().Be(ShellMode.Unconfined);
    }

    [Fact]
    public void Evaluate_ForbiddenShellCommandWithEmptyList_Runs()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Forbidden = ForbiddenCommands.None;
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);

        // Act
        var decision = Evaluate(NewShell(), new { command = "rm -rf build" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_MalformedArguments_DoesNotThrow()
    {
        // Act
        var decision = _sut.Evaluate(new ReadFileTool(), "{not json", _workspace.Context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    private static ShellTool NewShell() => new(
        ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false),
        new NoShellSandbox("none"),
        Path.GetTempPath());

    private PermissionDecision Evaluate(ITool tool, object arguments)
    {
        return _sut.Evaluate(tool, JsonSerializer.Serialize(arguments), _workspace.Context);
    }
}
