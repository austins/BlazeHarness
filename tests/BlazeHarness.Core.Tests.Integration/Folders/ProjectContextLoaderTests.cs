using BlazeHarness.Core.Folders;

namespace BlazeHarness.Core.Tests.Integration.Folders;

public sealed class ProjectContextLoaderTests : IDisposable
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bh-agents-" + Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_NoGitRepoAndFileInFolder_LoadsIt()
    {
        // Arrange
        var folder = Dir("app");
        Write(folder, "AGENTS.md", "rules");

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().ContainSingle().Which.Should().Be(new ProjectFile(Path.Combine(folder, "AGENTS.md"), "rules", false));
    }

    [Fact]
    public async Task LoadAsync_NoGitRepoAndFileOnlyInParent_LoadsNothing()
    {
        // Arrange
        Write(Dir("parent"), "AGENTS.md", "parent rules");
        var folder = Dir("parent/app");

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_InsideGitRepo_LoadsFromRepoRootDownToTheFolderWithTheNearestLast()
    {
        // Arrange
        var repo = Dir("repo");
        Dir("repo/.git");
        Write(repo, "AGENTS.md", "root rules");
        Write(Dir("repo/packages"), "AGENTS.md", "packages rules");
        var folder = Dir("repo/packages/web");
        Write(folder, "AGENTS.md", "web rules");

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Select(f => f.Content).Should().Equal("root rules", "packages rules", "web rules");
    }

    [Fact]
    public async Task LoadAsync_NestedRepoWithAGitFile_StopsAtTheNearestRoot()
    {
        // Arrange
        Dir("outer/.git");
        Write(Dir("outer"), "AGENTS.md", "outer rules");
        var inner = Dir("outer/vendor/lib");
        Write(inner, ".git", "gitdir: ../../.git/modules/lib");
        Write(inner, "AGENTS.md", "lib rules");

        // Act
        var context = await ProjectContextLoader.LoadAsync(inner, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Select(f => f.Content).Should().Equal("lib rules");
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_IsSkipped()
    {
        // Arrange
        var folder = Dir("app");
        Write(folder, "AGENTS.md", "  \n");

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_FileLargerThanTheCap_IsCutOff()
    {
        // Arrange
        var folder = Dir("app");
        Write(folder, "AGENTS.md", new string('a', ProjectContextLoader.MaxBytes + 100));

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        var file = context.Files.Should().ContainSingle().Subject;
        file.Truncated.Should().BeTrue();
        file.Content.Length.Should().Be(ProjectContextLoader.MaxBytes);
    }

    [Fact]
    public async Task LoadAsync_NearestFileUsesTheWholeBudget_OuterFileIsListedWithoutText()
    {
        // Arrange
        Dir("repo/.git");
        Write(Dir("repo"), "AGENTS.md", "outer");
        var folder = Dir("repo/app");
        Write(folder, "AGENTS.md", new string('a', ProjectContextLoader.MaxBytes));

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().HaveCount(2);
        context.Files[0].Should().Be(new ProjectFile(Path.Combine(_root, "repo", "AGENTS.md"), string.Empty, true));
        context.Files[1].Truncated.Should().BeFalse();
    }

    [Fact(Skip = "Symbolic links need privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task LoadAsync_LinkToAFileInsideTheRepo_IsFollowed()
    {
        // Arrange
        var repo = Dir("repo");
        Dir("repo/.git");
        Write(repo, "CLAUDE.md", "shared rules");
        File.CreateSymbolicLink(Path.Combine(repo, "AGENTS.md"), Path.Combine(repo, "CLAUDE.md"));

        // Act
        var context = await ProjectContextLoader.LoadAsync(repo, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().ContainSingle().Which.Content.Should().Be("shared rules");
    }

    [Fact(Skip = "Symbolic links need privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task LoadAsync_LinkToAFileOutsideTheRepo_IsIgnored()
    {
        // Arrange
        var secret = Dir("elsewhere");
        Write(secret, "notes.md", "private");
        var repo = Dir("repo");
        Dir("repo/.git");
        File.CreateSymbolicLink(Path.Combine(repo, "AGENTS.md"), Path.Combine(secret, "notes.md"));

        // Act
        var context = await ProjectContextLoader.LoadAsync(repo, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().BeEmpty();
    }

    [Fact(Skip = "Symbolic links need privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task LoadAsync_BrokenLink_IsIgnored()
    {
        // Arrange
        var folder = Dir("app");
        File.CreateSymbolicLink(Path.Combine(folder, "AGENTS.md"), Path.Combine(folder, "missing.md"));

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().BeEmpty();
    }

    [Fact(Skip = "Named pipes are a Unix feature.", SkipWhen = nameof(IsWindows), Timeout = 10_000)]
    public async Task LoadAsync_NamedPipeNamedAgentsMd_IsIgnoredWithoutBlocking()
    {
        // Arrange
        var folder = Dir("app");
        using var mkfifo = System.Diagnostics.Process.Start("mkfifo", Path.Combine(folder, "AGENTS.md"));
        await mkfifo.WaitForExitAsync(TestContext.Current.CancellationToken);

        // Act
        var context = await ProjectContextLoader.LoadAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        context.Files.Should().BeEmpty();
    }

    private string Dir(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string directory, string name, string content)
    {
        File.WriteAllText(Path.Combine(directory, name), content);
    }
}
