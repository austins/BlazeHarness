namespace BlazeHarness.Web.Tests.E2E.Components.Settings;

public sealed class ProvidersSettingsTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Open_SingleSeededProvider_OpensItsEditorWithoutAList()
    {
        // Arrange
        await LaunchAsync();

        // Act
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Providers" }).ClickAsync();

        // Assert
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Providers" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = ProviderName })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Configured providers")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Edit_NameChanged_ShowsSaveBarUntilDiscarded()
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Providers" }).ClickAsync();
        var name = Page.GetByLabel("Name", new PageGetByLabelOptions { Exact = true });
        var saveBar = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Unsaved changes" });
        await Expect(saveBar).ToHaveCountAsync(0);

        // Act
        await name.FillAsync("Renamed");
        await Expect(saveBar).ToBeVisibleAsync();
        await saveBar.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Discard" }).ClickAsync();

        // Assert
        await Expect(saveBar).ToHaveCountAsync(0);
        await Expect(name).ToHaveValueAsync(ProviderName);
    }
}
