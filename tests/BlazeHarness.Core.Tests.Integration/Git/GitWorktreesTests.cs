using BlazeHarness.Core.Git;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Git;

public sealed class GitWorktreesTests : IDisposable
{
    private const string Reason = "git is not installed.";

    private readonly TempFolder _folder = new("bh-git-worktree-");
    private readonly GitWorktrees _sut = new(TimeProvider.System);
    private readonly GitBranches _branches = new(TimeProvider.System);
    private readonly string _repo;
    private readonly string _path;

    public GitWorktreesTests()
    {
        _repo = _folder.CreateDirectory("repo");
        _path = _folder.Combine("chat", "worktree");
    }

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task AddAsync_FromSubfolder_ReturnsSameSubfolderInWorktree()
    {
        // Arrange
        await InitAsync();
        var sub = _folder.CreateDirectory("repo", "sub");
        await File.WriteAllTextAsync(Path.Combine(sub, "b.txt"), "b\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_repo, "add", "-A");
        await TestGit.RunAsync(_repo, "commit", "-q", "-m", "sub");

        // Act
        var (result, worktree) = await _sut.AddAsync(sub, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitBranchResult.Done);
        worktree.Should().NotBeNull();
        File.Exists(Path.Combine(worktree.FolderPath, "b.txt")).Should().BeTrue();
        Path.GetFileName(worktree.FolderPath).Should().Be("sub");
        Directory.Exists(Path.Combine(worktree.GitDirectory, "worktrees")).Should().BeTrue();
        (await _branches.ListAsync(worktree.FolderPath, TestContext.Current.CancellationToken))!.Current.Should().Be("feature");
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData("main", "main", GitBranchResult.Exists)]
    [InlineData("-x", "main", GitBranchResult.InvalidName)]
    [InlineData("feature", "missing", GitBranchResult.NotAllowed)]
    public async Task AddAsync_BadBranch_CreatesNothing(string branch, string baseBranch, GitBranchResult expected)
    {
        // Arrange
        await InitAsync();

        // Act
        var (result, worktree) = await _sut.AddAsync(_repo, _path, branch, baseBranch, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(expected);
        worktree.Should().BeNull();
        Directory.Exists(_path).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task InspectAsync_NewWorktree_IsClean()
    {
        // Arrange
        await InitAsync();
        await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();

        // Act
        var state = await _sut.InspectAsync(_path, "feature", TestContext.Current.CancellationToken).Bounded();

        // Assert
        state.Should().Be(new GitWorktreeState(0, 0));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task InspectAsync_CommitAndNewFile_CountsBoth()
    {
        // Arrange
        await InitAsync();
        await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();
        await File.WriteAllTextAsync(Path.Combine(_path, "a.txt"), "changed\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_path, "commit", "-q", "-am", "change");
        await File.WriteAllTextAsync(Path.Combine(_path, "new.txt"), "x\n", TestContext.Current.CancellationToken);

        // Act
        var state = await _sut.InspectAsync(_path, "feature", TestContext.Current.CancellationToken).Bounded();

        // Assert
        state.Should().Be(new GitWorktreeState(1, 1));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task InspectAsync_SwitchedToAnotherBranch_StillCountsCommitsOnlyOnTheWorktreeBranch()
    {
        // Arrange
        await InitAsync();
        await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();
        await File.WriteAllTextAsync(Path.Combine(_path, "a.txt"), "changed\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_path, "commit", "-q", "-am", "change");
        await TestGit.RunAsync(_path, "switch", "-q", "-c", "other", "main");

        // Act
        var state = await _sut.InspectAsync(_path, "feature", TestContext.Current.CancellationToken).Bounded();

        // Assert
        state.Should().Be(new GitWorktreeState(0, 1));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task AddAsync_FilterOnlyOnNewBranch_DoesNotRunIt()
    {
        // Arrange
        await InitAsync();
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitattributes"), "* filter=probe\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_repo, "add", "-A");
        await TestGit.RunAsync(_repo, "commit", "-q", "-m", "attributes");
        var marker = _folder.Combine("ran");
        var include = _folder.Combine("branch.gitconfig");
        await File.WriteAllTextAsync(include, $"[filter \"probe\"]\n\tsmudge = \"touch '{marker.Replace('\\', '/')}' && cat\"\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_repo, "config", "includeIf.onbranch:feature.path", include.Replace('\\', '/'));

        // Act
        var (result, worktree) = await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitBranchResult.Done);
        worktree.Should().NotBeNull();
        File.Exists(Path.Combine(worktree.FolderPath, "a.txt")).Should().BeTrue();
        File.Exists(marker).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RepairAsync_RepositoryMoved_ReconnectsWorktree()
    {
        // Arrange
        await InitAsync();
        await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();
        var moved = _folder.Combine("moved");
        Directory.Move(_repo, moved);

        // Act
        var gitDirectory = await _sut.RepairAsync(moved, _path, TestContext.Current.CancellationToken).Bounded();

        // Assert
        gitDirectory.Should().Be(Path.Combine(RootedPath.CanonicalRoot(moved), ".git"));
        (await _sut.InspectAsync(_path, "feature", TestContext.Current.CancellationToken).Bounded()).Should().Be(new GitWorktreeState(0, 0));
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveAsync_WithChanges_RemovesWorktreeAndChosenBranch(bool deleteBranch)
    {
        // Arrange
        await InitAsync();
        await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();
        await File.WriteAllTextAsync(Path.Combine(_path, "new.txt"), "x\n", TestContext.Current.CancellationToken);

        // Act
        var removed = await _sut.RemoveAsync(_repo, _path, "feature", deleteBranch, TestContext.Current.CancellationToken).Bounded();

        // Assert
        removed.Should().BeTrue();
        Directory.Exists(_path).Should().BeFalse();
        var list = await _branches.ListAsync(_repo, TestContext.Current.CancellationToken);
        list!.Branches.Select(b => b.Name).Should().BeEquivalentTo(deleteBranch ? ["main"] : ["feature", "main"]);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RemoveAsync_FolderAlreadyGone_ForgetsWorktreeFromGitDirectory()
    {
        // Arrange
        await InitAsync();
        var (_, worktree) = await _sut.AddAsync(_repo, _path, "feature", "main", TestContext.Current.CancellationToken).Bounded();
        worktree.Should().NotBeNull();
        Directory.Delete(_path, recursive: true);

        // Act
        var removed = await _sut.RemoveAsync(worktree.GitDirectory, _path, "feature", deleteBranch: true, TestContext.Current.CancellationToken).Bounded();

        // Assert
        removed.Should().BeTrue();
        var list = await _branches.ListAsync(_repo, TestContext.Current.CancellationToken);
        list!.Branches.Select(b => b.Name).Should().Equal("main");
    }

    private async Task InitAsync()
    {
        await TestGit.InitAsync(_repo);
        await TestGit.RunAsync(_repo, "branch", "-M", "main");
    }
}
