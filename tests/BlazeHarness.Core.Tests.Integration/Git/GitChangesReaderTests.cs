using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Git;

public sealed class GitChangesReaderTests : IDisposable
{
    private const string Reason = "git is not installed.";

    private readonly TempFolder _folder = new("bh-git-");
    private readonly GitChangesReader _sut = new(TimeProvider.System);

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_FolderOutsideARepository_ReturnsNull()
    {
        // Act
        var result = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().BeNull();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_RepositoryCreatedLater_IsNoticedOnTheNextCall()
    {
        // Arrange
        var before = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Act
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("new.txt"), "x\n", TestContext.Current.CancellationToken);
        var after = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        before.Should().BeNull();
        after!.Files.Should().ContainSingle().Which.Path.Should().Be("new.txt");
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData("clean")]
    [InlineData("process")]
    public async Task GetStatusAsync_ConfiguredFilters_DoesNotExecuteHelpers(string filter)
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine(".gitattributes"), "*.txt filter=external\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "config", $"filter.external.{filter}", "echo helper-ran > helper.txt");
        await TestGit.RunAsync(_folder.Root, "config", "filter.external.required", "true");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "changed contents\n", TestContext.Current.CancellationToken);

        // Act
        var status = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();
        var diff = await _sut.GetDiffAsync(_folder.Root, GitChangeScope.All, status!.Files.Single(file => file.Path == "a.txt"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff.Should().NotBeNull();
        File.Exists(_folder.Combine("helper.txt")).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_CleanRepository_ListsNothing()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);

        // Act
        var result = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result!.Files.Should().BeEmpty();
        result.AllCount.Should().Be(0);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_StagedUnstagedAndUntracked_FiltersByScope()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("staged.txt"), "s\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "staged.txt");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWO\nthree\nfour\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_folder.Combine("loose.txt"), "l\n", TestContext.Current.CancellationToken);

        // Act
        var all = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();
        var staged = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.Staged, TestContext.Current.CancellationToken).Bounded();
        var unstaged = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.Unstaged, TestContext.Current.CancellationToken).Bounded();

        // Assert
        all!.Files.Select(f => f.Path).Should().Equal("a.txt", "loose.txt", "staged.txt");
        staged!.Files.Select(f => f.Path).Should().Equal("staged.txt");
        unstaged!.Files.Select(f => f.Path).Should().Equal("a.txt", "loose.txt");
        (all.AllCount, all.StagedCount, all.UnstagedCount).Should().Be((3, 1, 2));
        all.Files.Single(f => f.Path == "a.txt").Should().Match<GitChangedFile>(f => f.Added == 2 && f.Removed == 1 && f.Kind == GitChangeKind.Modified);
        all.Files.Single(f => f.Path == "loose.txt").Kind.Should().Be(GitChangeKind.Untracked);
        all.Files.Single(f => f.Path == "staged.txt").Kind.Should().Be(GitChangeKind.Added);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_SubfolderOfARepository_UsesTheRepositoryRoot()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        var sub = _folder.CreateDirectory("src", "deep");
        await File.WriteAllTextAsync(Path.Combine(sub, "x.cs"), "class X {}\n", TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.GetStatusAsync(sub, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result!.Root.Should().Be(Path.GetFullPath(_folder.Root));
        result.Files.Should().ContainSingle().Which.Path.Should().Be("src/deep/x.cs");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_RepositoryWithoutCommits_ListsStagedAndUntracked()
    {
        // Arrange
        await TestGit.RunAsync(_folder.Root, "init", "-q");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "a\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        await File.WriteAllTextAsync(_folder.Combine("b.txt"), "b\n", TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result!.Files.Select(f => f.Path).Should().Equal("a.txt", "b.txt");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_ModifiedFile_ReturnsNumberedHunk()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWO\nthree\n", TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Should().Equal(
            new GitDiffLine(DiffKind.Same, 1, 1, "one"),
            new GitDiffLine(DiffKind.Removed, 2, null, "two"),
            new GitDiffLine(DiffKind.Added, null, 2, "TWO"),
            new GitDiffLine(DiffKind.Same, 3, 3, "three"));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_StagedScope_IgnoresLaterUnstagedEdits()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\ntwo\nthree\nfour\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\ntwo\nthree\nfour\nfive\n", TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.Staged, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.Staged, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Where(l => l.Kind == DiffKind.Added).Select(l => l.Text).Should().Equal("four");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_UntrackedTextFile_ShowsEveryLineAsAdded()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("new.txt"), "x\ny\n", TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Should().Equal(
            new GitDiffLine(DiffKind.Added, null, 1, "x"),
            new GitDiffLine(DiffKind.Added, null, 2, "y"));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_UntrackedBinaryFile_IsMarkedBinary()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllBytesAsync(_folder.Combine("data.bin"), [1, 2, 0, 3], TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.IsBinary.Should().BeTrue();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_TrackedBinaryFile_IsMarkedBinaryInListAndDiff()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllBytesAsync(_folder.Combine("data.bin"), [1, 2, 0, 3], TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "data.bin");
        await TestGit.RunAsync(_folder.Root, "commit", "-q", "-m", "bin");
        await File.WriteAllBytesAsync(_folder.Combine("data.bin"), [9, 9, 0, 9], TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        status.Files.Single().IsBinary.Should().BeTrue();
        diff!.IsBinary.Should().BeTrue();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_PathWithGlobCharacters_IsTakenLiterally()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("[a].txt"), "1\n", TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "-A");
        await TestGit.RunAsync(_folder.Root, "commit", "-q", "-m", "glob");
        await File.WriteAllTextAsync(_folder.Combine("[a].txt"), "2\n", TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Select(l => l.Text).Should().Equal("1", "2");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_UntrackedPathEscapingTheRoot_ReturnsNull()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        var file = new GitChangedFile("../outside.txt", GitChangeKind.Untracked, false, true, null, null, false);

        // Act
        var diff = await _sut.GetDiffAsync(Path.GetFullPath(_folder.Root), GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff.Should().BeNull();
    }
}
