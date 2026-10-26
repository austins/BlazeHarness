using BlazeHarness.Core.Folders;

namespace BlazeHarness.Core.Tests.Integration.Folders;

public sealed class FolderBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bh-browse-" + Guid.NewGuid().ToString("N"));

    public FolderBrowserTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Beta"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden"));
        File.WriteAllText(Path.Combine(_root, "file.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void List_ReturnsOnlyFoldersSortedByNameAndHidesDotFolders()
    {
        // Act
        var result = FolderBrowser.List(_root, _root);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.Path.Should().Be(_root);
        result.Folders.Select(f => f.Name).Should().Equal("alpha", "Beta");
        result.ParentPath.Should().Be(Path.GetDirectoryName(_root));
    }

    [Fact]
    public void List_IncludeHidden_ShowsDotFolders()
    {
        // Act
        var result = FolderBrowser.List(_root, _root, includeHidden: true);

        // Assert
        result.Folders.Select(f => f.Name).Should().Contain(".hidden");
    }

    [Fact]
    public void List_NoPath_ListsTheHomeFolder()
    {
        // Act
        var result = FolderBrowser.List(null, _root);

        // Assert
        result.Path.Should().Be(_root);
    }

    [Fact]
    public void List_FolderPathsInTheResult_CanBeListedInTurn()
    {
        // Arrange
        var child = FolderBrowser.List(_root, _root).Folders[0];

        // Act
        var result = FolderBrowser.List(child.Path, _root);

        // Assert
        result.Error.Should().Be(FolderError.None);
        result.ParentPath.Should().Be(_root);
    }

    [Fact]
    public void List_RelativePath_IsRejected()
    {
        // Act
        var result = FolderBrowser.List("missing", _root);

        // Assert
        result.Error.Should().Be(FolderError.NotAbsolute);
        result.Folders.Should().BeEmpty();
    }

    [Fact]
    public void List_MissingFolder_ReportsNotFound()
    {
        // Act
        var result = FolderBrowser.List(Path.Combine(_root, "nope"), _root);

        // Assert
        result.Error.Should().Be(FolderError.NotFound);
    }

    [Fact]
    public void List_PathOfAFile_ReportsNotAFolder()
    {
        // Act
        var result = FolderBrowser.List(Path.Combine(_root, "file.txt"), _root);

        // Assert
        result.Error.Should().Be(FolderError.NotAFolder);
    }

    [Fact]
    public void Places_StartsWithTheHomeFolder()
    {
        // Act
        var places = FolderBrowser.Places(_root);

        // Assert
        places[0].Should().Be(new FolderEntry("Home", _root));
        places.Count.Should().BeGreaterThan(1);
    }
}
