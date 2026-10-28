using BlazeHarness.Core.Web;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class WebSettingsTests
{
    [Fact]
    public void ResolveProviders_Default_AllKnownEnabledInOrder()
    {
        // Act
        var result = new WebSettings().ResolveProviders();

        // Assert
        result.Select(p => p.Id).Should().Equal(WebProviders.All.Select(i => i.Id));
        result.Should().OnlyContain(p => p.Enabled);
    }

    [Fact]
    public void ResolveProviders_SavedOrderWithUnknownAndMissing_KeepsSavedAndAppendsNew()
    {
        // Arrange
        var sut = new WebSettings { Providers = [new WebProviderSetting(WebProviders.Exa, false), new WebProviderSetting("gone"), new WebProviderSetting(WebProviders.Exa)] };

        // Act
        var result = sut.ResolveProviders();

        // Assert
        result[0].Should().Be(new WebProviderSetting(WebProviders.Exa, false));
        result.Should().NotContain(p => p.Id == "gone");
        result.Should().HaveCount(WebProviders.All.Count);
    }

    [Fact]
    public void SearchMode_Default_IsParallel()
    {
        // Act
        var mode = new WebSettings().SearchMode;

        // Assert
        mode.Should().Be(WebSearchMode.Parallel);
    }
}
