using BlazeHarness.Core.Folders;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Folders;

public sealed class FolderRegistryTests : IDisposable
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    private readonly TempFolder _temp = new("bh-folders-");
    private readonly string _home;
    private readonly AppPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly FolderRegistry _sut;

    public FolderRegistryTests()
    {
        _home = Path.Combine(_temp.Root, "home");
        Directory.CreateDirectory(_home);
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"), _home);
        _sut = Create();
    }

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task OpenAsync_ExistingFolder_IsListedWithItsNameAndSaved()
    {
        // Arrange
        var project = MakeFolder("my-project");

        // Act
        var result = await _sut.OpenAsync(project, TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.Folder!.Name.Should().Be("my-project");
        _sut.List().Should().ContainSingle().Which.Should().Be(result.Folder);
        File.Exists(_paths.FoldersFile).Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_HomeFolderItself_IsRefused()
    {
        // Act
        var result = await _sut.OpenAsync(_home, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(new OpenFolderResult(null, FolderError.NotAllowed));
        _sut.List().Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_TildeForTheHomeFolder_IsRefused()
    {
        // Act
        var result = await _sut.OpenAsync("~", TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotAllowed);
    }

    [Fact]
    public async Task OpenAsync_FileSystemRoot_IsRefused()
    {
        // Act
        var result = await _sut.OpenAsync(Path.GetPathRoot(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotAllowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("keys")]
    public async Task OpenAsync_AppDataFolderOrInsideIt_IsRefused(string inside)
    {
        // Arrange
        var folder = Path.Combine(_paths.Home, inside);
        Directory.CreateDirectory(folder);

        // Act
        var result = await _sut.OpenAsync(folder, TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotAllowed);
    }

    [Fact]
    public async Task OpenAsync_SameFolderTwiceWithATrailingSeparator_IsOneEntryOpenedAgain()
    {
        // Arrange
        var project = MakeFolder("app");
        var first = await _sut.OpenAsync(project, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromHours(1));

        // Act
        var second = await _sut.OpenAsync(project + Path.DirectorySeparatorChar, TestContext.Current.CancellationToken);

        // Assert
        _sut.List().Should().ContainSingle();
        second.Folder!.Path.Should().Be(first.Folder!.Path);
        second.Folder.LastOpenedAt.Should().Be(first.Folder.LastOpenedAt.AddHours(1));
    }

    [Fact(Skip = "Creating links needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task OpenAsync_FolderReachedThroughALink_IsTheSameEntryAsTheRealFolder()
    {
        // Arrange
        var real = MakeFolder("real");
        var link = Path.Combine(_temp.Root, "link");
        CreateLink(link, real);
        await _sut.OpenAsync(real, TestContext.Current.CancellationToken);

        // Act
        var viaLink = await _sut.OpenAsync(link, TestContext.Current.CancellationToken);

        // Assert
        _sut.List().Should().ContainSingle();
        viaLink.Folder!.Path.Should().Be(_sut.Find(real)!.Path);
    }

    [Fact(Skip = "Creating links needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task OpenAsync_TypedLinkName_IsUsedAsTheDisplayName()
    {
        // Arrange
        var real = MakeFolder("some-long-checkout-name");
        var link = Path.Combine(_temp.Root, "short");
        CreateLink(link, real);

        // Act
        var result = await _sut.OpenAsync(link, TestContext.Current.CancellationToken);

        // Assert
        result.Folder!.Name.Should().Be("short");
    }

    [Fact]
    public async Task OpenAsync_TildePath_MeansTheHomeFolder()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_home, "code"));

        // Act
        var result = await _sut.OpenAsync("~/code", TestContext.Current.CancellationToken);

        // Assert
        result.Folder!.Name.Should().Be("code");
    }

    [Theory]
    [InlineData("", FolderError.Empty)]
    [InlineData("relative/path", FolderError.NotAbsolute)]
    public async Task OpenAsync_UnusablePath_ReportsTheProblemAndSavesNothing(string typed, FolderError expected)
    {
        // Act
        var result = await _sut.OpenAsync(typed, TestContext.Current.CancellationToken);

        // Assert
        result.Folder.Should().BeNull();
        result.Error.Should().Be(expected);
        _sut.List().Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_MissingPath_ReportsNotFound()
    {
        // Act
        var result = await _sut.OpenAsync(Path.Combine(_temp.Root, "nope"), TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotFound);
    }

    [Fact]
    public async Task OpenAsync_PathOfAFile_ReportsNotAFolder()
    {
        // Arrange
        var file = Path.Combine(_temp.Root, "file.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.OpenAsync(file, TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotAFolder);
    }

    [Fact]
    public async Task List_PutsTheMostRecentlyOpenedFirst()
    {
        // Arrange
        await _sut.OpenAsync(MakeFolder("a"), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _sut.OpenAsync(MakeFolder("b"), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Act
        await _sut.TouchAsync(Path.Combine(_temp.Root, "a"), TestContext.Current.CancellationToken);

        // Assert
        _sut.List().Select(f => f.Name).Should().Equal("a", "b");
    }

    [Fact]
    public async Task RemoveAsync_ListedFolder_IsRemovedAndTheFolderOnDiskIsKept()
    {
        // Arrange
        var project = MakeFolder("app");
        await _sut.OpenAsync(project, TestContext.Current.CancellationToken);

        // Act
        var removed = await _sut.RemoveAsync(project, TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeTrue();
        _sut.List().Should().BeEmpty();
        Directory.Exists(project).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_UnlistedFolder_ReturnsFalse()
    {
        // Act
        var removed = await _sut.RemoveAsync(MakeFolder("app"), TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task List_FolderDeletedLater_StaysListedAndIsUnavailable()
    {
        // Arrange
        var project = MakeFolder("gone");
        var opened = await _sut.OpenAsync(project, TestContext.Current.CancellationToken);

        // Act
        Directory.Delete(project);

        // Assert
        _sut.List().Should().ContainSingle();
        FolderRegistry.IsAvailable(opened.Folder!).Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_SavedFolders_AreLoadedFromDisk()
    {
        // Arrange
        await _sut.OpenAsync(MakeFolder("kept"), TestContext.Current.CancellationToken);

        // Act
        using var reopened = Create();

        // Assert
        reopened.List().Should().ContainSingle().Which.Name.Should().Be("kept");
    }

    [Fact]
    public async Task OpenAsync_And_RemoveAsync_RaiseChanged()
    {
        // Arrange
        var raised = 0;
        _sut.Changed += (_, _) => raised++;
        var project = MakeFolder("app");

        // Act
        await _sut.OpenAsync(project, TestContext.Current.CancellationToken);
        await _sut.RemoveAsync(project, TestContext.Current.CancellationToken);

        // Assert
        raised.Should().Be(2);
    }

    [Fact]
    public async Task RelocateAsync_MissingFolder_IsReplacedByTheNewOne()
    {
        // Arrange
        var old = MakeFolder("old-name");
        var opened = await _sut.OpenAsync(old, TestContext.Current.CancellationToken);
        Directory.Delete(old);
        var moved = MakeFolder("new-name");

        // Act
        var result = await _sut.RelocateAsync(opened.Folder!.Path, moved, TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.Folder!.Name.Should().Be("new-name");
        _sut.List().Should().ContainSingle().Which.Should().Be(result.Folder);
    }

    [Fact]
    public async Task RelocateAsync_NewFolderAlreadyListed_MergesIntoOneEntry()
    {
        // Arrange
        var old = MakeFolder("old");
        var existing = MakeFolder("existing");
        await _sut.OpenAsync(old, TestContext.Current.CancellationToken);
        await _sut.OpenAsync(existing, TestContext.Current.CancellationToken);

        // Act
        await _sut.RelocateAsync(old, existing, TestContext.Current.CancellationToken);

        // Assert
        _sut.List().Should().ContainSingle().Which.Name.Should().Be("existing");
    }

    [Fact]
    public async Task RelocateAsync_NewPathDoesNotExist_KeepsTheOldEntryAndReportsTheError()
    {
        // Arrange
        var old = MakeFolder("old");
        await _sut.OpenAsync(old, TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.RelocateAsync(old, Path.Combine(_temp.Root, "nope"), TestContext.Current.CancellationToken);

        // Assert
        result.Error.Should().Be(FolderError.NotFound);
        _sut.List().Should().ContainSingle().Which.Name.Should().Be("old");
    }

    private FolderRegistry Create() => new(_paths, NullLoggerFactory.Instance, _time);

    private string MakeFolder(string name)
    {
        var path = Path.Combine(_temp.Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateLink(string link, string target) => Directory.CreateSymbolicLink(link, target);
}
