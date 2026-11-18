using BlazeHarness.Core.Folders;

namespace BlazeHarness.Web.Tests.E2E.Components.Layout;

public sealed class SidebarTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Backdrop_NarrowScreenDrawerOpen_ShowsAndClickClosesTheDrawer()
    {
        // Arrange
        await Page.SetViewportSizeAsync(390, 844);
        await LaunchAsync();
        var sidebar = Page.Locator(".sidebar");
        var backdrop = Page.Locator(".sidebar-backdrop");
        await Expect(sidebar).ToHaveAttributeAsync("data-collapsed", "true");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show sidebar" }).ClickAsync();

        // Assert
        await Expect(backdrop).ToBeVisibleAsync();

        // Act: the drawer covers the left of the screen, so click the backdrop to its right.
        await backdrop.ClickAsync(new LocatorClickOptions { Position = new Position { X = 370, Y = 400 } });

        // Assert
        await Expect(sidebar).ToHaveAttributeAsync("data-collapsed", "true");
        await Expect(backdrop).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task FolderContextMenu_RightButtonReleased_StaysOpenUntilClickedOutside()
    {
        // Arrange
        await Factory.Services.GetRequiredService<FolderRegistry>().OpenAsync(ProjectFolder.Root, TestContext.Current.CancellationToken);
        await LaunchAsync();
        var menu = Page.Locator("[id^=folder-menu-]");
        await Page.Locator(".folder-head").HoverAsync();

        // Act
        await Page.Mouse.DownAsync(new MouseDownOptions { Button = MouseButton.Right });
        await Page.Mouse.UpAsync(new MouseUpOptions { Button = MouseButton.Right });

        // Assert
        await Expect(menu).ToBeVisibleAsync();
        await Expect(menu).ToContainTextAsync("New chat");
        await Page.GetByPlaceholder("Message the model").ClickAsync();
        await Expect(menu).Not.ToBeVisibleAsync();
    }
}
