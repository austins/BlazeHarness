using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;
using BlazeHarness.Core.Tools.Files;
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
        var diff = await _sut.GetDiffAsync(_folder.Root, GitChangeScope.All, status!.Files.Single(file => file.Path == "a.txt"), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        result!.Root.Should().Be(RootedPath.CanonicalRoot(_folder.Root));
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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.Staged, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

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
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), cancellationToken: TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Select(l => l.Text).Should().Equal("1", "2");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_UntrackedPathEscapingTheRoot_ReturnsNull()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        var file = new GitChangedFile("../outside.txt", GitChangeKind.Untracked, false, true, null, null, false, string.Empty);

        // Act
        var diff = await _sut.GetDiffAsync(Path.GetFullPath(_folder.Root), GitChangeScope.All, file, cancellationToken: TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff.Should().BeNull();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_ZeroContext_ShowsOnlyChangedLines()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWO\nthree\n", TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), contextLines: 0, TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Single().Lines.Select(l => l.Kind).Should().Equal(DiffKind.Removed, DiffKind.Added);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetDiffAsync_WholeFileContext_ShowsEveryLineInOneHunk()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        var old = string.Concat(Enumerable.Range(1, 40).Select(i => $"line{i}\n"));
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), old, TestContext.Current.CancellationToken);
        await TestGit.RunAsync(_folder.Root, "add", "-A");
        await TestGit.RunAsync(_folder.Root, "commit", "-q", "-m", "long");
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), old.Replace("line2\n", "two\n", StringComparison.Ordinal).Replace("line39\n", "x\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var status = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        var diff = await _sut.GetDiffAsync(status.Root, GitChangeScope.All, status.Files.Single(), GitChangesReader.WholeFileContext, TestContext.Current.CancellationToken).Bounded();

        // Assert
        diff!.Hunks.Should().ContainSingle().Which.Lines.Count(l => l.Kind != DiffKind.Added).Should().Be(40);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_NothingChangedBetweenCalls_ReturnsTheSameStatus()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWO\nthree\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_folder.Combine("new.txt"), "n\n", TestContext.Current.CancellationToken);

        // Act
        var first = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();
        var second = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        first!.IsSameAs(second).Should().BeTrue();
        first.Files.Should().OnlyContain(file => file.Stamp.Length > 0);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_FileEditedAgainWithSameCounts_ChangesTheStamp()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWO\nthree\n", TestContext.Current.CancellationToken);
        var before = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Act
        await File.WriteAllTextAsync(_folder.Combine("a.txt"), "one\nTWOO\nthree\n", TestContext.Current.CancellationToken);
        var after = (await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded())!;

        // Assert
        after.Files.Single().Stamp.Should().NotBe(before.Files.Single().Stamp);
        after.IsSameAs(before).Should().BeFalse();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestGit.IsAvailable), SkipType = typeof(TestGit))]
    public async Task GetStatusAsync_DeletedFile_HasADeletedStamp()
    {
        // Arrange
        await TestGit.InitAsync(_folder.Root);
        File.Delete(_folder.Combine("a.txt"));

        // Act
        var status = await _sut.GetStatusAsync(_folder.Root, GitChangeScope.All, TestContext.Current.CancellationToken).Bounded();

        // Assert
        status!.Files.Single().Stamp.Should().StartWith("deleted:");
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 })]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 })]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 2 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 1 })]
    public async Task GetImageAsync_PictureFile_ReturnsItsBytesAndTypeFromTheContent(string mediaType, byte[] content)
    {
        // Arrange
        await File.WriteAllBytesAsync(_folder.Combine("picture.dat"), content, TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("picture.dat"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().NotBeNull();
        image.MediaType.Should().Be(mediaType);
        image.Content.Should().Equal(content);
    }

    [Fact]
    public async Task GetImageAsync_TextFileNamedPng_ReturnsNull()
    {
        // Arrange
        await File.WriteAllTextAsync(_folder.Combine("fake.png"), "not a picture", TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("fake.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_SvgFile_ReturnsNull()
    {
        // Arrange
        await File.WriteAllTextAsync(_folder.Combine("a.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("a.svg"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_FileOverOneMebibyte_ReturnsNull()
    {
        // Arrange
        var content = new byte[(1024 * 1024) + 1];
        PngSignature.CopyTo(content, 0);
        await File.WriteAllBytesAsync(_folder.Combine("big.png"), content, TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("big.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_FileOfExactlyOneMebibyte_IsReturned()
    {
        // Arrange
        var content = new byte[1024 * 1024];
        PngSignature.CopyTo(content, 0);
        await File.WriteAllBytesAsync(_folder.Combine("max.png"), content, TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("max.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().NotBeNull();
    }

    [Theory]
    [InlineData(GitChangeKind.Deleted)]
    [InlineData(GitChangeKind.Conflicted)]
    public async Task GetImageAsync_DeletedOrConflictedFile_ReturnsNull(GitChangeKind kind)
    {
        // Arrange
        await File.WriteAllBytesAsync(_folder.Combine("a.png"), PngSignature, TestContext.Current.CancellationToken);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("a.png", kind), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_MissingFile_ReturnsNull()
    {
        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("missing.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_PathOutsideTheRoot_ReturnsNull()
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        await File.WriteAllBytesAsync(outside.Combine("a.png"), PngSignature, TestContext.Current.CancellationToken);
        var inside = _folder.CreateDirectory("repo");
        var relative = Path.GetRelativePath(inside, outside.Combine("a.png")).Replace('\\', '/');

        // Act
        var traversal = await _sut.GetImageAsync(inside, Changed(relative), TestContext.Current.CancellationToken).Bounded();
        var rooted = await _sut.GetImageAsync(inside, Changed(outside.Combine("a.png")), TestContext.Current.CancellationToken).Bounded();

        // Assert
        traversal.Should().BeNull();
        rooted.Should().BeNull();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task GetImageAsync_SymlinkedFile_ReturnsNull()
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        await File.WriteAllBytesAsync(outside.Combine("a.png"), PngSignature, TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(_folder.Combine("link.png"), outside.Combine("a.png"));

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("link.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    [Fact(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    public async Task GetImageAsync_FileBehindASymlinkedFolder_ReturnsNull()
    {
        // Arrange
        using var outside = new TempFolder("bh-git-outside-");
        await File.WriteAllBytesAsync(outside.Combine("a.png"), PngSignature, TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(_folder.Combine("folder-link"), outside.Root);

        // Act
        var image = await _sut.GetImageAsync(_folder.Root, Changed("folder-link/a.png"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        image.Should().BeNull();
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static GitChangedFile Changed(string path, GitChangeKind kind = GitChangeKind.Untracked)
        => new(path, kind, false, true, null, null, false, string.Empty);
}
