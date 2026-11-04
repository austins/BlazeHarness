using BlazeHarness.Core.Git;
using BlazeHarness.Core.Tools.Shell;

namespace BlazeHarness.Core.Tests.Integration.Git;

internal static class TestGit
{
    /// <summary>Gets a value indicating whether git is installed; the skip condition for repository tests.</summary>
    public static bool IsAvailable { get; } = ShellInfo.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git", Environment.GetEnvironmentVariable("PATH")) is not null;

    /// <summary>Run git in a folder and fail the test when it does not succeed.</summary>
    /// <param name="folder">The working directory.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>A task that completes when git has exited.</returns>
    public static async Task RunAsync(string folder, params string[] arguments)
    {
        var result = await new GitProcessRunner(TimeProvider.System).RunAsync(
            folder,
            ["-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. arguments],
            TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue("git {0} should succeed", string.Join(' ', arguments));
    }

    /// <summary>Create a repository with one committed file.</summary>
    /// <param name="folder">The folder to initialize.</param>
    /// <returns>A task that completes when the commit exists.</returns>
    public static async Task InitAsync(string folder)
    {
        await RunAsync(folder, "init", "-q");
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "one\ntwo\nthree\n", TestContext.Current.CancellationToken);
        await RunAsync(folder, "add", "-A");
        await RunAsync(folder, "commit", "-q", "-m", "first");
    }
}
