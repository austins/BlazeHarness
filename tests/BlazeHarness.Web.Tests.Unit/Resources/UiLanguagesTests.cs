using BlazeHarness.Web.Resources;
using System.Globalization;

namespace BlazeHarness.Web.Tests.Unit.Resources;

public sealed class UiLanguagesTests
{
    private readonly UiLanguages _sut = new([CultureInfo.GetCultureInfo("zh-Hans"), CultureInfo.GetCultureInfo("ru")]);

    [Fact]
    public void All_TranslatedCultures_ListsEnglishFirstThenNativeNames()
    {
        // Act
        var all = _sut.All;

        // Assert
        all.Select(language => language.Culture).Should().Equal("en-US", "ru", "zh-Hans");
        all[0].DisplayName.Should().Be("English");
        all[1].DisplayName.Should().StartWith("Р");
    }

    [Theory]
    [InlineData("en-GB", "en-US")]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-TW", "en-US")]
    [InlineData("fr-FR", "en-US")]
    public void Resolve_RequestCulture_FindsTheTranslatedLanguageOrEnglish(string culture, string expected)
    {
        // Act
        var result = _sut.Resolve(CultureInfo.GetCultureInfo(culture));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void SupportedCultures_TranslatedCultures_IncludesParentCultures()
    {
        // Act
        var cultures = _sut.SupportedCultures;

        // Assert
        cultures.Should().BeEquivalentTo("en-US", "en", "ru", "zh-Hans", "zh");
    }
}
