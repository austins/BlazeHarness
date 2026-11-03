namespace BlazeHarness.Web.Tests.E2E.Components.Conversation;

public sealed class ChatStatsTests(BrowserFixture browser) : TestBase(browser)
{
    [Theory]
    [InlineData("One token", "1 prompt token was served from the provider's cache")]
    [InlineData("Two tokens", "2 prompt tokens were served from the provider's cache")]
    public async Task CachedTokens_Count_ShowsTheMatchingTooltip(string message, string expected)
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByPlaceholder("Message the model").FillAsync(message);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(Page.Locator("[data-role=assistant]")).ToHaveAttributeAsync("data-status", "done");

        // Act
        await Page.Locator(".context-trigger").ClickAsync();

        // Assert
        await Expect(Page.GetByTitle(expected, new PageGetByTitleOptions { Exact = true })).ToBeVisibleAsync();
    }

    protected override string[]? ChunksFor(string body)
    {
        var cached = body.Contains("Two tokens", StringComparison.Ordinal) ? 2 : 1;
        return
        [
            Chunk("""{"content":"Hello from the fake model."}""", null),
            Chunk("{}", "stop"),
            """{"id":"1","object":"chat.completion.chunk","model":"fake-model","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":2,"total_tokens":12,"prompt_tokens_details":{"cached_tokens":""" + cached + "}}}",
        ];
    }
}
