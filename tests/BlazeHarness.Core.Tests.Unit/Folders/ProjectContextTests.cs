using BlazeHarness.Core.Folders;

namespace BlazeHarness.Core.Tests.Unit.Folders;

public sealed class ProjectContextTests
{
    [Fact]
    public void ToPrompt_NoFiles_ReturnsNull()
    {
        // Arrange
        var sut = new ProjectContext("/work/app", []);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().BeNull();
    }

    [Fact]
    public void ToPrompt_OnlyFilesWithNoRoom_ReturnsNull()
    {
        // Arrange
        var sut = new ProjectContext("/work/app", [new ProjectFile("/work/AGENTS.md", string.Empty, true)]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().BeNull();
    }

    [Fact]
    public void ToPrompt_SeveralFiles_ListsThemInOrderWithTheirPaths()
    {
        // Arrange
        var sut = new ProjectContext("/work/app/sub", [new ProjectFile("/work/AGENTS.md", "outer rules", false), new ProjectFile("/work/app/sub/AGENTS.md", "inner rules", false)]);

        // Act
        var prompt = sut.ToPrompt()!;

        // Assert
        prompt.Should().Contain("<agents-md path=\"/work/AGENTS.md\">").And.Contain("<agents-md path=\"/work/app/sub/AGENTS.md\">");
        prompt.IndexOf("outer rules", StringComparison.Ordinal).Should().BeLessThan(prompt.IndexOf("inner rules", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAs_SameFolderAndTextWithSeparateInstances_ReturnsTrue()
    {
        // Arrange
        var sut = new ProjectContext("/work/app", [new ProjectFile("/work/app/AGENTS.md", "a", false)]);
        var other = new ProjectContext("/work/app", [new ProjectFile("/work/app/AGENTS.md", "a", false)]);

        // Act
        var same = sut.SameAs(other);

        // Assert
        same.Should().BeTrue();
    }

    [Fact]
    public void SameAs_ChangedText_ReturnsFalse()
    {
        // Arrange
        var sut = new ProjectContext("/work/app", [new ProjectFile("/work/app/AGENTS.md", "a", false)]);
        var other = new ProjectContext("/work/app", [new ProjectFile("/work/app/AGENTS.md", "b", false)]);

        // Act
        var same = sut.SameAs(other);

        // Assert
        same.Should().BeFalse();
    }
}
