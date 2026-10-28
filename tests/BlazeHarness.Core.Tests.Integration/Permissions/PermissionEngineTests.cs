using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Permissions;

public sealed class PermissionEngineTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly TempFolder _temp = new("bh-else-");
    private readonly PermissionEngine _sut = new();

    public PermissionEngineTests()
    {
        Directory.CreateDirectory(_temp.Root);
        File.WriteAllText(Path.Combine(_temp.Root, "note.txt"), "hi");
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static SandboxStatus Available { get; } = new(true, "test", "A test sandbox.");

    public void Dispose()
    {
        _workspace.Dispose();
        _temp.Dispose();
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
    public void Evaluate_ReadFileOutsideTheFolder_AsksAndKeysByThatFile()
    {
        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.AllowOutside.Should().BeTrue();
        decision.GrantKey.Should().Be("file:" + Path.Combine(WorkspacePath.CanonicalRoot(_temp.Root), "note.txt"));
    }

    [Fact]
    public void Evaluate_ReadFileOutsideAfterGrant_AllowsThatFileOnly()
    {
        // Arrange
        var first = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });
        _sut.Remember(first.GrantKey!);

        // Act
        var again = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });
        var sibling = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "other.txt") });

        // Assert
        again.Should().Be(PermissionDecision.Allow with { AllowOutside = true });
        sibling.Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public void Evaluate_ReadFolderOutside_AsksAndKeysByFolder()
    {
        // Act
        var decision = Evaluate(new ListFilesTool(), new { path = _temp.Root });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("dir:" + WorkspacePath.CanonicalRoot(_temp.Root));
    }

    [Fact]
    public void Evaluate_ReadFolderOutsideAfterGrant_AllowsThatFolderOnly()
    {
        // Arrange
        var first = Evaluate(new ListFilesTool(), new { path = _temp.Root });
        _sut.Remember(first.GrantKey!);

        // Act
        var inside = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "other.txt") });
        var elsewhere = Evaluate(new ListFilesTool(), new { path = Path.GetTempPath() });

        // Assert
        inside.Should().Be(PermissionDecision.Allow with { AllowOutside = true });
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
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });

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
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_WriteOutsideUnrestricted_AsksWithoutAGrantKey()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.AskFirst);

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "new.txt"), content = "x" });

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
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ReadInAReadableFolder_AllowsWithoutAsking()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AskFirst);
        _sut.ReadableFolders = () => [_temp.Root];

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WriteInAReadableFolder_StillDenied()
    {
        // Arrange
        _sut.ReadableFolders = () => [_temp.Root];

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "note.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_ShellWithAReadableFolder_CarriesIt()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.ReadableFolders = () => [_temp.Root];

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.ShellReadFolders.Should().ContainSingle().Which.Should().Be(WorkspacePath.CanonicalRoot(_temp.Root));
    }

    [Theory]
    [InlineData(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk)]
    [InlineData(AccessLevel.Unrestricted, ApprovalMode.NeverAsk)]
    public void Evaluate_ProtectedPath_DeniesAtEveryLevel(AccessLevel access, ApprovalMode approvals)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(access, approvals);
        _sut.Sensitive = () => new SensitivePaths([_temp.Root]);

        // Act
        var read = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });
        var write = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "note.txt"), content = "x" });

        // Assert
        read.Verdict.Should().Be(PermissionVerdict.Deny);
        write.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact(Skip = "Creating a symbolic link needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public void Evaluate_SymlinkIntoProtectedFolder_Denies()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "innocent"), _temp.Root);
        _sut.Sensitive = () => new SensitivePaths([_temp.Root]);

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = "innocent/note.txt" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact(Skip = "Creating a symbolic link needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public void Evaluate_SymlinkOutOfTheFolder_CountsAsOutside()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_workspace.Root, "link"), _temp.Root);

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
        var escape = "../" + Path.GetFileName(_temp.Root) + "/note.txt";

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
    public void Settings_BypassNotAllowed_ReadsAsTheSaferOptions()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk);
        _sut.BypassAllowed = () => false;

        // Act
        var settings = _sut.Settings;

        // Assert
        settings.Should().Be(new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove));
    }

    [Fact]
    public void Evaluate_UnrestrictedWhileBypassNotAllowed_StillGuardsPathsOutsideTheFolder()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk);
        _sut.BypassAllowed = () => false;

        // Act
        var decision = Evaluate(new WriteFileTool(), new { path = Path.Combine(_temp.Root, "new.txt"), content = "x" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_ShellAfterAnApprovedFolderRead_CarriesTheApprovedFolder()
    {
        // Arrange
        _sut.Sandbox = Available;
        var read = Evaluate(new ListFilesTool(), new { path = _temp.Root });
        _sut.Remember(read.GrantKey!);

        // Act
        var decision = Evaluate(NewShell(), new { command = "cat note.txt" });

        // Assert
        decision.ShellReadFolders.Should().ContainSingle().Which.Should().Be(WorkspacePath.CanonicalRoot(_temp.Root));
    }

    [Fact]
    public void Evaluate_ShellAfterAnApprovedFileRead_CarriesNoFolder()
    {
        // Arrange
        _sut.Sandbox = Available;
        var read = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });
        _sut.Remember(read.GrantKey!);

        // Act
        var decision = Evaluate(NewShell(), new { command = "cat note.txt" });

        // Assert
        decision.ShellReadFolders.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShellWithoutApprovedReads_CarriesNoFolders()
    {
        // Arrange
        _sut.Sandbox = Available;

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.ShellReadFolders.Should().BeEmpty();
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
    public void Evaluate_ForbiddenShellCommandWithAllowRule_StillAsks()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Rules = () => [new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Allow, "rm *")];

        // Act
        var decision = Evaluate(NewShell(), new { command = "rm -rf build" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
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
        // The tool parses the same text with the same parser and refuses it, so allowing here cannot run anything.
        // Act
        var decision = _sut.Evaluate(new ReadFileTool(), "{not json", _workspace.Context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_RefuseRuleOnACommand_DeniesEvenWhenUnrestricted()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk);
        _sut.Rules = () => [new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Deny, "git push*")];

        // Act
        var decision = Evaluate(NewShell(), new { command = "git push origin main" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_AskRuleOnACommand_AsksUnderAutoApproveAndCannotBeRemembered()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);
        _sut.Rules = () => [new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Ask, "git push*")];

        // Act
        var decision = Evaluate(NewShell(), new { command = "git push" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
    }

    [Fact]
    public void Evaluate_AllowRuleOnACommand_SkipsTheQuestionButStaysSandboxed()
    {
        // Arrange
        _sut.Sandbox = Available;
        _sut.Rules = () => [new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Allow, "git status*")];

        // Act
        var decision = Evaluate(NewShell(), new { command = "git status" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.Shell.Should().Be(ShellMode.Writable);
    }

    [Fact]
    public void Evaluate_AllowRuleWithoutASandbox_StillDenies()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Allow, "*")];

        // Act
        var decision = Evaluate(NewShell(), new { command = "ls" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_AllowRuleOnAProtectedPath_StillDenies()
    {
        // Arrange
        _sut.Sensitive = () => new SensitivePaths([_temp.Root]);
        _sut.Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Allow, "**")];

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_AllowRuleOnAFileOutsideTheFolder_SkipsTheQuestionToRead()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Allow, _temp.Root.Replace('\\', '/') + "/**")];

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = Path.Combine(_temp.Root, "note.txt") });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
        decision.AllowOutside.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RefuseRuleOnAFilePath_DeniesAReadInsideTheFolder()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "a.txt")];

        // Act
        var decision = Evaluate(new ReadFileTool(), new { path = "a.txt" });

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
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
