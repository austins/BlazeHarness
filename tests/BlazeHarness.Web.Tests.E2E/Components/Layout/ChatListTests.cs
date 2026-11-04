using BlazeHarness.Core.Chats;

namespace BlazeHarness.Web.Tests.E2E.Components.Layout;

public sealed class ChatListTests(BrowserFixture browser) : TestBase(browser)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextMenu_RightButtonReleased_RemainsOpenAndRenameWorks(bool holdUntilRendered)
    {
        // Arrange
        await Factory.Services.GetRequiredService<ChatService>().CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        await LaunchAsync();
        var chat = Page.Locator(".chat-link");
        var menu = Page.Locator("[id^=chat-menu-]");
        await chat.HoverAsync();

        // Act
        await Page.Mouse.DownAsync(new MouseDownOptions { Button = MouseButton.Right });
        if (holdUntilRendered)
        {
            await chat.DispatchEventAsync("contextmenu", new { button = 2, buttons = 2 });
            await Expect(menu).ToContainTextAsync("Rename");
        }

        await Page.Mouse.UpAsync(new MouseUpOptions { Button = MouseButton.Right });

        // Assert
        await Expect(menu).ToBeVisibleAsync();
        await menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Rename", Exact = true }).ClickAsync();
        var input = Page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Chat name" });
        await Expect(input).ToBeFocusedAsync();
        await input.FillAsync("Renamed chat");
        await input.PressAsync("Enter");
        await Expect(chat).ToContainTextAsync("Renamed chat");
    }

    [Fact]
    public async Task ContextMenu_WithoutPressedButtons_FocusesFirstItemAndEscapeCloses()
    {
        // Arrange
        await Factory.Services.GetRequiredService<ChatService>().CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        await LaunchAsync();
        await Page.Locator(".chat-link").FocusAsync();
        var menu = Page.Locator("[id^=chat-menu-]");

        // Act
        await Page.Locator(".chat-link").DispatchEventAsync("contextmenu", new { button = 0, buttons = 0 });

        // Assert
        await Expect(menu).ToBeVisibleAsync();
        await Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Rename", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).Not.ToBeVisibleAsync();
    }
}
