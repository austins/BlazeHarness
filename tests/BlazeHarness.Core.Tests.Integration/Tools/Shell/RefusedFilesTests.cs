using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Tools.Shell;

public sealed class RefusedFilesTests : IDisposable
{
    private readonly TempRoot _root = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void TryFind_RefusedFilesAtAnyDepth_AreFound()
    {
        // Arrange
        _root.Write(".env", "x");
        _root.Write("api/deep/.env", "x");
        _root.Write("readme.txt", "x");
        var context = _root.Context.WithRefused(Refuse("**/.env"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().BeEquivalentTo(Path.Combine(_root.Root, ".env"), Path.Combine(_root.Root, "api", "deep", ".env"));
    }

    [Fact]
    public void TryFind_RefusedFolder_IsListedWithoutWhatIsInsideIt()
    {
        // Arrange
        _root.Write("secrets/a.txt", "x");
        _root.Write("secrets/b.txt", "x");
        var context = _root.Context.WithRefused(Refuse("secrets", "secrets/**"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(Path.Combine(_root.Root, "secrets"));
    }

    [Fact]
    public void TryFind_MoreEntriesThanTheLimit_ReportsAnIncompleteList()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            _root.Write($"f{i}.txt", "x");
        }

        var context = _root.Context.WithRefused(Refuse("**/.env"));

        // Act
        var complete = RefusedFiles.TryFind(context, 3, 10, out _, out var tooBroad, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeFalse();
        tooBroad.Should().Be("**/.env");
    }

    [Fact]
    public void TryFind_MorePathsThanTheLimit_ReportsAnIncompleteList()
    {
        // Arrange
        _root.Write("a.env", "x");
        _root.Write("b.env", "x");
        var context = _root.Context.WithRefused(Refuse("**/*.env"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 1, out _, out var tooBroad, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeFalse();
        tooBroad.Should().Be("**/*.env");
    }

    [Fact]
    public void TryFind_ProtectedFolder_IsNotWalked()
    {
        // Arrange
        _root.Write(".keys/.env", "x");
        var context = _root.Context
            .WithRefused(Refuse("**/.env"))
            .WithSensitive(new SensitivePaths([Path.Combine(_root.Root, ".keys")]));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().BeEmpty();
    }

    [Fact]
    public void TryFind_ApprovedReadFolder_IsLookedInToo()
    {
        // Arrange
        using var other = new TempFolder("bh-read-");
        var secret = Path.Combine(other.Root, "keys", ".env");
        Directory.CreateDirectory(Path.GetDirectoryName(secret)!);
        File.WriteAllText(secret, "x");
        var context = _root.Context.WithShellReadFolders([other.Root]).WithRefused(Refuse("**/.env"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(secret);
    }

    [Fact]
    public void TryFind_ReadFolderInsideTheWorkingFolder_IsNotCountedTwice()
    {
        // Arrange
        _root.Write("api/a.txt", "x");
        var context = _root.Context.WithShellReadFolders([Path.Combine(_root.Root, "api")]).WithRefused(Refuse("**/.env"));

        // Act: the working folder holds the api folder, its file and nothing else, so two entries fit.
        var complete = RefusedFiles.TryFind(context, 2, 10, out _, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
    }

    [Fact(Skip = "Creating a symbolic link needs a privilege on Windows.", SkipWhen = nameof(IsWindows))]
    public void TryFind_FolderLink_IsNotFollowed()
    {
        // Arrange
        using var other = new TempFolder("bh-link-");
        File.WriteAllText(Path.Combine(other.Root, ".env"), "x");
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), other.Root);
        var context = _root.Context.WithRefused(Refuse("**/.env"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().BeEmpty();
    }

    [Fact]
    public void TryFind_LiteralPathOutsideTheFolders_IsFound()
    {
        // Arrange
        using var other = new TempFolder("bh-literal-");
        var secret = Path.Combine(other.Root, "creds.json");
        File.WriteAllText(secret, "x");
        var context = _root.Context.WithRefused(Refuse(secret));

        // Act
        var complete = RefusedFiles.TryFind(context, 0, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(secret);
    }

    [Fact]
    public void TryFind_EverythingInAFolder_ListsTheFolderWithoutLookingInside()
    {
        // Arrange
        _root.Write("node_modules/a/index.js", "x");
        _root.Write("node_modules/b/index.js", "x");
        var context = _root.Context.WithRefused(Refuse("node_modules/**"));

        // Act
        var complete = RefusedFiles.TryFind(context, 0, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(Path.Combine(_root.Root, "node_modules"));
    }

    [Fact]
    public void TryFind_PatternWithoutDoubleStar_LooksNoDeeperThanItCanMatch()
    {
        // Arrange
        _root.Write("a.pem", "x");
        _root.Write("deep/b.pem", "x");
        _root.Write("deep/c.txt", "x");
        var context = _root.Context.WithRefused(Refuse("*.pem"));

        // Act: the working folder itself holds two entries, so a search that went deeper would pass the limit.
        var complete = RefusedFiles.TryFind(context, 2, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(Path.Combine(_root.Root, "a.pem"));
    }

    [Fact]
    public void TryFind_PatternUnderAFixedFolder_LooksOnlyInThatFolder()
    {
        // Arrange
        _root.Write("config/app.json", "x");
        _root.Write("config/readme.txt", "x");
        _root.Write("other/app.json", "x");
        _root.Write("other/more.json", "x");
        var context = _root.Context.WithRefused(Refuse("config/*.json"));

        // Act: only the config folder's two entries may be looked at.
        var complete = RefusedFiles.TryFind(context, 2, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().ContainSingle().Which.Should().Be(Path.Combine(_root.Root, "config", "app.json"));
    }

    [Fact]
    public void TryFind_ParentFolderPattern_DoesNotRefuseTheParent()
    {
        // Arrange
        var context = _root.Context.WithRefused(Refuse("../**"));

        // Act
        var complete = RefusedFiles.TryFind(context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().BeEmpty();
    }

    [Fact]
    public void TryFind_NothingRefusesAFile_FindsNothing()
    {
        // Arrange
        _root.Write(".env", "x");

        // Act
        var complete = RefusedFiles.TryFind(_root.Context, 100, 10, out var paths, out _, TestContext.Current.CancellationToken);

        // Assert
        complete.Should().BeTrue();
        paths.Should().BeEmpty();
    }

    private RefusedPaths Refuse(params string[] patterns)
    {
        return new RefusedPaths([.. patterns.Select(p => new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, p))], _root.Root);
    }
}
