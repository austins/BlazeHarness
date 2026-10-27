using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

public sealed class MxcShellSandboxTests : IDisposable
{
    private const string SkipReason = "The sandbox isn't available on this computer, or the test uses POSIX shell syntax.";

    private readonly TempWorkspace _workspace = new();
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "bh-outside-" + Guid.NewGuid().ToString("N"));
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "bh-scratch-" + Guid.NewGuid().ToString("N"));

    public MxcShellSandboxTests()
    {
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "top-secret");
    }

    public static bool IsAvailableOnPosix => SandboxSupport.IsAvailableOnPosix;

    public void Dispose()
    {
        _workspace.Dispose();
        Directory.Delete(_outside, recursive: true);
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_CanChangeTheWorkingFolder()
    {
        // Act
        var result = await RunAsync("echo made > made.txt; cat made.txt", ShellMode.Writable);

        // Assert
        result.Should().Be(ToolResult.Ok("made\n[exit code 0]"));
        _workspace.Read("made.txt").Should().Be("made\n");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ReadOnly_CanReadButNotChangeTheWorkingFolder()
    {
        // Arrange
        _workspace.Write("keep.txt", "original");

        // Act
        var result = await RunAsync("cat keep.txt; echo changed > keep.txt; echo new > new.txt", ShellMode.ReadOnly);

        // Assert
        result.Output.Should().Contain("original").And.Contain("Read-only file system");
        result.IsError.Should().BeTrue();
        _workspace.Read("keep.txt").Should().Be("original");
        File.Exists(Path.Combine(_workspace.Root, "new.txt")).Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_CannotReadAFolderOutsideTheAllowList()
    {
        // Act
        var result = await RunAsync($"cat '{Path.Combine(_outside, "secret.txt")}'", ShellMode.Writable);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ApprovedReadFolder_CanBeReadButNotChanged()
    {
        // Act
        var result = await RunAsync(
            $"cat '{Path.Combine(_outside, "secret.txt")}'; echo changed > '{Path.Combine(_outside, "secret.txt")}'",
            ShellMode.Writable,
            readFolders: [_outside]);

        // Assert
        result.Output.Should().Contain("top-secret").And.Contain("Read-only file system");
        (await File.ReadAllTextAsync(Path.Combine(_outside, "secret.txt"), TestContext.Current.CancellationToken)).Should().Be("top-secret");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ParentPathEscape_CannotReachFilesOutside()
    {
        // Arrange
        var relative = Path.GetRelativePath(_workspace.Root, Path.Combine(_outside, "secret.txt"));

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
        var target = Path.Combine(_outside, "secret.txt");
        File.CreateSymbolicLink(Path.Combine(_workspace.Root, "link.txt"), target);

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
        _workspace.Write(".keys/token.txt", "key-material");
        var sensitive = new SensitivePaths([Path.Combine(_workspace.Root, ".keys")]);

        // Act
        var result = await RunAsync("cat .keys/token.txt; echo x > .keys/other.txt", ShellMode.Writable, new MxcShellSandbox(sensitive));

        // Assert
        result.Output.Should().Contain("No such file").And.NotContain("key-material");
        File.Exists(Path.Combine(_workspace.Root, ".keys", "other.txt")).Should().BeFalse();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_DoesNotPassSecretEnvironmentVariables()
    {
        // Arrange
        var name = "BH_TEST_TOKEN_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "env-secret");

        try
        {
            // Act
            var result = await RunAsync($"echo \"[${name}]\"", ShellMode.Writable);

            // Assert
            result.Output.Should().Be("[]\n[exit code 0]");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_Writable_UsesTheScratchFolderAsHome()
    {
        // Act
        var result = await RunAsync("echo $HOME; echo data > $HOME/file.txt", ShellMode.Writable);

        // Assert
        result.Output.Should().StartWith(Path.Combine(Path.GetFullPath(_scratch), "home"));
        File.Exists(Path.Combine(_scratch, "home", "file.txt")).Should().BeTrue();
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
        var result = await RunAsync($"cat '{Path.Combine(_outside, "secret.txt")}'", ShellMode.Unconfined);

        // Assert
        result.Should().Be(ToolResult.Ok("top-secret\n[exit code 0]"));
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_CommandThatOutlivesTheTimeout_IsStopped()
    {
        // Act
        var result = await RunAsync("sleep 30", ShellMode.Writable, timeoutSeconds: 1);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("ran longer than 1 seconds");
    }

    [Fact]
    public async Task StartAsync_ConfinedWithoutASandbox_Throws()
    {
        // Arrange
        var sandbox = new NoShellSandbox("No sandbox here.");
        var launch = new ShellLaunch(new ShellInfo("sh", "/bin/sh", IsPowerShell: false), "true", _workspace.Root, ShellMode.Writable, _scratch, TimeSpan.FromSeconds(5));

        // Act
        var act = () => sandbox.StartAsync(launch, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("No sandbox here.");
    }

    private async Task<ToolResult> RunAsync(string command, ShellMode mode, IShellSandbox? sandbox = null, int timeoutSeconds = 30, IReadOnlyList<string>? readFolders = null)
    {
        var shell = ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false);
        var tool = new ShellTool(shell, sandbox ?? new MxcShellSandbox(SensitivePaths.None), _scratch);
        return await tool.ExecuteAsync(
            TempWorkspace.Args(new { command, timeout_seconds = timeoutSeconds }),
            _workspace.Context.WithShell(mode).WithShellReadFolders(readFolders ?? []),
            TestContext.Current.CancellationToken);
    }
}
