using BlazeHarness.Web.Resources;
using System.Globalization;
using System.Resources;

namespace BlazeHarness.Web.Tests.Unit.Resources;

public sealed class ResourceMessageFormatterTests
{
    private readonly ResourceMessageFormatter _sut = new(
        new ResourceManager("BlazeHarness.Web.Tests.Unit.Resources.TestMessages", typeof(ResourceMessageFormatterTests).Assembly),
        CultureInfo.GetCultureInfo("en-US"));

    [Theory]
    [InlineData("fr-FR", "TranslatedCount", 0, "0 article")]
    [InlineData("fr-CA", "TranslatedCount", 2, "2 articles")]
    [InlineData("fr-FR", "FallbackCount", 0, "0 items")]
    [InlineData("fr-CA", "FallbackCount", 1, "1 item")]
    [InlineData("en-US", "FallbackCount", 2, "2 items")]
    public void Format_TranslatedAndFallbackMessages_UsesTheLanguageOfTheResource(
        string culture, string key, int count, string expected)
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["count"] = count };

        // Act
        var result = _sut.Format(key, arguments, CultureInfo.GetCultureInfo(culture));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatMarkup_TagsAndArguments_ConvertsOnlyKnownTagsAndEncodesTheRest()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["name"] = "<i>x</i>", ["file"] = "a&b.json" };

        // Act
        var result = _sut.FormatMarkup("Markup", arguments, CultureInfo.GetCultureInfo("en-US"));

        // Assert
        result.Value.Should().Be("Removes <strong>&lt;i&gt;x&lt;/i&gt;</strong> from <code>a&amp;b.json</code> &amp; &lt;b&gt;[unknown]");
    }

    [Fact]
    public void Format_MissingKey_ReportsTheMissingResource()
    {
        // Arrange
        var arguments = new Dictionary<string, object?>();

        // Act
        var act = () => _sut.Format("Missing", arguments, CultureInfo.GetCultureInfo("fr-FR"));

        // Assert
        act.Should().Throw<KeyNotFoundException>().WithMessage("*Missing*");
    }

    [Fact]
    public void Format_FallbackBeforeTranslation_KeepsTheTranslatedLanguage()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["count"] = 0 };
        var culture = CultureInfo.GetCultureInfo("fr-CA");

        // Act
        var fallback = _sut.Format("FallbackCount", arguments, culture);
        var translated = _sut.Format("TranslatedCount", arguments, culture);

        // Assert
        fallback.Should().Be("0 items");
        translated.Should().Be("0 article");
    }
}
