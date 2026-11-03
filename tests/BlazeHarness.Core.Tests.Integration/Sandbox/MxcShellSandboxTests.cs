using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

public sealed class MxcShellSandboxTests : IDisposable
{
    private const string SkipReason = "The sandbox isn't available on this computer, or the test uses POSIX shell syntax.";

    private readonly TempRoot _root = new();
    private readonly TempFolder _outsideFolder = new("bh-outside-");
    private readonly TempFolder _scratchFolder = new("bh-scratch-");
    private readonly FakeTimeProvider _time = new();

    public MxcShellSandboxTests()
    {
        File.WriteAllText(Path.Combine(_outsideFolder.Root, "secret.txt"), "top-secret");
    }

    public static bool IsAvailableOnPosix => SandboxSupport.IsAvailableOnPosix;

    public void Dispose()
    {
        _root.Dispose();
        _outsideFolder.Dispose();
        _scratchFolder.Dispose();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_CanChangeTheWorkingFolder()
    {
        // Act
        var result = await RunAsync("echo made > made.txt; cat made.txt", ShellMode.Writable);

        // Assert
        result.Should().Be(ToolResult.Ok("made\n[exit code 0]"));
        _root.Read("made.txt").Should().Be("made\n");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ReadOnly_CanReadButNotChangeTheWorkingFolder()
    {
        // Arrange
        _root.Write("keep.txt", "original");

        // Act
        var result = await RunAsync("cat keep.txt; echo changed > keep.txt; echo new > new.txt", ShellMode.ReadOnly);

        // Assert
        result.Output.Should().Contain("original").And.Contain("Read-only file system");
        result.IsError.Should().BeTrue();
        _root.Read("keep.txt").Should().Be("original");
        File.Exists(Path.Combine(_root.Root, "new.txt")).Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_CannotReadAFolderOutsideTheAllowList()
    {
        // Act
        var result = await RunAsync($"cat '{Path.Combine(_outsideFolder.Root, "secret.txt")}'", ShellMode.Writable);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ApprovedReadFolder_CanBeReadButNotChanged()
    {
        // Act
        var result = await RunAsync(
            $"cat '{Path.Combine(_outsideFolder.Root, "secret.txt")}'; echo changed > '{Path.Combine(_outsideFolder.Root, "secret.txt")}'",
            ShellMode.Writable,
            readFolders: [_outsideFolder.Root]);

        // Assert
        result.Output.Should().Contain("top-secret").And.Contain("Read-only file system");
        (await File.ReadAllTextAsync(Path.Combine(_outsideFolder.Root, "secret.txt"), TestContext.Current.CancellationToken)).Should().Be("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ParentPathEscape_CannotReachFilesOutside()
    {
        // Arrange
        var relative = Path.GetRelativePath(_root.Root, Path.Combine(_outsideFolder.Root, "secret.txt"));

        // Act
        var result = await RunAsync($"cat '{relative}'", ShellMode.Writable);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_SymlinkToOutside_CannotReadOrWriteThroughIt()
    {
        // Arrange
        var target = Path.Combine(_outsideFolder.Root, "secret.txt");
        File.CreateSymbolicLink(Path.Combine(_root.Root, "link.txt"), target);

        // Act
        var result = await RunAsync("cat link.txt; echo overwritten > link.txt", ShellMode.Writable);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("top-secret");
        (await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)).Should().Be("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ProtectedFolderInsideTheWorkingFolder_IsOutOfReach()
    {
        // Arrange
        _root.Write(".keys/token.txt", "key-material");
        var sensitive = new SensitivePaths([Path.Combine(_root.Root, ".keys")]);

        // Act
        var result = await RunAsync("cat .keys/token.txt; echo x > .keys/other.txt", ShellMode.Writable, new MxcShellSandbox(() => sensitive));

        // Assert
        result.Output.Should().Contain("No such file").And.NotContain("key-material");
        File.Exists(Path.Combine(_root.Root, ".keys", "other.txt")).Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_RefusedFiles_AreOutOfReach()
    {
        // Arrange
        _root.Write(".env", "api-password");
        _root.Write("api/.env", "nested-password");
        _root.Write("readme.txt", "hello");
        var refused = new RefusedPaths([new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/.env")], _root.Root);

        // Act
        var result = await RunAsync("cat .env api/.env readme.txt; echo changed > .env; echo changed > api/.env", ShellMode.Writable, refused: refused);

        // Assert
        result.Output.Should().Contain("hello").And.NotContain("password");
        _root.Read(".env").Should().Be("api-password");
        _root.Read("api/.env").Should().Be("nested-password");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_RefusedFilesAndNoRefusingRule_LeavesTheFileReadable()
    {
        // Arrange
        _root.Write(".env", "api-password");
        var onlyAsks = new RefusedPaths([new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Ask, "**/.env")], _root.Root);

        // Act
        var result = await RunAsync("cat .env", ShellMode.Writable, refused: onlyAsks);

        // Assert
        result.Output.Should().Contain("api-password");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_DoesNotPassSecretEnvironmentVariables()
    {
        // Arrange
        var host = new Dictionary<string, string>
        {
            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin",
            ["BH_TEST_TOKEN"] = "env-secret",
        };
        var sandbox = new MxcShellSandbox(() => SensitivePaths.None, () => host);

        // Act
        var result = await RunAsync("echo \"[$BH_TEST_TOKEN]\"", ShellMode.Writable, sandbox);

        // Assert
        result.Output.Should().Be("[]\n[exit code 0]");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_UsesTheScratchFolderAsHome()
    {
        // Act
        var result = await RunAsync("echo $HOME; echo data > $HOME/file.txt", ShellMode.Writable);

        // Assert
        result.Output.Should().StartWith(Path.Combine(Path.GetFullPath(_scratchFolder.Root), "home"));
        File.Exists(Path.Combine(_scratchFolder.Root, "home", "file.txt")).Should().BeTrue();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_CommandWithQuotesAndNewlines_RunsAsWritten()
    {
        // Act
        var result = await RunAsync("echo \"it's \\\"quoted\\\"\"\necho 'second line'", ShellMode.Writable);

        // Assert
        result.Should().Be(ToolResult.Ok("it's \"quoted\"\nsecond line\n[exit code 0]"));
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Unconfined_CanReachFilesOutsideTheWorkingFolder()
    {
        // Act
        var result = await RunAsync($"cat '{Path.Combine(_outsideFolder.Root, "secret.txt")}'", ShellMode.Unconfined);

        // Assert
        result.Should().Be(ToolResult.Ok("top-secret\n[exit code 0]"));
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_CommandThatOutlivesTheTimeout_IsStopped()
    {
        // Arrange
        var ready = Path.Combine(_root.Root, "ready.txt");

        // Act
        var run = RunAsync($"echo ready > '{ready}'; sleep 30", ShellMode.Writable, timeoutSeconds: 1);
        await WaitForFileAsync(ready);
        var result = await _time.AdvanceUntilAsync(run, TimeSpan.FromSeconds(1));

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("ran longer than 1 seconds");
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var giveUp = new CancellationTokenSource(TestTimeouts.Default);
        while (!File.Exists(path))
        {
            await Task.Delay(10, giveUp.Token);
        }
    }

    private async Task<ToolResult> RunAsync(string command, ShellMode mode, IShellSandbox? sandbox = null, int timeoutSeconds = 30, IReadOnlyList<string>? readFolders = null, RefusedPaths? refused = null)
    {
        var shell = ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false);
        using var background = new BackgroundShells(_time);
        var tool = new ShellTool(shell, sandbox ?? new MxcShellSandbox(() => SensitivePaths.None), _scratchFolder.Root, background, _time);
        return await tool.ExecuteAsync(
            TempRoot.Args(new { command, timeout_seconds = timeoutSeconds }),
            _root.Context.WithShell(mode).WithShellReadFolders(readFolders ?? []).WithRefused(refused ?? RefusedPaths.None),
            TestContext.Current.CancellationToken);
    }
}
