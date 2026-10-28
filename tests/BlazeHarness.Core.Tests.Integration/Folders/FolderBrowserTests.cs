using BlazeHarness.Core.Folders;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Folders;

public sealed class FolderBrowserTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-browse-");

    public FolderBrowserTests()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "Beta"));
        Directory.CreateDirectory(Path.Combine(_temp.Root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_temp.Root, ".hidden"));
        File.WriteAllText(Path.Combine(_temp.Root, "file.txt"), "x");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void List_ReturnsOnlyFoldersSortedByNameAndHidesDotFolders()
    {
        // Act
        var result = FolderBrowser.List(_temp.Root, _temp.Root);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.Path.Should().Be(_temp.Root);
        result.Folders.Select(f => f.Name).Should().Equal("alpha", "Beta");
        result.ParentPath.Should().Be(Path.GetDirectoryName(_temp.Root));
    }

    [Fact]
    public void List_IncludeHidden_ShowsDotFolders()
    {
        // Act
        var result = FolderBrowser.List(_temp.Root, _temp.Root, includeHidden: true);

        // Assert
        result.Folders.Select(f => f.Name).Should().Contain(".hidden");
    }

    [Fact]
    public void List_NoPath_ListsTheHomeFolder()
    {
        // Act
        var result = FolderBrowser.List(null, _temp.Root);

        // Assert
        result.Path.Should().Be(_temp.Root);
    }

    [Fact]
    public void List_FolderPathsInTheResult_CanBeListedInTurn()
    {
        // Arrange
        var child = FolderBrowser.List(_temp.Root, _temp.Root).Folders[0];

        // Act
        var result = FolderBrowser.List(child.Path, _temp.Root);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.ParentPath.Should().Be(_temp.Root);
    }

    [Fact]
    public void List_RelativePath_IsRejected()
    {
        // Act
        var result = FolderBrowser.List("missing", _temp.Root);

        // Assert
        result.Error.Should().Be(FolderError.NotAbsolute);
        result.Folders.Should().BeEmpty();
    }

    [Fact]
    public void List_MissingFolder_ReportsNotFound()
    {
        // Act
        var result = FolderBrowser.List(Path.Combine(_temp.Root, "nope"), _temp.Root);

        // Assert
        result.Error.Should().Be(FolderError.NotFound);
    }

    [Fact]
    public void List_PathOfAFile_ReportsNotAFolder()
    {
        // Act
        var result = FolderBrowser.List(Path.Combine(_temp.Root, "file.txt"), _temp.Root);

        // Assert
        result.Error.Should().Be(FolderError.NotAFolder);
    }

    [Fact]
    public void Places_StartsWithTheHomeFolder()
    {
        // Act
        var places = FolderBrowser.Places(_temp.Root);

        // Assert
        places[0].Should().Be(new FolderEntry("Home", _temp.Root));
        places.Count.Should().BeGreaterThan(1);
    }
}
