using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class ShellInfoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bh-shell-" + Guid.NewGuid().ToString("N"));

    public ShellInfoTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Detect_WindowsWithPwshAndPowerShell_PrefersPwsh()
    {
        // Arrange
        var pwsh = Touch("pwsh.exe");
        Touch("powershell.exe");

        // Act
        var shell = ShellInfo.Detect(isWindows: true, shellVariable: null, _folder);

        // Assert
        shell.Should().Be(new ShellInfo("pwsh", pwsh, IsPowerShell: true));
    }

    [Fact]
    public void Detect_WindowsWithOnlyPowerShell_FallsBackToIt()
    {
        // Arrange
        var legacy = Touch("powershell.exe");

        // Act
        var shell = ShellInfo.Detect(isWindows: true, shellVariable: null, _folder);

        // Assert
        shell.Should().Be(new ShellInfo("powershell", legacy, IsPowerShell: true));
    }

    [Fact]
    public void Detect_WindowsWithNoPowerShell_ReturnsNull()
    {
        // Act
        var shell = ShellInfo.Detect(isWindows: true, shellVariable: null, _folder);

        // Assert
        shell.Should().BeNull();
    }

    [Fact]
    public void Detect_PosixWithAKnownShellVariable_UsesIt()
    {
        // Arrange
        var zsh = Touch("zsh");

        // Act
        var shell = ShellInfo.Detect(isWindows: false, zsh, pathVariable: null);

        // Assert
        shell.Should().Be(new ShellInfo("zsh", zsh, IsPowerShell: false));
    }

    [Fact]
    public void Detect_PosixWithAnUnsupportedShellVariable_FallsBackToABourneShell()
    {
        // Arrange
        var fish = Touch("fish");

        // Act
        var shell = ShellInfo.Detect(isWindows: false, fish, pathVariable: null);

        // Assert
        shell!.Name.Should().BeOneOf("bash", "sh");
        shell.IsPowerShell.Should().BeFalse();
    }

    [Fact]
    public void ArgumentsFor_PosixShell_RunsOneCommandString()
    {
        // Act
        var arguments = new ShellInfo("bash", "/bin/bash", IsPowerShell: false).ArgumentsFor("ls -la");

        // Assert
        arguments.Should().Equal("-c", "ls -la");
    }

    [Fact]
    public void ArgumentsFor_PowerShell_SkipsTheProfileAndPrompts()
    {
        // Act
        var arguments = new ShellInfo("pwsh", "pwsh", IsPowerShell: true).ArgumentsFor("Get-ChildItem");

        // Assert
        arguments.Should().Equal("-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Get-ChildItem");
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
