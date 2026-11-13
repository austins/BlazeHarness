using BlazeHarness.Core.Git;
using BlazeHarness.Tests.Shared;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Git;

public sealed class GitChangeReverterTests : IDisposable
{
    private const string Reason = "git is not installed.";
    private const string SymlinkReason = "git is not installed, or symbolic links can't be created here.";

    private readonly TempFolder _folder = new("bh-git-revert-");
    private readonly GitChangeReverter _sut = new(TimeProvider.System);
    private readonly GitChangesReader _reader = new(TimeProvider.System);

    public static bool CanUseUnixMode => TestGit.IsAvailable && !OperatingSystem.IsWindows();

    public static bool CanUseSymlinks => TestGit.IsAvailable && SymlinkSupport.CanCreate;

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_UntrackedFile_DeletesIt()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("new.txt", "x\n");
        var (status, file) = await StatusOfAsync("new.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        File.Exists(_folder.Combine("new.txt")).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_UntrackedFileAlreadyGone_ReturnsChanged()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("new.txt", "x\n");
        var (status, file) = await StatusOfAsync("new.txt");
        File.Delete(_folder.Combine("new.txt"));

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Changed);
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData(GitChangeScope.All)]
    [InlineData(GitChangeScope.Unstaged)]
    public async Task RevertFileAsync_ModifiedFile_RestoresIt(GitChangeScope scope)
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        var (status, file) = await StatusOfAsync("a.txt", scope);

        // Act
        var result = await _sut.RevertFileAsync(status.Root, scope, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
        (await StatusAsync(GitChangeScope.All)).Files.Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_PartlyStagedFileInAllScope_EndsIdenticalToHead()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        await WriteAsync("a.txt", "one\nTWO\nTHREE\nfour\n");
        var (status, file) = await StatusOfAsync("a.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
        (await StatusAsync(GitChangeScope.All)).Files.Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_UnstagedScopeOfPartlyStagedFile_KeepsTheStagedVersion()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        await WriteAsync("a.txt", "one\nTWO\nTHREE\n");
        var (status, file) = await StatusOfAsync("a.txt", GitChangeScope.Unstaged);

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.Unstaged, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\nTWO\nthree\n");
        (await StatusAsync(GitChangeScope.Unstaged)).Files.Should().BeEmpty();
        (await StatusAsync(GitChangeScope.Staged)).Files.Should().ContainSingle();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_DeletedFile_RestoresIt()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        File.Delete(_folder.Combine("a.txt"));
        var (status, file) = await StatusOfAsync("a.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_StagedNewFileInAllScope_RemovesIt()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("new.txt", "x\n");
        await TestGit.RunAsync(_folder.Root, "add", "new.txt");
        var (status, file) = await StatusOfAsync("new.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        File.Exists(_folder.Combine("new.txt")).Should().BeFalse();
        (await StatusAsync(GitChangeScope.All)).Files.Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_StagedNewFileInRepositoryWithoutCommits_RemovesIt()
    {
        // Arrange
        await TestGit.InitEmptyAsync(_folder.Root);
        await WriteAsync("new.txt", "x\n");
        await TestGit.RunAsync(_folder.Root, "add", "new.txt");
        var (status, file) = await StatusOfAsync("new.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        File.Exists(_folder.Combine("new.txt")).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_StagedScope_IsNotAllowed()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        var (status, file) = await StatusOfAsync("a.txt", GitChangeScope.Staged);

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.Staged, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        (await ReadAsync("a.txt")).Should().Be("one\nTWO\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_ConflictedFile_IsNotAllowed()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        var file = new GitChangedFile("a.txt", GitChangeKind.Conflicted, true, true, null, null, false, string.Empty);

        // Act
        var result = await _sut.RevertFileAsync(_folder.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        (await ReadAsync("a.txt")).Should().Be("one\nTWO\nthree\n");
    }

    [Theory(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    [InlineData("")]
    [InlineData("sub//x.txt")]
    public async Task RevertFileAsync_PathEscapingTheRoot_IsNotAllowed(string path)
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        var repo = _folder.CreateDirectory("repo");
        await TestGit.InitAsync(repo);
        var target = outside.Combine("outside.txt");
        await File.WriteAllTextAsync(target, "keep\n", TestContext.Current.CancellationToken);
        var file = new GitChangedFile(path, GitChangeKind.Untracked, false, true, null, null, false, string.Empty);
        var absolute = new GitChangedFile(target, GitChangeKind.Untracked, false, true, null, null, false, string.Empty);

        // Act
        var result = await _sut.RevertFileAsync(repo, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();
        var absoluteResult = await _sut.RevertFileAsync(repo, GitChangeScope.All, absolute, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        absoluteResult.Should().Be(GitRevertResult.NotAllowed);
        File.Exists(target).Should().BeTrue();
    }

    [Fact(Skip = SymlinkReason, SkipUnless = nameof(CanUseSymlinks))]
    public async Task RevertFileAsync_FileBehindASymlinkedFolder_IsNotAllowed()
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        var repo = _folder.CreateDirectory("repo");
        await TestGit.InitAsync(repo);
        await File.WriteAllTextAsync(outside.Combine("victim.txt"), "keep\n", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(repo, "link"), outside.Root);
        var file = new GitChangedFile("link/victim.txt", GitChangeKind.Untracked, false, true, null, null, false, string.Empty);

        // Act
        var result = await _sut.RevertFileAsync(repo, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        File.Exists(outside.Combine("victim.txt")).Should().BeTrue();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertFileAsync_ConfiguredSmudgeFilter_DoesNotExecuteHelpers()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync(".gitattributes", "*.txt filter=external\n");
        await TestGit.RunAsync(_folder.Root, "config", "filter.external.smudge", "echo helper-ran > helper.txt; cat");
        await TestGit.RunAsync(_folder.Root, "config", "filter.external.required", "true");
        await WriteAsync("a.txt", "changed contents\n");
        var (status, file) = await StatusOfAsync("a.txt");

        // Act
        var result = await _sut.RevertFileAsync(status.Root, GitChangeScope.All, file, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
        File.Exists(_folder.Combine("helper.txt")).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_TwoHunks_RestoresOnlyTheChosenOne()
    {
        // Arrange
        await CommitLinesAsync("\n");
        await WriteAsync("a.txt", Edited("\n", [3, 27]));
        var (status, file, diff) = await DiffOfAsync("a.txt");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks[0], TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff.Hunks.Should().HaveCount(2);
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be(Edited("\n", [27]));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_OneChangeOfAWholeFileHunk_RestoresOnlyThatChange()
    {
        // Arrange
        await CommitLinesAsync("\n");
        await WriteAsync("a.txt", Edited("\n", [3, 27]));
        var (status, file, diff) = await DiffOfAsync("a.txt", contextLines: GitChangesReader.WholeFileContext);
        var changes = diff.Hunks.Single().SplitChanges();

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, changes[1].Change, TestContext.Current.CancellationToken).Bounded();

        // Assert
        changes.Should().HaveCount(2);
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be(Edited("\n", [3]));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_UnstagedScope_RestoresTheChosenHunk()
    {
        // Arrange
        await CommitLinesAsync("\n");
        await WriteAsync("a.txt", Edited("\n", [3, 27]));
        var (status, file, diff) = await DiffOfAsync("a.txt", GitChangeScope.Unstaged);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.Unstaged, file, diff.Hunks[1], TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be(Edited("\n", [3]));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_FileWithCrLfLines_KeepsCrLf()
    {
        // Arrange
        await CommitLinesAsync("\r\n");
        await WriteAsync("a.txt", Edited("\r\n", [3, 27]));
        var (status, file, diff) = await DiffOfAsync("a.txt");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks[0], TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be(Edited("\r\n", [27]));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_FileWithoutFinalNewline_KeepsItWithout()
    {
        // Arrange
        await CommitLinesAsync("\n", finalNewline: false);
        await WriteAsync("a.txt", Edited("\n", [3, 30], finalNewline: false));
        var (status, file, diff) = await DiffOfAsync("a.txt");

        // Act
        var first = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks[0], TestContext.Current.CancellationToken).Bounded();
        var afterFirst = await ReadAsync("a.txt");
        var (_, _, second) = await DiffOfAsync("a.txt");
        var last = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, second.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        first.Should().Be(GitRevertResult.Reverted);
        afterFirst.Should().Be(Edited("\n", [30], finalNewline: false));
        last.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be(Lines("\n", finalNewline: false));
        (await StatusAsync(GitChangeScope.All)).Files.Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_FileChangedSinceTheDiff_ReturnsChangedAndLeavesItAlone()
    {
        // Arrange
        await CommitLinesAsync("\n");
        await WriteAsync("a.txt", Edited("\n", [3, 27]));
        var (status, file, diff) = await DiffOfAsync("a.txt");
        var changedSince = Edited("\n", [3, 27]).Replace("line3 changed", "someone else was here", StringComparison.Ordinal);
        await WriteAsync("a.txt", changedSince);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks[0], TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Changed);
        (await ReadAsync("a.txt")).Should().Be(changedSince);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_FileShorterThanTheDiff_ReturnsChanged()
    {
        // Arrange
        await CommitLinesAsync("\n");
        await WriteAsync("a.txt", Edited("\n", [27]));
        var (status, file, diff) = await DiffOfAsync("a.txt");
        await WriteAsync("a.txt", "line1\n");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Changed);
        (await ReadAsync("a.txt")).Should().Be("line1\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_PureRemovalWithoutContext_PutsTheLinesBack()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nthree\n");
        var (status, file, diff) = await DiffOfAsync("a.txt", contextLines: 0);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff.Hunks.Single().Header.Should().StartWith("@@ -2 +1,0 @@");
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_PureRemovalAtTheStart_PutsTheLinesBack()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "two\nthree\n");
        var (status, file, diff) = await DiffOfAsync("a.txt", contextLines: 0);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_PureAdditionWithoutContext_RemovesTheLines()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nnew1\nnew2\ntwo\nthree\n");
        var (status, file, diff) = await DiffOfAsync("a.txt", contextLines: 0);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("a.txt")).Should().Be("one\ntwo\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_AddedFileHunk_EmptiesTheFile()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("new.txt", "x\ny\n");
        await TestGit.RunAsync(_folder.Root, "add", "new.txt");
        var (status, file, diff) = await DiffOfAsync("new.txt");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        file.Kind.Should().Be(GitChangeKind.Added);
        result.Should().Be(GitRevertResult.Reverted);
        (await ReadAsync("new.txt")).Should().BeEmpty();
    }

    [UnsupportedOSPlatform("windows")]
    [Fact(Skip = "Needs git and a file system with Unix file modes.", SkipUnless = nameof(CanUseUnixMode), SkipType = typeof(GitChangeReverterTests))]
    public async Task RevertHunkAsync_ExecutableFile_KeepsItsMode()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        File.SetUnixFileMode(_folder.Combine("a.txt"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var (status, file, diff) = await DiffOfAsync("a.txt");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.Reverted);
        File.GetUnixFileMode(_folder.Combine("a.txt")).Should().HaveFlag(UnixFileMode.UserExecute);
        Directory.GetFiles(_folder.Root, "*.tmp").Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_StagedScope_IsNotAllowed()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        await TestGit.RunAsync(_folder.Root, "add", "a.txt");
        var (status, file, diff) = await DiffOfAsync("a.txt", GitChangeScope.Staged);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.Staged, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        (await ReadAsync("a.txt")).Should().Be("one\nTWO\nthree\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_UntrackedFile_IsNotAllowed()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("new.txt", "x\n");
        var (status, file, diff) = await DiffOfAsync("new.txt");

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, diff.Hunks.Single(), TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        (await ReadAsync("new.txt")).Should().Be("x\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_PathEscapingTheRoot_IsNotAllowed()
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        var repo = _folder.CreateDirectory("repo");
        await TestGit.InitAsync(repo);
        await File.WriteAllTextAsync(outside.Combine("outside.txt"), "x\n", TestContext.Current.CancellationToken);
        var file = new GitChangedFile("../" + Path.GetFileName(outside.Root) + "/outside.txt", GitChangeKind.Modified, false, true, 1, 1, false, string.Empty);
        var hunk = new GitDiffHunk("@@ -1 +1 @@", [new GitDiffLine(Core.Diff.DiffKind.Removed, 1, null, "y"), new GitDiffLine(Core.Diff.DiffKind.Added, null, 1, "x")]);

        // Act
        var result = await _sut.RevertHunkAsync(repo, GitChangeScope.All, file, hunk, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
        (await File.ReadAllTextAsync(outside.Combine("outside.txt"), TestContext.Current.CancellationToken)).Should().Be("x\n");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task RevertHunkAsync_HunkWhoseCountsDoNotMatchItsLines_IsNotAllowed()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await WriteAsync("a.txt", "one\nTWO\nthree\n");
        var (status, file, _) = await DiffOfAsync("a.txt");
        var hunk = new GitDiffHunk("@@ -1,9 +1,9 @@", [new GitDiffLine(Core.Diff.DiffKind.Same, 1, 1, "one")]);

        // Act
        var result = await _sut.RevertHunkAsync(status.Root, GitChangeScope.All, file, hunk, TestContext.Current.CancellationToken).Bounded();

        // Assert
        result.Should().Be(GitRevertResult.NotAllowed);
    }

    private static string Lines(string ending, bool finalNewline = true)
    {
        var text = string.Join(ending, Enumerable.Range(1, 30).Select(i => $"line{i}"));
        return finalNewline ? text + ending : text;
    }

    private static string Edited(string ending, int[] lineNumbers, bool finalNewline = true)
    {
        var text = string.Join(ending, Enumerable.Range(1, 30).Select(i => lineNumbers.Contains(i) ? $"line{i} changed" : $"line{i}"));
        return finalNewline ? text + ending : text;
    }

    private async Task CommitLinesAsync(string ending, bool finalNewline = true)
    {
        await TestGit.InitEmptyAsync(_folder.Root);
        await WriteAsync("a.txt", Lines(ending, finalNewline));
        await TestGit.RunAsync(_folder.Root, "add", "-A");
        await TestGit.RunAsync(_folder.Root, "commit", "-q", "-m", "lines");
    }

    private Task WriteAsync(string path, string content) => File.WriteAllTextAsync(_folder.Combine(path), content, TestContext.Current.CancellationToken);

    private async Task<string> ReadAsync(string path) => await File.ReadAllTextAsync(_folder.Combine(path), TestContext.Current.CancellationToken);

    private async Task<GitStatus> StatusAsync(GitChangeScope scope)
        => (await _reader.GetStatusAsync(_folder.Root, scope, TestContext.Current.CancellationToken).Bounded())!;

    private async Task<(GitStatus Status, GitChangedFile File)> StatusOfAsync(string path, GitChangeScope scope = GitChangeScope.All)
    {
        var status = await StatusAsync(scope);
        return (status, status.Files.Single(file => file.Path == path));
    }

    private async Task<(GitStatus Status, GitChangedFile File, GitFileDiff Diff)> DiffOfAsync(string path, GitChangeScope scope = GitChangeScope.All, int contextLines = 3)
    {
        var (status, file) = await StatusOfAsync(path, scope);
        var diff = await _reader.GetDiffAsync(status.Root, scope, file, contextLines, TestContext.Current.CancellationToken).Bounded();
        return (status, file, diff!);
    }
}
