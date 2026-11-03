using BlazeHarness.Core.Web.Content.Office;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class MarkdownTableTests
{
    [Fact]
    public void Render_Rows_UsesTheFirstRowAsHeader()
    {
        // Act
        var result = MarkdownTable.Render([["Name", "Age"], ["Ann", "30"]]);

        // Assert
        result.Should().Be("| Name | Age |\n| --- | --- |\n| Ann | 30 |");
    }

    [Fact]
    public void Render_ShortRow_IsPadded()
    {
        // Act
        var result = MarkdownTable.Render([["A", "B"], ["x"]]);

        // Assert
        result.Should().EndWith("| x |  |");
    }

    [Fact]
    public void Render_PipeAndNewline_AreEscapedOrFlattened()
    {
        // Act
        var result = MarkdownTable.Render([["a|b"], ["line1\r\nline2"]]);

        // Assert
        result.Should().Contain("a\\|b").And.Contain("line1 line2");
    }

    [Fact]
    public void Render_NoCells_ReturnsEmpty()
    {
        // Act
        var result = MarkdownTable.Render([[]]);

        // Assert
        result.Should().BeEmpty();
    }
}
