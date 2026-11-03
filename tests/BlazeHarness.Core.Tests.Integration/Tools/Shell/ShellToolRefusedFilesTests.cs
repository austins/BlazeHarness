using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Shell;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Tools.Shell;

public sealed class ShellToolRefusedFilesTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly IShellSandbox _sandbox = Substitute.For<IShellSandbox>();
    private readonly BackgroundShells _background = new(TimeProvider.System);
    private readonly ShellTool _sut;
    private ShellLaunch? _launch;

    public ShellToolRefusedFilesTests()
    {
        _sandbox.StartAsync(Arg.Any<ShellLaunch>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _launch = call.Arg<ShellLaunch>();
            var process = Substitute.For<IShellProcess>();
            process.Output.Returns(Stream.Null);
            process.Error.Returns(Stream.Null);
            process.WaitAsync(Arg.Any<CancellationToken>()).Returns(0);
            return Task.FromResult(process);
        });
        _sut = new ShellTool(new ShellInfo("sh", "/bin/sh", IsPowerShell: false), _sandbox, _root.Root, _background, new FakeTimeProvider());
    }

    public void Dispose()
    {
        _background.Dispose();
        _root.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_SandboxedWithARefusingRule_StartsTheCommandWithTheRefusedFiles()
    {
        // Arrange
        var env = _root.Write("api/.env", "x");
        _root.Write("readme.txt", "x");

        // Act
        await RunAsync(ShellMode.Writable, RefuseEnv());

        // Assert
        _launch!.DeniedPaths.Should().ContainSingle().Which.Should().Be(env);
    }

    [Fact]
    public async Task ExecuteAsync_Unconfined_StartsTheCommandWithoutLookingForRefusedFiles()
    {
        // Arrange
        _root.Write(".env", "x");

        // Act
        await RunAsync(ShellMode.Unconfined, RefuseEnv());

        // Assert
        _launch!.DeniedPaths.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoRefusingRule_StartsTheCommandWithoutLookingForRefusedFiles()
    {
        // Arrange
        _root.Write(".env", "x");

        // Act
        await RunAsync(ShellMode.Writable, RefusedPaths.None);

        // Assert
        _launch!.DeniedPaths.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_TooManyRefusedFiles_ReturnsAnErrorNamingTheRuleWithoutStartingTheCommand()
    {
        // Arrange
        for (var i = 0; i <= RefusedFiles.MaxPaths; i++)
        {
            _root.Write($"keys/{i}.env", "x");
        }

        var refused = new RefusedPaths([new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/*.env")], _root.Root);

        // Act
        var result = await RunAsync(ShellMode.Writable, refused);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("**/*.env");
        _launch.Should().BeNull();
    }

    private RefusedPaths RefuseEnv() => new([new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/.env")], _root.Root);

    private async Task<ToolResult> RunAsync(ShellMode mode, RefusedPaths refused)
    {
        return await _sut.ExecuteAsync(TempRoot.Args(new { command = "true" }), _root.Context.WithShell(mode).WithRefused(refused), TestContext.Current.CancellationToken);
    }
}
