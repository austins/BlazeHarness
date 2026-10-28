namespace BlazeHarness.Web.Tests.E2E.Hosting;

public sealed class LaunchTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Root_Opened_ShowsTheChatComposer()
    {
        // Arrange & Act
        await LaunchAsync();

        // Assert
        await Expect(Page.GetByPlaceholder("Message the model")).ToBeVisibleAsync();
    }
}
