namespace BlazeHarness.Web.Tests.E2E.Hosting;

public sealed class LaunchTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Launch_ValidToken_ShowsTheChatComposer()
    {
        // Arrange & Act
        await LaunchAsync();

        // Assert
        await Expect(Page.GetByPlaceholder("Message the model")).ToBeVisibleAsync();
        Page.Url.Should().NotContain("token=");
    }

    [Fact]
    public async Task Root_WithoutLaunch_IsUnauthorized()
    {
        // Act
        var response = await Page.GotoAsync("/");

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(401);
    }

    [Fact]
    public async Task Launch_ReusedToken_IsUnauthorized()
    {
        // Arrange
        await LaunchAsync();
        var token = Factory.Services.GetRequiredService<LaunchCredentials>().Token;

        // Act
        var response = await Page.GotoAsync($"/launch?token={token}");

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(401);
    }
}
