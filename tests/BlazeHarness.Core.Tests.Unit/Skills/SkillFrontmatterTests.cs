using BlazeHarness.Core.Skills;

namespace BlazeHarness.Core.Tests.Unit.Skills;

public sealed class SkillFrontmatterTests
{
    [Fact]
    public void Parse_NameAndDescription_ReadsBothAndTheBody()
    {
        // Arrange
        const string Text = "---\nname: pdf-tools\ndescription: Work with PDFs.\n---\n# Heading\n\nDo it.\n";

        // Act
        var result = SkillFrontmatter.Parse(Text);

        // Assert
        result.Should().Be(new SkillFile("pdf-tools", "Work with PDFs.", "# Heading\n\nDo it.", null));
    }

    [Theory]
    [InlineData("description: \"Say \\\"hi\\\": now\"", "Say \"hi\": now")]
    [InlineData("description: 'It''s fine'", "It's fine")]
    [InlineData("description: plain: text with colon", "plain: text with colon")]
    public void Parse_QuotedAndPlainValues_AreUnquoted(string line, string expected)
    {
        // Arrange
        var text = $"---\nname: a\n{line}\n---\nbody";

        // Act
        var result = SkillFrontmatter.Parse(text);

        // Assert
        result.Description.Should().Be(expected);
    }

    [Theory]
    [InlineData("description: >\n  First line\n  second line\n")]
    [InlineData("description: |-\n  First line\n  second line\n")]
    [InlineData("description: First line\n  second line\n")]
    public void Parse_ValueOverSeveralLines_IsJoinedOnOneLine(string field)
    {
        // Arrange
        var text = $"---\nname: a\n{field}---\nbody";

        // Act
        var result = SkillFrontmatter.Parse(text);

        // Assert
        result.Description.Should().Be("First line second line");
    }

    [Fact]
    public void Parse_CrLfAndBom_AreHandled()
    {
        // Arrange
        const string Text = "\uFEFF---\r\nname: a\r\ndescription: Does it.\r\n---\r\nbody\r\n";

        // Act
        var result = SkillFrontmatter.Parse(Text);

        // Assert
        result.Should().Be(new SkillFile("a", "Does it.", "body", null));
    }

    [Fact]
    public void Parse_OtherFieldsAndNestedMaps_AreIgnored()
    {
        // Arrange
        const string Text = "---\nname: a\nmetadata:\n  author: me\n  description: not this one\ndescription: The real one.\nlicense: MIT\n---\nbody";

        // Act
        var result = SkillFrontmatter.Parse(Text);

        // Assert
        result.Description.Should().Be("The real one.");
    }

    [Fact]
    public void Parse_MissingDescription_LeavesItNull()
    {
        // Arrange
        const string Text = "---\nname: a\n---\nbody";

        // Act
        var result = SkillFrontmatter.Parse(Text);

        // Assert
        result.Description.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("no front matter here")]
    [InlineData("---\nname: a\ndescription: never closed\n")]
    public void Parse_BrokenFrontMatter_ReportsAnError(string text)
    {
        // Arrange

        // Act
        var result = SkillFrontmatter.Parse(text);

        // Assert
        result.Error.Should().NotBeNullOrEmpty();
    }
}
