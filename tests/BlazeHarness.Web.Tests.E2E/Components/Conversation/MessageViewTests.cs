namespace BlazeHarness.Web.Tests.E2E.Components.Conversation;

public sealed class MessageViewTests(BrowserFixture browser) : TestBase(browser)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task UserMessage_HoverAndKeyboardFocus_ActionsStayBelowCompactBubble(int width)
    {
        // Arrange
        await Page.SetViewportSizeAsync(width, 844);
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await LaunchAsync();
        await Page.GetByPlaceholder("Message the model").FillAsync("Hi");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        var reply = Page.Locator("[data-role=assistant]");
        await Expect(reply).ToHaveAttributeAsync("data-status", "done");
        var message = Page.Locator("[data-role=user]");
        var bubble = message.Locator(".msg-content");
        var footer = message.Locator(".msg-meta");
        var copy = message.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Copy this message" });
        var edit = message.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit and resend this message" });
        await Expect(edit).ToHaveCountAsync(1);
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(copy).ToHaveCSSAsync("opacity", "0");
        var bubbleBox = (await bubble.BoundingBoxAsync())!;
        var bodyBox = (await bubble.Locator(".msg-body").BoundingBoxAsync())!;
        var footerBox = (await footer.BoundingBoxAsync())!;
        var messageBox = (await message.BoundingBoxAsync())!;
        var replyBox = (await reply.BoundingBoxAsync())!;
        var padding = await bubble.EvaluateAsync<float>(
            "element => { const style = getComputedStyle(element); return parseFloat(style.paddingTop) + parseFloat(style.paddingBottom); }");

        // Act
        await bubble.HoverAsync();

        // Assert
        await Expect(copy).ToHaveCSSAsync("opacity", "1");
        await Expect(edit).ToHaveCSSAsync("opacity", "1");
        await Expect(message).ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");
        footerBox.Y.Should().BeGreaterThanOrEqualTo(bubbleBox.Y + bubbleBox.Height);
        bubbleBox.Height.Should().BeApproximately(bodyBox.Height + padding, 1);
        bubbleBox.Width.Should().BeLessThan(footerBox.Width);
        (await message.BoundingBoxAsync()).Should().BeEquivalentTo(messageBox);
        (await reply.BoundingBoxAsync()).Should().BeEquivalentTo(replyBox);
        (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(width);

        await Page.GetByPlaceholder("Message the model").HoverAsync();
        await Expect(copy).ToHaveCSSAsync("opacity", "0");
        await copy.FocusAsync();
        await Expect(copy).ToHaveCSSAsync("opacity", "1");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(edit).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByPlaceholder("Message the model")).ToHaveValueAsync("Hi");
        await Expect(message).ToHaveAttributeAsync("data-editing", "true");
        await Expect(bubble).ToHaveCSSAsync("outline-style", "solid");
        await Expect(bubble).ToHaveCSSAsync("outline-offset", "-2px");
        var editingBox = (await bubble.BoundingBoxAsync())!;
        editingBox.Width.Should().Be(bubbleBox.Width);
        editingBox.Height.Should().Be(bubbleBox.Height);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(bubble).ToHaveCSSAsync("outline-style", "none");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task UserMessage_LongUnbrokenText_WrapsInsideTheBubble(int width)
    {
        // Arrange
        await Page.SetViewportSizeAsync(width, 844);
        await LaunchAsync();

        // Act
        await Page.GetByPlaceholder("Message the model").FillAsync(new string('x', 500));
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(Page.Locator("[data-role=assistant]")).ToHaveAttributeAsync("data-status", "done");

        // Assert
        var body = Page.Locator("[data-role=user] .msg-body");
        var overflow = await body.EvaluateAsync<int>("element => element.scrollWidth - element.clientWidth");
        overflow.Should().BeLessThanOrEqualTo(1);
        (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(width);
    }

    [Theory]
    [InlineData(1280, true)]
    [InlineData(390, true)]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    public async Task AssistantMessage_StatisticsOpened_ShowsDetailsWithoutMovingTheMessage(int width, bool hasSpeed)
    {
        // Arrange
        await Page.SetViewportSizeAsync(width, 844);
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await LaunchAsync();
        await Page.GetByPlaceholder("Message the model").FillAsync(hasSpeed ? "Timed statistics" : "Single-token statistics");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        var message = Page.Locator("[data-role=assistant]");
        await Expect(message).ToHaveAttributeAsync("data-status", "done");
        var statistics = message.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Message statistics" });
        var panel = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Message statistics" });
        await Page.GetByPlaceholder("Message the model").HoverAsync();
        await Expect(statistics).ToBeVisibleAsync();
        await Expect(statistics.Locator("svg")).ToHaveCountAsync(hasSpeed ? 0 : 1);
        await Expect(statistics).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("Prompt: 10 tokens, 3 from the cache"));
        await Expect(message.Locator(".msg-time")).ToBeVisibleAsync();
        await Expect(panel).Not.ToBeVisibleAsync();
        if (hasSpeed)
        {
            await Expect(statistics).ToContainTextAsync("t/s");
        }
        else
        {
            await Expect(statistics.Locator(".mono")).ToHaveCountAsync(0);
            await Expect(statistics).ToHaveAccessibleNameAsync("Message statistics");
        }

        var messageBox = (await message.BoundingBoxAsync())!;

        // Act
        await statistics.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        // Assert
        await Expect(panel).ToBeVisibleAsync();
        await Expect(panel).ToContainTextAsync("Prompt: 10 tokens, 3 from the cache");
        await Expect(panel).ToContainTextAsync(hasSpeed ? "Output: 12 tokens" : "Output: 1 token");
        await Expect(panel).ToContainTextAsync("First output after");
        if (hasSpeed)
        {
            await Expect(panel).ToContainTextAsync("Reasoning: 2 tokens");
            await Expect(panel).ToContainTextAsync("Output generated at");
            await Expect(panel).ToContainTextAsync("Output streamed in");
        }

        (await message.BoundingBoxAsync()).Should().BeEquivalentTo(messageBox);
        var panelBox = (await panel.BoundingBoxAsync())!;
        panelBox.X.Should().BeGreaterThanOrEqualTo(0);
        (panelBox.X + panelBox.Width).Should().BeLessThanOrEqualTo(width);
        (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(width);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).Not.ToBeVisibleAsync();
        await Expect(statistics).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(message.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Copy this message" })).ToBeFocusedAsync();
        await statistics.ClickAsync();
        await Expect(panel).ToBeVisibleAsync();
        await Page.GetByPlaceholder("Message the model").ClickAsync();
        await Expect(panel).Not.ToBeVisibleAsync();
    }

    protected override string[]? ChunksFor(string body)
    {
        var singleToken = body.Contains("Single-token statistics", StringComparison.Ordinal);
        if (!singleToken && !body.Contains("Timed statistics", StringComparison.Ordinal))
        {
            return base.ChunksFor(body);
        }

        var completionTokens = singleToken ? 1 : 12;
        var reasoningTokens = singleToken ? 0 : 2;
        return
        [
            Chunk("""{"content":"Hello"}""", null),
            Chunk("""{"content":" from the fake model."}""", null),
            Chunk("{}", "stop"),
            $$"""{"id":"1","object":"chat.completion.chunk","model":"fake-model","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":{{completionTokens}},"total_tokens":{{10 + completionTokens}},"prompt_tokens_details":{"cached_tokens":3},"completion_tokens_details":{"reasoning_tokens":{{reasoningTokens}} } } }""",
        ];
    }
}
