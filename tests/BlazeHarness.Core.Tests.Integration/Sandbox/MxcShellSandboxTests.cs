using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

public sealed class MxcShellSandboxTests : IDisposable
{
    private const string SkipReason = "The sandbox isn't available on this computer, or the test uses POSIX shell syntax.";

    /// <summary>
    /// The skip reason of the Windows tests.
    /// </summary>
    /// <remarks>
    /// The Windows tests disable parallelization: each starts a process container, which takes seconds, and a dozen starting at once on a
    /// small machine pushed some past their timeouts. Their commands use absolute paths: the Windows container starts every command in <c>C:\</c>.
    /// </remarks>
    private const string WindowsSkipReason = "The sandbox isn't available on this computer, or the test uses PowerShell syntax.";

    private readonly TempRoot _root = new();
    private readonly TempFolder _outsideFolder = new("bh-outside-");
    private readonly TempFolder _scratchFolder = new("bh-scratch-");
    private readonly FakeTimeProvider _time = new();

    public MxcShellSandboxTests()
    {
        File.WriteAllText(Path.Combine(_outsideFolder.Root, "secret.txt"), "top-secret");
    }

    public static bool IsAvailableOnPosix => SandboxSupport.IsAvailableOnPosix;

    public static bool IsAvailableOnWindows => SandboxSupport.IsAvailableOnWindows;

