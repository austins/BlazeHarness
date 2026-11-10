using BlazeHarness.Core.Git;
using BlazeHarness.Core.Tests.Integration.Git;
using BlazeHarness.Tests.Shared;
using ChatService = BlazeHarness.Core.Chats.ChatService;

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatServiceWorktreesTests : IDisposable
{
    private const string Reason = "git is not installed.";

    private readonly ChatServiceRig _rig = new("bh-svc-worktrees-", "ok");
    private readonly GitBranches _branches = new(TimeProvider.System);

    public ChatServiceWorktreesTests() => _rig.Build();

    private ChatService Sut => _rig.Service;

    public void Dispose() => _rig.Dispose();

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task MoveToWorktreeAsync_ThenLeave_ReturnsChatToFolderAndKeepsBranch()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await InitAsync();
        var info = await Sut.CreateAsync(_rig.Work, ct);
        var worktree = _rig.Paths.ChatWorktreeDirectory(info.Id);

        // Act
        var moved = await Sut.MoveToWorktreeAsync(info.Id, "feature", "main", ct).Bounded();
        var during = Sut.Store.Find(info.Id)!;
        var left = await Sut.LeaveWorktreeAsync(info.Id, deleteBranch: false).Bounded();

        // Assert
        moved.Should().Be(GitBranchResult.Done);
        during.FolderPath.Should().Be(worktree);
        during.Worktree!.RepositoryPath.Should().Be(_rig.Work);
        during.HomeFolder.Should().Be(_rig.Work);
        left.Should().BeTrue();
        Sut.Store.Find(info.Id)!.Should().Match<Core.Chats.ChatInfo>(c => c.FolderPath == _rig.Work && c.Worktree == null);
        Directory.Exists(worktree).Should().BeFalse();
        (await _branches.ListAsync(_rig.Work, ct))!.Branches.Select(b => b.Name).Should().Equal("feature", "main");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task DeleteAsync_WorktreeChat_RemovesWorktreeAndBranch()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await InitAsync();
        var info = await Sut.CreateAsync(_rig.Work, ct);
        await Sut.MoveToWorktreeAsync(info.Id, "feature", "main", ct).Bounded();

        // Act
        var deleted = await Sut.DeleteAsync(info.Id, deleteBranch: true, ct).Bounded();

        // Assert
        deleted.Should().BeTrue();
        Directory.Exists(_rig.Paths.ChatDirectory(info.Id)).Should().BeFalse();
        (await _branches.ListAsync(_rig.Work, ct))!.Branches.Select(b => b.Name).Should().Equal("main");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task LeaveWorktreeAsync_ChatHeldByAnotherChange_LeavesWorktreeAlone()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await InitAsync();
        var info = await Sut.CreateAsync(_rig.Work, ct);
        await Sut.MoveToWorktreeAsync(info.Id, "feature", "main", ct).Bounded();
        var chat = (await Sut.OpenAsync(info.Id, ct))!;
        chat.TryHold().Should().BeTrue();

        // Act
        var left = await Sut.LeaveWorktreeAsync(info.Id, deleteBranch: true).Bounded();
        var deleted = await Sut.DeleteAsync(info.Id, cancellationToken: ct).Bounded();

        // Assert
        left.Should().BeFalse();
        deleted.Should().BeFalse();
        Directory.Exists(_rig.Paths.ChatWorktreeDirectory(info.Id)).Should().BeTrue();
        chat.Release();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RelocateFolderAsync_RepositoryMoved_ReconnectsWorktree()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await InitAsync();
        var info = await Sut.CreateAsync(_rig.Work, ct);
        await Sut.MoveToWorktreeAsync(info.Id, "feature", "main", ct).Bounded();
        var moved = Path.Combine(Path.GetDirectoryName(_rig.Work)!, "moved");
        Directory.Move(_rig.Work, moved);

        // Act
        await Sut.RelocateFolderAsync(_rig.Work, moved).Bounded();

        // Assert
        var worktree = Sut.Store.Find(info.Id)!.Worktree!;
        worktree.RepositoryPath.Should().Be(moved);
        worktree.GitDirectory.Should().Be(Path.Combine(Path.GetFullPath(moved), ".git"));
        (await Sut.InspectWorktreeAsync(info.Id, ct).Bounded()).Should().Be(new GitWorktreeState(0, 0));
    }

    [Fact]
    public async Task MoveToWorktreeAsync_GeneralChat_IsNotAllowed()
    {
        // Arrange
        var info = await Sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await Sut.MoveToWorktreeAsync(info.Id, "feature", "main", TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(GitBranchResult.NotAllowed);
    }

    private async Task InitAsync()
    {
        await TestGit.InitAsync(_rig.Work);
        await TestGit.RunAsync(_rig.Work, "branch", "-M", "main");
    }
}
