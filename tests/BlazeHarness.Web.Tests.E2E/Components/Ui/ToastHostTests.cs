namespace BlazeHarness.Web.Tests.E2E.Components.Ui;

public sealed class ToastHostTests(BrowserFixture browser) : TestBase(browser)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task CopyMessage_ToastShown_UsesCompactCenteredRowAndCanBeDismissed(int width)
    {
        // Arrange
        await Page.SetViewportSizeAsync(width, 844);
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await Page.AddInitScriptAsync(
            "Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.resolve() } });");
        await LaunchAsync();
        await Page.GetByPlaceholder("Message the model").FillAsync("Hi");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        var message = Page.Locator("[data-role=user]");
        await Expect(Page.Locator("[data-role=assistant]")).ToHaveAttributeAsync("data-status", "done");
        await message.HoverAsync();

        // Act
        await message.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Copy this message" }).ClickAsync();

        // Assert
        var toast = Page.Locator(".toast");
        await Expect(toast).ToBeVisibleAsync();
        var text = toast.Locator(".toast-message");
        await Expect(text).ToHaveTextAsync("Message copied.");
        await Expect(text).ToHaveCSSAsync("content-visibility", "visible");
        var dismiss = toast.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Dismiss", Exact = true });
        var textBox = (await text.BoundingBoxAsync())!;
        var dismissBox = (await dismiss.BoundingBoxAsync())!;
        var toastBox = (await toast.BoundingBoxAsync())!;
        var rootSize = await Page.Locator("html").EvaluateAsync<float>("element => parseFloat(getComputedStyle(element).fontSize)");
        (textBox.Y + (textBox.Height / 2)).Should().BeApproximately(dismissBox.Y + (dismissBox.Height / 2), 1);
        toastBox.Height.Should().BeLessThan(4 * rootSize);
        dismissBox.Width.Should().BeGreaterThanOrEqualTo(2 * rootSize);
        dismissBox.Height.Should().BeGreaterThanOrEqualTo(2 * rootSize);
        (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(width);

        await dismiss.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(toast).ToHaveCountAsync(0);
    }
}
