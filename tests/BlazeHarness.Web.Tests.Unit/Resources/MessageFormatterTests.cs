using Jeffijoe.MessageFormat;
using System.Globalization;

namespace BlazeHarness.Web.Tests.Unit.Resources;

public sealed class MessageFormatterTests
{
    [Theory]
    [InlineData("en-US", 0, "Ada has 0 tasks")]
    [InlineData("en-US", 1, "Ada has 1 task")]
    [InlineData("en-US", 2, "Ada has 2 tasks")]
    [InlineData("ru-RU", 1, "Ada has 1 task")]
    [InlineData("ru-RU", 2, "Ada has 2 tasks (few)")]
    [InlineData("ru-RU", 5, "Ada has 5 tasks (many)")]
    [InlineData("ru-RU", 21, "Ada has 21 task")]
    public void FormatMessage_PluralsAndNamedValues_UseTheRequestedCulture(string culture, int count, string expected)
    {
        // Arrange
        const string pattern = "{name} has {count, plural, one {# task} few {# tasks (few)} many {# tasks (many)} other {# tasks}}";
        var values = new Dictionary<string, object?> { ["name"] = "Ada", ["count"] = count };

        // Act
        var result = MessageFormatter.Format(pattern, values, CultureInfo.GetCultureInfo(culture));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatMessage_ExactCountAndNestedValue_UsesTheMatchingForm()
    {
        // Arrange
        const string pattern = "{count, plural, =0 {No tasks for {name}} one {One task for {name}} other {# tasks for {name}}}";
        var values = new Dictionary<string, object?> { ["name"] = "Ada", ["count"] = 0 };

        // Act
        var result = MessageFormatter.Format(pattern, values, CultureInfo.GetCultureInfo("en-US"));

        // Assert
        result.Should().Be("No tasks for Ada");
    }

    [Fact]
    public void FormatMessage_ExactCountBeforePluralCategory_UsesTheExactForm()
    {
        // Arrange
        const string pattern = "{count, plural, =1 {exact} one {category} other {other}}";
        var values = new Dictionary<string, object?> { ["count"] = 1 };

        // Act
        var result = MessageFormatter.Format(pattern, values, CultureInfo.GetCultureInfo("en-US"));

        // Assert
        result.Should().Be("exact");
    }
}
