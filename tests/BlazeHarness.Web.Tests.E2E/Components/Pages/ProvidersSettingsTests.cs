namespace BlazeHarness.Web.Tests.E2E.Components.Pages;

public sealed class ProvidersSettingsTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Open_SeededProvider_IsListed()
    {
        // Arrange
        await LaunchAsync("/settings/providers");

        // Act
        var provider = Page.GetByLabel("Configured providers").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = ProviderName });

        // Assert
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Providers" })).ToBeVisibleAsync();
        await Expect(provider).ToBeVisibleAsync();
        await Expect(provider).ToContainTextAsync("1 model");
    }
}
