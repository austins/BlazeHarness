using System.Globalization;

namespace BlazeHarness.Web.Tests.E2E.Components.Settings;

public sealed class GeneralSettingsTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task LanguagePicker_Default_ListsEnglishFirstThenTranslatedLanguages()
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "General" }).ClickAsync();

        // Act
        var language = Page.GetByLabel("Display language");

        // Assert
        await Expect(language).ToHaveValueAsync("en-US");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en-US");
        var cultures = await language.Locator("option").EvaluateAllAsync<string[]>("options => options.map(option => option.value)");
        cultures.Should().Equal("en-US", "ru", "zh-Hans");
        await Expect(language.Locator("option")).ToHaveTextAsync(["English", "Русский", CultureInfo.GetCultureInfo("zh-Hans").NativeName]);
    }

    [Theory]
    [InlineData("ru", "Настройки", "Язык интерфейса")]
    [InlineData("zh-Hans", "设置", "显示语言")]
    public async Task LanguagePicker_LanguageChanged_StoresTheCultureAndKeepsSettingsOpen(
        string culture, string settingsTitle, string languageLabel)
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "General" }).ClickAsync();

        // Act
        await Page.GetByLabel("Display language").SelectOptionAsync(culture);

        // Assert
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", culture);
        await Expect(Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = settingsTitle })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel(languageLabel)).ToHaveValueAsync(culture);
        var cultureCookie = (await Page.Context.CookiesAsync())
            .SingleOrDefault(cookie => cookie.Name == ".AspNetCore.Culture");
        cultureCookie.Should().NotBeNull();
        Uri.UnescapeDataString(cultureCookie.Value).Should().Be($"c={culture}|uic={culture}");

        await Page.GetByLabel(languageLabel).SelectOptionAsync("en-US");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en-US");
        await Expect(Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Settings" })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Display language")).ToHaveValueAsync("en-US");

        await Page.ReloadAsync();
        await Expect(Page.Locator(".app")).ToHaveAttributeAsync("data-circuit", "ready");
        await Expect(Page.GetByRole(AriaRole.Dialog)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Toggle_TurnedOn_StaysOnWhenTheSectionIsOpenedAgain()
    {
        // Arrange
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings" }).ClickAsync();
        var toggle = Page.GetByLabel("Compact without the model (Extraction)");

        // Act
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "General" }).ClickAsync();
        await Expect(toggle).Not.ToBeCheckedAsync();
        await toggle.CheckAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Providers" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "General" }).ClickAsync();

        // Assert
        await Expect(toggle).ToBeCheckedAsync();
    }

    [Fact]
    public async Task ChatWidth_Toggled_ExpandsTheChatAndPersistsAcrossReloads()
    {
        // Arrange
        await Page.SetViewportSizeAsync(1600, 900);
        await LaunchAsync();
        await Page.GetByPlaceholder("Message the model").FillAsync("Make a plan");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        var chat = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Conversation" });
        var thread = chat.Locator(".chat-thread");
        var composer = chat.Locator(".composer");
        var composerBox = chat.Locator(".composer-box");
        var todo = chat.GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = "To-do list" });
        var reply = chat.Locator("[data-role=assistant]").Last;
        await Expect(todo).ToBeVisibleAsync();
        await Expect(reply).ToContainTextAsync("Hello from the fake model.");
        await Expect(reply).ToHaveAttributeAsync("data-status", "done");
        var chatWidth = (await chat.BoundingBoxAsync())!.Width;
        var defaultThreadWidth = (await thread.BoundingBoxAsync())!.Width;
        var defaultComposerWidth = (await composer.BoundingBoxAsync())!.Width;
        var defaultTodo = (await todo.BoundingBoxAsync())!;
        var defaultComposerBox = (await composerBox.BoundingBoxAsync())!;
        defaultThreadWidth.Should().BeLessThan(chatWidth - 100);
        defaultComposerWidth.Should().BeApproximately(defaultThreadWidth, 1);
        defaultTodo.Width.Should().BeApproximately(defaultComposerBox.Width, 1);
        defaultTodo.X.Should().BeApproximately(defaultComposerBox.X, 1);

        // Act
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings", Exact = true }).ClickAsync();
        var toggle = Page.GetByLabel("Use full width for chats");
        await Expect(toggle).Not.ToBeCheckedAsync();
        await toggle.CheckAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-chat-full-width", string.Empty);

        // Assert
        var fullThreadWidth = (await thread.BoundingBoxAsync())!.Width;
        var fullComposerWidth = (await composer.BoundingBoxAsync())!.Width;
        var fullTodo = (await todo.BoundingBoxAsync())!;
        var fullComposerBox = (await composerBox.BoundingBoxAsync())!;
        fullThreadWidth.Should().BeApproximately(chatWidth, 1);
        fullComposerWidth.Should().BeApproximately(chatWidth, 1);
        fullTodo.Width.Should().BeGreaterThan(defaultTodo.Width + 100);
        fullTodo.Width.Should().BeApproximately(fullComposerBox.Width, 1);
        fullTodo.X.Should().BeApproximately(fullComposerBox.X, 1);

        await Page.ReloadAsync();
        await Expect(Page.Locator(".app")).ToHaveAttributeAsync("data-circuit", "ready");
        await Expect(todo).ToBeVisibleAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-chat-full-width", string.Empty);
        (await thread.BoundingBoxAsync())!.Width.Should().BeApproximately(chatWidth, 1);
        (await composer.BoundingBoxAsync())!.Width.Should().BeApproximately(chatWidth, 1);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings", Exact = true }).ClickAsync();
        await Expect(toggle).ToBeCheckedAsync();
        await toggle.UncheckAsync();
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync("data-chat-full-width", string.Empty);
        (await thread.BoundingBoxAsync())!.Width.Should().BeApproximately(defaultThreadWidth, 1);
        (await composer.BoundingBoxAsync())!.Width.Should().BeApproximately(defaultComposerWidth, 1);
        (await todo.BoundingBoxAsync())!.Width.Should().BeApproximately(defaultTodo.Width, 1);

        await Page.ReloadAsync();
        await Expect(Page.Locator(".app")).ToHaveAttributeAsync("data-circuit", "ready");
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync("data-chat-full-width", string.Empty);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings", Exact = true }).ClickAsync();
        await Expect(toggle).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task ChatWidth_OnNarrowScreen_FitsTheAvailableSpace()
    {
        // Arrange
        await Page.SetViewportSizeAsync(390, 844);
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show sidebar" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings", Exact = true }).ClickAsync();

        // Act
        await Page.GetByLabel("Use full width for chats").CheckAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-chat-full-width", string.Empty);
        await Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Settings" })
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();

        // Assert
        var chat = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Conversation" });
        var chatWidth = (await chat.BoundingBoxAsync())!.Width;
        (await chat.Locator(".chat-thread").BoundingBoxAsync())!.Width.Should().BeApproximately(chatWidth, 1);
        (await chat.Locator(".composer").BoundingBoxAsync())!.Width.Should().BeApproximately(chatWidth, 1);
        (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(390);
    }

    protected override string[]? ChunksFor(string body)
    {
        if (body.Contains("\"tools\"", StringComparison.Ordinal)
            && body.Contains("Make a plan", StringComparison.Ordinal)
            && !body.Contains("\"role\":\"tool\"", StringComparison.Ordinal))
        {
            return
            [
                Chunk("""{"tool_calls":[{"index":0,"id":"todo-1","type":"function","function":{"name":"update_todos","arguments":"{\"items\":[{\"text\":\"First step\",\"status\":\"in_progress\"}]}"}}]}""", null),
                Chunk("{}", "tool_calls"),
            ];
        }

        return base.ChunksFor(body);
    }
}
