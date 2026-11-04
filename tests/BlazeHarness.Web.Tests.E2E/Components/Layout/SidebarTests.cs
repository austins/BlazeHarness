using BlazeHarness.Core.Folders;

namespace BlazeHarness.Web.Tests.E2E.Components.Layout;

public sealed class SidebarTests(BrowserFixture browser) : TestBase(browser)
{
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
