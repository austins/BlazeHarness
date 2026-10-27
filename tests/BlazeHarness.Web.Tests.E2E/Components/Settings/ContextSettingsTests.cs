namespace BlazeHarness.Web.Tests.E2E.Components.Settings;

public sealed class ContextSettingsTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Toggle_TurnedOn_StaysOnWhenTheSectionIsOpenedAgain()
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        var toggle = Page.GetByLabel("Compact without asking the model");

        // Act
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Context" }).ClickAsync();
        await Expect(toggle).Not.ToBeCheckedAsync();
        await toggle.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Providers" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Context" }).ClickAsync();

        // Assert
        await Expect(toggle).ToBeCheckedAsync();
    }
}
