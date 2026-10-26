using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class ToolArgumentsTests
{
    [Fact]
    public void RequiredString_Present_ReturnsIt()
    {
        // Arrange
        var sut = ToolArguments.Parse("""{"path":"a.txt"}""");

        // Act
        var result = sut.RequiredString("path");

        // Assert
        result.Should().Be("a.txt");
    }

    [Fact]
    public void RequiredString_Missing_Throws()
    {
        // Arrange
        var sut = ToolArguments.Parse("{}");

        // Act
        var act = () => sut.RequiredString("path");

        // Assert
        act.Should().Throw<ToolArgumentException>().WithMessage("*'path'*required*");
    }

    [Fact]
    public void RequiredString_WrongType_Throws()
    {
        // Arrange
        var sut = ToolArguments.Parse("""{"path":5}""");

        // Act
        var act = () => sut.RequiredString("path");

        // Assert
        act.Should().Throw<ToolArgumentException>().WithMessage("*must be a string*");
    }

    [Fact]
    public void OptionalString_Null_ReturnsNull()
    {
        // Arrange
        var sut = ToolArguments.Parse("""{"path":null}""");

        // Act
        var result = sut.OptionalString("path");

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"n":3}""", 3)]
    [InlineData("{}", null)]
    public void OptionalInt_ValidOrAbsent_ReturnsIt(string json, int? expected)
    {
        // Arrange
        var sut = ToolArguments.Parse(json);

        // Act
        var result = sut.OptionalInt("n", 1, 10);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"n":0}""")]
    [InlineData("""{"n":11}""")]
    [InlineData("""{"n":1.5}""")]
    [InlineData("""{"n":"3"}""")]
    public void OptionalInt_OutOfRangeOrWrongType_Throws(string json)
    {
        // Arrange
        var sut = ToolArguments.Parse(json);

        // Act
        var act = () => sut.OptionalInt("n", 1, 10);

        // Assert
        act.Should().Throw<ToolArgumentException>();
    }

    [Fact]
    public void OptionalBool_AbsentOrWrongType_UsesDefaultOrThrows()
    {
        // Arrange
        var absent = ToolArguments.Parse("{}");
        var wrong = ToolArguments.Parse("""{"all":"yes"}""");

        // Act
        var result = absent.OptionalBool("all", defaultValue: true);
        var act = () => wrong.OptionalBool("all");

        // Assert
        result.Should().BeTrue();
        act.Should().Throw<ToolArgumentException>();
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        // Act
        var act = () => ToolArguments.Parse("{oops");

        // Assert
        act.Should().Throw<ToolArgumentException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Parse_BlankText_MeansNoArguments()
    {
        // Act
        var result = ToolArguments.Parse("  ").OptionalString("path");

        // Assert
        result.Should().BeNull();
    }
}
