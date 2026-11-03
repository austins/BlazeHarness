using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Tests.Integration.Tools.Files;

public sealed class RootWalkerTests : IDisposable
{
    private readonly TempRoot _root = new();

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Walk_Recursive_ListsFoldersBeforeTheirContentsSortedByName()
    {
        // Arrange
        _root.Write("b/z.txt", "x");
        _root.Write("b/a.txt", "x");
        _root.Write("a.txt", "x");

        // Act
        var result = Walk(_root.Root, recursive: true, respectIgnore: true);

        // Assert
        result.Should().Equal("a.txt", "b", "b/a.txt", "b/z.txt");
    }

    [Fact]
    public void Walk_NotRecursive_ListsOnlyTheStartFolder()
    {
        // Arrange
        _root.Write("b/a.txt", "x");
        _root.Write("a.txt", "x");

        // Act
        var result = Walk(_root.Root, recursive: false, respectIgnore: true);

        // Assert
        result.Should().Equal("a.txt", "b");
    }

    [Fact]
    public void Walk_StartBelowTheRoot_AppliesTheIgnoreFilesAboveIt()
    {
        // Arrange
        _root.Write(".gitignore", "*.log\n");
        _root.Write("src/app.log", "x");
        _root.Write("src/app.cs", "x");

        // Act
        var result = Walk(Path.Combine(_root.Root, "src"), recursive: true, respectIgnore: true);

        // Assert
        result.Should().Equal("src/app.cs");
    }

    [Fact]
    public void Walk_IgnoreFilesNotRespected_ListsIgnoredEntriesButNotGit()
    {
        // Arrange
        _root.Write(".gitignore", "*.log\n");
        _root.Write(".git/config", "x");
        _root.Write("app.log", "x");

        // Act
        var result = Walk(_root.Root, recursive: true, respectIgnore: false);

        // Assert
        result.Should().Equal(".gitignore", "app.log");
    }

    [Fact]
    public void Walk_NestedIgnoreFile_AppliesOnlyInsideItsFolder()
    {
        // Arrange
        _root.Write("a/.gitignore", "gen.cs\n");
        _root.Write("a/gen.cs", "x");
        _root.Write("b/gen.cs", "x");

        // Act
        var result = Walk(_root.Root, recursive: true, respectIgnore: true);

        // Assert
        result.Should().Equal("a", "a/.gitignore", "b", "b/gen.cs");
    }

    [Fact(Skip = "Creating a symbolic link needs privileges on Windows.", SkipWhen = nameof(IsWindows))]
    public void Walk_LinkedFolder_IsListedButNotEntered()
    {
        // Arrange
        _root.Write("real/inner.txt", "x");
        Directory.CreateSymbolicLink(Path.Combine(_root.Root, "link"), Path.Combine(_root.Root, "real"));

        // Act
        var entries = RootWalker.Walk(_root.Context, _root.Root, recursive: true, respectIgnore: true, TestContext.Current.CancellationToken).ToList();

        // Assert
        entries.Select(e => e.RelativePath).Should().Equal("link", "real", "real/inner.txt");
        entries[0].IsLink.Should().BeTrue();
    }

    private List<string> Walk(string start, bool recursive, bool respectIgnore)
    {
        return [.. RootWalker.Walk(_root.Context, start, recursive, respectIgnore, TestContext.Current.CancellationToken).Select(e => e.RelativePath)];
    }
}
