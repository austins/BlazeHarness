using BlazeHarness.Core.Git;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Git;

public sealed class GitBranchesTests : IDisposable
{
    private const string Reason = "git is not installed.";

    private readonly TempFolder _folder = new("bh-git-branches-");
    private readonly GitBranches _sut = new(TimeProvider.System);

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task ListAsync_BranchInOtherWorktree_ReportsItsPath()
    {
        // Arrange
        await InitAsync();
        await TestGit.RunAsync(_folder.Root, "branch", "other");
        await TestGit.RunAsync(_folder.Root, "worktree", "add", "-q", "-b", "elsewhere", Path.Combine(Path.GetTempPath(), Path.GetFileName(_folder.Root) + "-wt"), "main");

        try
        {
            // Act
            var list = await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken).Bounded();

            // Assert
            list!.Current.Should().Be("main");
            list.HasChanges.Should().BeFalse();
            list.Branches.Select(b => b.Name).Should().Equal("elsewhere", "main", "other");
            list.Branches.Single(b => b.Name == "elsewhere").WorktreePath.Should().NotBeNull();
            list.Branches.Single(b => b.Name == "other").WorktreePath.Should().BeNull();
        }
        finally
        {
            await TestGit.RunAsync(_folder.Root, "worktree", "remove", "--force", Path.Combine(Path.GetTempPath(), Path.GetFileName(_folder.Root) + "-wt"));
        }
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task ListAsync_DetachedHead_ReportsCommit()
    {
        // Arrange
        await InitAsync();
        await TestGit.RunAsync(_folder.Root, "switch", "-q", "--detach");

        // Act
        var list = await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken).Bounded();

        // Assert
        list!.Current.Should().BeNull();
        list.DetachedAt.Should().NotBeNullOrEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task ListAsync_NotARepository_ReturnsNull()
    {
        // Act
        var list = await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken).Bounded();

        // Assert
        list.Should().BeNull();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task SwitchAsync_Clean_ChecksOutBranch()
    {
        // Arrange
        await InitAsync();
        await TestGit.RunAsync(_folder.Root, "branch", "other");

        // Act
        var result = await _sut.SwitchAsync(_folder.Root, "other", TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitBranchResult.Done);
        (await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken))!.Current.Should().Be("other");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task SwitchAsync_TrackedChanges_IsNotAllowed()
    {
        // Arrange
        await InitAsync();
        await TestGit.RunAsync(_folder.Root, "branch", "other");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "changed\n", TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.SwitchAsync(_folder.Root, "other", TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitBranchResult.NotAllowed);
        (await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken))!.Current.Should().Be("main");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task CreateAsync_WithChanges_ChecksOutNewBranchAndKeepsChanges()
    {
        // Arrange
        await InitAsync();
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "changed\n", TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.CreateAsync(_folder.Root, "feature/x", TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitBranchResult.Done);
        var list = await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken);
        list!.Current.Should().Be("feature/x");
        list.HasChanges.Should().BeTrue();
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData("main", GitBranchResult.Exists)]
    [InlineData("--orphan", GitBranchResult.InvalidName)]
    public async Task CreateAsync_BadName_LeavesBranch(string name, GitBranchResult expected)
    {
        // Arrange
        await InitAsync();

        // Act
        var result = await _sut.CreateAsync(_folder.Root, name, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(expected);
        (await _sut.ListAsync(_folder.Root, TestContext.Current.CancellationToken))!.Current.Should().Be("main");
    }

    private async Task InitAsync()
    {
        await TestGit.InitAsync(_folder.Root);
        await TestGit.RunAsync(_folder.Root, "branch", "-M", "main");
    }
}
