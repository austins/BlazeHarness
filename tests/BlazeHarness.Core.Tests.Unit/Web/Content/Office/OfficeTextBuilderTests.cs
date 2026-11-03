using BlazeHarness.Core.Web.Content.Office;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class OfficeTextBuilderTests
{
    private readonly OfficeTextBuilder _sut = new();

    [Fact]
    public void AddBlock_Blocks_AreSeparatedByABlankLine()
    {
        // Act
        _sut.AddBlock("one");
        _sut.AddBlock("two");

        // Assert
        _sut.ToString().Should().Be("one\n\ntwo");
    }

    [Fact]
    public void AddBlock_EmptyBlock_IsIgnored()
    {
        // Act
        _sut.AddBlock("one");
        _sut.AddBlock(string.Empty);
        _sut.AddBlock("two");

        // Assert
        _sut.ToString().Should().Be("one\n\ntwo");
    }

    [Fact]
    public void AddListItem_ConsecutiveItems_SitTogether()
    {
        // Act
        _sut.AddListItem("- a");
        _sut.AddListItem("- b");
        _sut.AddBlock("para");
        _sut.AddListItem("- c");

        // Assert
        _sut.ToString().Should().Be("- a\n- b\n\npara\n\n- c");
    }

    [Theory]
    [InlineData(0, "# t")]
    [InlineData(3, "### t")]
    [InlineData(9, "###### t")]
    public void AddHeading_Level_IsClamped(int level, string expected)
    {
        // Act
        _sut.AddHeading(level, "t");

        // Assert
        _sut.ToString().Should().Be(expected);
        _sut.Headings.Should().Equal(expected);
    }

    [Fact]
    public void AddHeading_MoreThanTheLimit_KeepsTheFirstOnesInTheOutline()
    {
        // Act
        for (var i = 0; i < 100; i++)
        {
            _sut.AddHeading(2, $"h{i}");
        }

        // Assert
        _sut.Headings.Should().HaveCount(80);
        _sut.Headings[0].Should().Be("## h0");
        _sut.ToString().Should().Contain("## h99");
    }

    [Fact]
    public void HasText_OnlyHeadingMarkersAndWhitespace_IsFalse()
    {
        // Act
        _sut.AddHeading(1, " ");

        // Assert
        _sut.HasText.Should().BeFalse();
    }

    [Fact]
    public void HasText_Text_IsTrue()
    {
        // Act
        _sut.AddBlock("hello");

        // Assert
        _sut.HasText.Should().BeTrue();
    }

    [Fact]
    public void Full_ReachesTheLimit_IsTrue()
    {
        // Arrange
        _sut.Full.Should().BeFalse();

        // Act
        _sut.AddBlock(new string('x', OfficeTextBuilder.MaxCharacters));

        // Assert
        _sut.Full.Should().BeTrue();
    }
}
