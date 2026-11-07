using BlazeHarness.Web.Hosting;
using BlazeHarness.Web.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Globalization;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class CultureEndpointsTests
{
    private readonly UiLanguages _languages = new([CultureInfo.GetCultureInfo("zh-Hans"), CultureInfo.GetCultureInfo("ru")]);

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public void Set_UiLanguage_StoresTheCultureCookieForTheWholeApp(string culture)
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = CultureEndpoints.Set(culture, _languages, context);

        // Assert
        result.Should().BeOfType<NoContent>();
        var cookie = context.Response.Headers.SetCookie.ToString();
        cookie.Should().StartWith($".AspNetCore.Culture={Uri.EscapeDataString($"c={culture}|uic={culture}")};");
        cookie.Should().Contain("path=/").And.Contain("samesite=lax").And.Contain("httponly").And.Contain("max-age=31536000");
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh")]
    [InlineData("not a culture")]
    public void Set_NotAUiLanguage_IsABadRequestAndSetsNoCookie(string culture)
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = CultureEndpoints.Set(culture, _languages, context);

        // Assert
        result.Should().BeOfType<BadRequest>();
        context.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }
}
