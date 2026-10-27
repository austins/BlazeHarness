using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

public sealed class SandboxPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bh-policy-" + Guid.NewGuid().ToString("N"));
    private readonly string _work;
    private readonly string _scratch;
    private readonly string _toolBin;

    public SandboxPolicyTests()
    {
        _work = Path.Combine(_root, "work");
        _scratch = Path.Combine(_root, "scratch");
        _toolBin = Path.Combine(_root, "tools", "bin");
        Directory.CreateDirectory(_work);
        Directory.CreateDirectory(_scratch);
        Directory.CreateDirectory(_toolBin);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Create_Writable_GivesTheWorkingFolderAndScratchWriteAccess()
    {
        // Act
        var policy = Create(ShellMode.Writable);

        // Assert
        policy.ReadWrite.Should().HaveCount(2).And.Contain(Canonical(_scratch)).And.Contain(Canonical(_work));
        policy.ReadOnly.Should().NotContain(Canonical(_work));
    }

    [Fact]
    public void Create_ReadOnly_OnlyLetsTheCommandReadTheWorkingFolder()
    {
        // Act
        var policy = Create(ShellMode.ReadOnly);

        // Assert
        policy.ReadWrite.Should().ContainSingle().Which.Should().Be(Canonical(_scratch));
        policy.ReadOnly.Should().Contain(Canonical(_work));
    }

    [Fact]
    public void Create_FoldersOnThePath_AreReadOnly()
    {
        // Act
        var policy = Create(ShellMode.Writable, host: new Dictionary<string, string> { ["PATH"] = _toolBin });

        // Assert
        policy.ReadOnly.Should().Contain(Canonical(_toolBin));
    }

    [Fact]
    public void Create_RelativeFolderOnThePath_IsNotMounted()
    {
        // Arrange
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, _toolBin);

        // Act
        var policy = Create(ShellMode.Writable, host: new Dictionary<string, string> { ["PATH"] = relative });

        // Assert
        policy.ReadOnly.Should().NotContain(Canonical(_toolBin));
    }

    [Fact]
    public void Create_HomeFolderOrItsParentOnThePath_IsNotMounted()
    {
        // Arrange
        var home = Directory.CreateDirectory(Path.Combine(_root, "users", "me")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(home, ".local", "bin")).FullName;
        var path = string.Join(Path.PathSeparator, home, Path.GetDirectoryName(home), bin);

        // Act
        var policy = Create(ShellMode.Writable, host: new Dictionary<string, string> { ["PATH"] = path, ["HOME"] = home });

        // Assert
        policy.ReadOnly.Should().Contain(Canonical(bin)).And.NotContain(Canonical(home)).And.NotContain(Canonical(Path.GetDirectoryName(home)!));
    }

    [Fact]
    public void Create_ApprovedReadFolders_AreReadOnly()
    {
        // Arrange
        var approved = Directory.CreateDirectory(Path.Combine(_root, "approved")).FullName;

        // Act
        var policy = Create(ShellMode.Writable, readFolders: [approved, Path.Combine(_root, "missing")]);

        // Assert
        policy.ReadOnly.Should().Contain(Canonical(approved)).And.NotContain(Path.Combine(_root, "missing"));
        policy.ReadWrite.Should().NotContain(Canonical(approved));
    }

    [Fact]
    public void Create_ProtectedFolderInsideAnApprovedReadFolder_IsDenied()
    {
        // Arrange
        var approved = Directory.CreateDirectory(Path.Combine(_root, "approved")).FullName;
        var keys = Directory.CreateDirectory(Path.Combine(approved, ".keys")).FullName;

        // Act
        var policy = Create(ShellMode.Writable, protectedFolders: [keys], readFolders: [approved]);

        // Assert
        policy.Denied.Should().ContainSingle().Which.Should().Be(Canonical(keys));
    }

    [Fact]
    public void Create_Environment_KeepsOnlyListedVariablesAndRedirectsHome()
    {
        // Arrange
        var host = new Dictionary<string, string>
        {
            ["PATH"] = _toolBin,
            ["LANG"] = "en_US.UTF-8",
            ["OPENAI_API_KEY"] = "sk-secret",
            ["GITHUB_TOKEN"] = "ghp-secret",
            ["HOME"] = "/home/someone",
        };

        // Act
        var policy = Create(ShellMode.Writable, host: host, command: "echo hi");

        // Assert
        policy.Environment.Should().NotContainKey("OPENAI_API_KEY").And.NotContainKey("GITHUB_TOKEN");
        policy.Environment["PATH"].Should().Be(_toolBin);
        policy.Environment["LANG"].Should().Be("en_US.UTF-8");
        policy.Environment["HOME"].Should().Be(Path.Combine(Canonical(_scratch), "home"));
        policy.Environment["TMPDIR"].Should().Be(Path.Combine(Canonical(_scratch), "tmp"));
        policy.Environment[SandboxPolicy.CommandVariable].Should().Be("echo hi");
    }

    [Fact]
    public void Create_WindowsEnvironment_KeepsSystemVariablesAndRedirectsProfile()
    {
        // Arrange
        var host = new Dictionary<string, string> { ["SystemRoot"] = @"C:\Windows", ["USERPROFILE"] = @"C:\Users\someone", ["AWS_SECRET_ACCESS_KEY"] = "x" };

        // Act
        var policy = Create(ShellMode.Writable, SandboxPlatform.Windows, host);

        // Assert
        policy.Environment["SystemRoot"].Should().Be(@"C:\Windows");
        policy.Environment["USERPROFILE"].Should().Be(Path.Combine(Canonical(_scratch), "home"));
        policy.Environment.Should().NotContainKey("AWS_SECRET_ACCESS_KEY");
    }

    [Fact]
    public void Create_ProtectedFolderInsideTheWorkingFolder_IsDenied()
    {
        // Arrange
        var inside = Directory.CreateDirectory(Path.Combine(_work, ".keys")).FullName;
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "other-keys")).FullName;

        // Act
        var policy = Create(ShellMode.Writable, protectedFolders: [inside, elsewhere, Path.Combine(_work, "missing")]);

        // Assert
        policy.Denied.Should().ContainSingle().Which.Should().Be(Canonical(inside));
    }

    [Fact]
    public void Create_PosixCommandLine_QuotesTheShellPathAndReadsTheCommandFromTheEnvironment()
    {
        // Act
        var policy = Create(ShellMode.Writable, shell: new ShellInfo("bash", "/opt/it's/bash", IsPowerShell: false));

        // Assert
        policy.CommandLine.Should().Be("'/opt/it'\\''s/bash' -c 'eval \"$BLAZEHARNESS_COMMAND\"'");
    }

    [Fact]
    public void Create_WindowsCommandLine_RunsPowerShellOnTheCommandFromTheEnvironment()
    {
        // Act
        var policy = Create(ShellMode.Writable, SandboxPlatform.Windows, shell: new ShellInfo("pwsh", @"C:\Program Files\PowerShell\7\pwsh.exe", IsPowerShell: true));

        // Assert
        policy.CommandLine.Should().Be("\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo -NoProfile -NonInteractive -Command Invoke-Expression $env:BLAZEHARNESS_COMMAND");
    }

    private static string Canonical(string path) => WorkspacePath.CanonicalRoot(path);

    private SandboxPolicy Create(
        ShellMode mode,
        SandboxPlatform platform = SandboxPlatform.Linux,
        IReadOnlyDictionary<string, string>? host = null,
        IEnumerable<string>? protectedFolders = null,
        string command = "true",
        ShellInfo? shell = null,
        IReadOnlyList<string>? readFolders = null)
    {
        var launch = new ShellLaunch(shell ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false), command, _work, mode, _scratch, TimeSpan.FromSeconds(10), readFolders);
        return SandboxPolicy.Create(launch, platform, host ?? new Dictionary<string, string>(), protectedFolders ?? []);
    }
}