    public static bool IsAvailableOnWindowsWithSymlinks => SandboxSupport.IsAvailableOnWindows && SymlinkSupport.CanCreate;

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
    public async Task StartAsync_WorkingFolderThroughALink_UsesTheRealPath()
    {
        // Arrange
        var alias = Path.Combine(_outsideFolder.Root, "work");
        Directory.CreateSymbolicLink(alias, _root.Root);
        var shell = ShellInfo.DetectCurrent()!;
        var launch = new ShellLaunch(shell, "pwd", alias, ShellMode.Writable, _scratchFolder.Root, TimeSpan.FromSeconds(10));
        var sandbox = SandboxSupport.Shared;
        var ct = TestContext.Current.CancellationToken;

        // Act
        using var process = await sandbox.StartAsync(launch, ct);
        using var reader = new StreamReader(process.Output);
        var output = await reader.ReadToEndAsync(ct).Bounded();
        var exit = await process.WaitAsync(ct).Bounded();

        // Assert
        output.Trim().Should().Be(_root.Root);
        exit.Should().Be(0);
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_ReadOnly_CanReadButNotChangeTheWorkingFolder()
    {
        // Arrange
        _root.Write("keep.txt", "original");

        // Act
        var result = await RunAsync("cat keep.txt; echo changed > keep.txt; echo new > new.txt", ShellMode.ReadOnly);

        // Assert
        result.Output.Should().Contain("original");
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
        // Arrange
        var file = Path.Combine(RootedPath.CanonicalRoot(_outsideFolder.Root), "secret.txt");

        // Act
        var result = await RunAsync(
            $"cat '{file}'; echo changed > '{file}'",
            ShellMode.Writable,
            readFolders: [_outsideFolder.Root]);

        // Assert
        result.Output.Should().Contain("top-secret");
        result.IsError.Should().BeTrue();
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
        result.Output.Should().NotContain("key-material");
        _root.Read(".keys/token.txt").Should().Be("key-material");
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
        result.Output.Should().StartWith(Path.Combine(RootedPath.CanonicalRoot(_scratchFolder.Root), "home"));
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

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsWritable_CanChangeTheWorkingFolder()
    {
        // Arrange
        var made = Path.Combine(_root.Root, "made.txt");

        // Act
        var result = await RunAsync($"Set-Content '{made}' made; Get-Content '{made}'", ShellMode.Writable);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("made");
        _root.Read("made.txt").Trim().Should().Be("made");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindowsWithSymlinks), DisableParallelization = true)]
    public async Task Run_WindowsWorkingFolderThroughALink_GrantsTheRealFolder()
    {
        // Arrange
        var alias = Path.Combine(_outsideFolder.Root, "work");
        Directory.CreateSymbolicLink(alias, _root.Root);
        var made = Path.Combine(_root.Root, "made.txt");
        var shell = ShellInfo.DetectCurrent()!;
        var launch = new ShellLaunch(shell, $"Set-Content '{made}' made", alias, ShellMode.Writable, _scratchFolder.Root, TimeSpan.FromSeconds(30));
        var sandbox = SandboxSupport.Shared;
        var ct = TestContext.Current.CancellationToken;

        // Act
        using var process = await sandbox.StartAsync(launch, ct);
        using var reader = new StreamReader(process.Output);
        await reader.ReadToEndAsync(ct).Bounded();
        var exit = await process.WaitAsync(ct).Bounded();

        // Assert
        exit.Should().Be(0);
        _root.Read("made.txt").Trim().Should().Be("made");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsReadOnly_CanReadButNotChangeTheWorkingFolder()
    {
        // Arrange
        _root.Write("keep.txt", "original");
        var keep = Path.Combine(_root.Root, "keep.txt");
        var added = Path.Combine(_root.Root, "new.txt");

        // Act
        var result = await RunAsync($"Get-Content '{keep}'; Set-Content '{keep}' changed; Set-Content '{added}' new", ShellMode.ReadOnly);

        // Assert
        result.Output.Should().Contain("original");
        _root.Read("keep.txt").Should().Be("original");
        File.Exists(added).Should().BeFalse();
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsWritable_CannotReadAFolderOutsideTheAllowList()
    {
        // Arrange
        var secret = Path.Combine(_outsideFolder.Root, "secret.txt");
        var control = Path.Combine(_root.Root, "control.txt");

        // Act
        var result = await RunAsync($"Get-Content '{secret}'; Set-Content '{control}' ran", ShellMode.Writable);

        // Assert
        result.Output.Should().NotContain("top-secret");
        _root.Read("control.txt").Trim().Should().Be("ran");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsApprovedReadFolder_CanBeReadButNotChanged()
    {
        // Arrange
        var file = Path.Combine(RootedPath.CanonicalRoot(_outsideFolder.Root), "secret.txt");

        // Act
        var result = await RunAsync(
            $"Get-Content '{file}'; Set-Content '{file}' changed",
            ShellMode.Writable,
            readFolders: [_outsideFolder.Root]);

        // Assert
        result.Output.Should().Contain("top-secret");
        (await File.ReadAllTextAsync(Path.Combine(_outsideFolder.Root, "secret.txt"), TestContext.Current.CancellationToken)).Should().Be("top-secret");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsParentPathEscape_CannotReachFilesOutside()
    {
        // Arrange
        var relative = Path.GetRelativePath(_root.Root, Path.Combine(_outsideFolder.Root, "secret.txt"));
        var escape = Path.Combine(_root.Root, relative);
        var control = Path.Combine(_root.Root, "control.txt");

        // Act
        var result = await RunAsync($"Get-Content '{escape}'; Set-Content '{control}' ran", ShellMode.Writable);

        // Assert
        result.Output.Should().NotContain("top-secret");
        _root.Read("control.txt").Trim().Should().Be("ran");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindowsWithSymlinks), DisableParallelization = true)]
    public async Task Run_WindowsSymlinkToOutside_CannotReadOrWriteThroughIt()
    {
        // Arrange
        var target = Path.Combine(_outsideFolder.Root, "secret.txt");
        var link = Path.Combine(_root.Root, "link.txt");
        File.CreateSymbolicLink(link, target);
        var control = Path.Combine(_root.Root, "control.txt");

        // Act
        var result = await RunAsync($"Get-Content '{link}'; Set-Content '{link}' overwritten; Set-Content '{control}' ran", ShellMode.Writable);

        // Assert
        result.Output.Should().NotContain("top-secret");
        (await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)).Should().Be("top-secret");
        _root.Read("control.txt").Trim().Should().Be("ran");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsProtectedFolderInsideTheWorkingFolder_IsOutOfReach()
    {
        // Arrange
        _root.Write(".keys/token.txt", "key-material");
        var sensitive = new SensitivePaths([Path.Combine(_root.Root, ".keys")]);

        // Act
        var token = Path.Combine(_root.Root, ".keys", "token.txt");
        var other = Path.Combine(_root.Root, ".keys", "other.txt");
        var control = Path.Combine(_root.Root, "control.txt");
        var result = await RunAsync(
            $"Get-Content '{token}'; Set-Content '{other}' x; Set-Content '{control}' ran",
            ShellMode.Writable,
            new MxcShellSandbox(() => sensitive));

        // Assert
        // The Windows process container can't deny a folder inside a granted one, so the sandbox refuses the command; a backend that can runs it.
        result.Output.Should().NotContain("key-material");
        _root.Read(".keys/token.txt").Should().Be("key-material");
        File.Exists(other).Should().BeFalse();
        if (File.Exists(control))
        {
            _root.Read("control.txt").Trim().Should().Be("ran");
        }
        else
        {
            result.Output.Should().Contain("This command was not run");
        }
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsRefusedFile_IsOutOfReach()
    {
        // Arrange
        _root.Write(".env", "api-password");
        var refused = new RefusedPaths([new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/.env")], _root.Root);

        // Act
        var env = Path.Combine(_root.Root, ".env");
        var control = Path.Combine(_root.Root, "control.txt");
        var result = await RunAsync($"Get-Content '{env}'; Set-Content '{env}' changed; Set-Content '{control}' ran", ShellMode.Writable, refused: refused);

        // Assert
        // As with folders, the sandbox refuses the command where the backend can't deny the file.
        result.Output.Should().NotContain("password");
        _root.Read(".env").Should().Be("api-password");
        if (File.Exists(control))
        {
            _root.Read("control.txt").Trim().Should().Be("ran");
        }
        else
        {
            result.Output.Should().Contain("This command was not run");
        }
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsWritable_DoesNotPassSecretEnvironmentVariables()
    {
        // Arrange
        var host = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                host[key] = value;
            }
        }

        host["BH_TEST_TOKEN"] = "env-secret";
        var sandbox = new MxcShellSandbox(() => SensitivePaths.None, () => host);

        // Act
        var result = await RunAsync("\"[$env:BH_TEST_TOKEN]\"", ShellMode.Writable, sandbox);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("[]").And.NotContain("env-secret");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsWritable_UsesTheScratchFolderAsHome()
    {
        // Act
        var result = await RunAsync("$env:USERPROFILE; Set-Content \"$env:USERPROFILE\\file.txt\" data", ShellMode.Writable);

        // Assert
        result.Output.Should().StartWith(Path.Combine(RootedPath.CanonicalRoot(_scratchFolder.Root), "home"));
        File.Exists(Path.Combine(_scratchFolder.Root, "home", "file.txt")).Should().BeTrue();
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsUnconfined_CanReachFilesOutsideTheWorkingFolder()
    {
        // Act
        var result = await RunAsync($"Get-Content '{Path.Combine(_outsideFolder.Root, "secret.txt")}'", ShellMode.Unconfined);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("top-secret");
    }

    [Fact(Skip = WindowsSkipReason, SkipUnless = nameof(IsAvailableOnWindows), DisableParallelization = true)]
    public async Task Run_WindowsCommandThatOutlivesTheTimeout_IsStopped()
    {
        // Arrange
        var ready = Path.Combine(_root.Root, "ready.txt");

        // Act
        var run = RunAsync($"Set-Content '{ready}' ready; Start-Sleep -Seconds 30", ShellMode.Writable, timeoutSeconds: 1);
        await WaitForFileAsync(ready);
        var result = await _time.AdvanceUntilAsync(run, TimeSpan.FromSeconds(1));

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("ran longer than 1 seconds");
    }

    private static async Task WaitForFileAsync(string path)
    {
        // A Windows container can take over a minute to start while other tests start theirs.
        using var giveUp = new CancellationTokenSource(OperatingSystem.IsWindows() ? TestTimeouts.Default * 4 : TestTimeouts.Default);
        while (!File.Exists(path))
        {
            await Task.Delay(10, giveUp.Token);
        }
    }

    private async Task<ToolResult> RunAsync(string command, ShellMode mode, IShellSandbox? sandbox = null, int timeoutSeconds = 30, IReadOnlyList<string>? readFolders = null, RefusedPaths? refused = null)
    {
        var shell = ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false);
        using var background = new BackgroundShells(_time);
        var tool = new ShellTool(shell, sandbox ?? SandboxSupport.Shared, _scratchFolder.Root, background, _time);
        return await tool.ExecuteAsync(
            TempRoot.Args(new { command, timeout_seconds = timeoutSeconds }),
            _root.Context.WithShell(mode).WithShellReadFolders(readFolders ?? []).WithRefused(refused ?? RefusedPaths.None),
            TestContext.Current.CancellationToken);
    }
}
