using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Mcp.Config;
using BlazeHarness.Core.Skills;

namespace BlazeHarness.Web.Tests.E2E.Components.Conversation;

public sealed class ChatViewTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task NewChat_Pickers_KeepChoicesAfterTheFirstMessage()
    {
        // Arrange
        var config = Factory.Services.GetRequiredService<McpConfigFile>();
        await config.SaveAsync(
            """{"mcpServers":{"example":{"url":"https://example.com/mcp","disabled":true}}}""",
            TestContext.Current.CancellationToken);
        var skillFolder = ProjectFolder.Combine(".agents", "skills", "example-skill");
        Directory.CreateDirectory(skillFolder);
        await File.WriteAllTextAsync(
            Path.Combine(skillFolder, "SKILL.md"),
            "---\nname: example-skill\ndescription: A test skill\n---\nTest instructions.",
            TestContext.Current.CancellationToken);
        Factory.Services.GetRequiredService<SkillCatalog>().Refresh();
        await Factory.Services.GetRequiredService<FolderRegistry>().OpenAsync(ProjectFolder.Root, TestContext.Current.CancellationToken);
        await LaunchAsync("/?folder=" + Uri.EscapeDataString(ProjectFolder.Root));

        // Act
        await Page.GetByTitle("MCP servers whose tools this chat can use").ClickAsync();
        await Expect(Page.GetByLabel("example", new PageGetByLabelOptions { Exact = true })).Not.ToBeCheckedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.GetByTitle("Skills this chat can load").ClickAsync();
        await Page.GetByLabel("example-skill", new PageGetByLabelOptions { Exact = true }).UncheckAsync();
        await Page.Keyboard.PressAsync("Escape");
        await SendAsync("Say hello");
        await Expect(Page.Locator("[data-role=assistant]")).ToContainTextAsync("Hello from the fake model.");
        await Page.ReloadAsync();
        await Page.GetByTitle("Skills this chat can load").ClickAsync();

        // Assert
        await Expect(Page.GetByLabel("example-skill", new PageGetByLabelOptions { Exact = true })).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task Send_Message_ShowsTheStreamedReply()
    {
        // Arrange
        await LaunchAsync();

        // Act
        await SendAsync("Say hello");

        // Assert
        await Expect(Page.Locator("[data-role=user]")).ToContainTextAsync("Say hello");
        await Expect(Page.Locator("[data-role=assistant]")).ToContainTextAsync("Hello from the fake model.");
        await Expect(Page.Locator("[data-role=assistant]")).ToHaveAttributeAsync("data-status", "done");
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    [InlineData(1280, true)]
    [InlineData(390, true)]
    public async Task ChatPickers_GloballyDisabledItemEnabled_ShowsChatOverride(int width, bool existingChat)
    {
        // Arrange
        const string hint = "Disabled globally; enabled for this chat";
        var config = Factory.Services.GetRequiredService<McpConfigFile>();
        await config.SaveAsync(
            """{"mcpServers":{"example":{"url":"https://example.com/mcp","disabled":true}}}""",
            TestContext.Current.CancellationToken);
        var skillFolder = ProjectFolder.Combine(".agents", "skills", "example-skill");
        Directory.CreateDirectory(skillFolder);
        await File.WriteAllTextAsync(
            Path.Combine(skillFolder, "SKILL.md"),
            "---\nname: example-skill\ndescription: A test skill\n---\nTest instructions.",
            TestContext.Current.CancellationToken);
        var catalog = Factory.Services.GetRequiredService<SkillCatalog>();
        await catalog.SetDisabledAsync("example-skill", true, TestContext.Current.CancellationToken);
        await Factory.Services.GetRequiredService<FolderRegistry>().OpenAsync(ProjectFolder.Root, TestContext.Current.CancellationToken);
        await Page.SetViewportSizeAsync(width, 844);
        await LaunchAsync("/?folder=" + Uri.EscapeDataString(ProjectFolder.Root));
        if (existingChat)
        {
            await SendAsync("Say hello");
            await Expect(Page.Locator("[data-role=assistant]")).ToHaveAttributeAsync("data-status", "done");
        }

        var pickers = new[]
        {
            (Title: "MCP servers whose tools this chat can use", Label: "MCP servers", Name: "example"),
            (Title: "Skills this chat can load", Label: "Skills", Name: "example-skill"),
        };

        foreach (var picker in pickers)
        {
            await Page.GetByTitle(picker.Title).ClickAsync();
            var panel = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = picker.Label, Exact = true });
            var row = panel.Locator("label").Filter(new LocatorFilterOptions
            {
                Has = Page.GetByText(picker.Name, new PageGetByTextOptions { Exact = true }),
            });
            var checkbox = row.GetByRole(AriaRole.Checkbox);
            var note = row.GetByText(hint, new LocatorGetByTextOptions { Exact = true });
            await Expect(checkbox).Not.ToBeCheckedAsync();
            await Expect(note).ToHaveCountAsync(0);

            // Act
            await checkbox.CheckAsync();

            // Assert
            await Expect(checkbox).ToBeCheckedAsync();
            await Expect(note).ToBeVisibleAsync();
            var noteBox = (await note.BoundingBoxAsync())!;
            noteBox.X.Should().BeGreaterThanOrEqualTo(0);
            (noteBox.X + noteBox.Width).Should().BeLessThanOrEqualTo(width);
            (await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).Should().BeLessThanOrEqualTo(width);
            await checkbox.UncheckAsync();
            await Expect(note).ToHaveCountAsync(0);
            await checkbox.CheckAsync();
            await Expect(note).ToBeVisibleAsync();
            await Page.Keyboard.PressAsync("Escape");
        }

        config.Servers.Single().Disabled.Should().BeTrue();
        catalog.IsDisabledGlobally("example-skill").Should().BeTrue();
        if (existingChat)
        {
            await Page.ReloadAsync();
            foreach (var picker in pickers)
            {
                await Page.GetByTitle(picker.Title).ClickAsync();
                await Expect(Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = picker.Label, Exact = true }).GetByText(hint)).ToBeVisibleAsync();
                await Page.Keyboard.PressAsync("Escape");
            }
        }

        await config.SetDisabledAsync("example", false, TestContext.Current.CancellationToken);
        await catalog.SetDisabledAsync("example-skill", false, TestContext.Current.CancellationToken);
        foreach (var picker in pickers)
        {
            await Page.GetByTitle(picker.Title).ClickAsync();
            var panel = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = picker.Label, Exact = true });
            await Expect(panel.GetByText(hint)).ToHaveCountAsync(0);
            await Expect(panel.GetByRole(AriaRole.Checkbox, new LocatorGetByRoleOptions { Name = picker.Name, Exact = true })).ToBeCheckedAsync();
            await Page.Keyboard.PressAsync("Escape");
        }
    }

    [Fact]
    public async Task FindShortcut_CtrlF_OpensTheFindBar()
    {
        // Arrange
        await LaunchAsync();
        await SendAsync("Say hello");
        await Expect(Page.Locator("[data-role=assistant]")).ToContainTextAsync("Hello from the fake model.");

        // Act
        await Page.Keyboard.PressAsync("Control+f");

        // Assert
        await Expect(Page.GetByRole(AriaRole.Search, new PageGetByRoleOptions { Name = "Find in this chat" })).ToBeVisibleAsync();
        await Expect(Page.GetByPlaceholder("Find in this chat")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task AttachFilesButton_Click_OpensTheFileChooser()
    {
        // Arrange
        await LaunchAsync();

        // Act
        var chooser = await Page.RunAndWaitForFileChooserAsync(
            () => Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Attach files" }).ClickAsync());

        // Assert
        chooser.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolApproval_Deny_LeavesTheFileUnwrittenAndTheChatContinues()
    {
        // Arrange
        await Factory.Services.GetRequiredService<FolderRegistry>().OpenAsync(ProjectFolder.Root, TestContext.Current.CancellationToken);
        await LaunchAsync("/?folder=" + Uri.EscapeDataString(ProjectFolder.Root));

        // Act
        await SendAsync("Write a file");
        await Expect(Page.Locator(".tool-approval")).ToBeVisibleAsync();
        await Page.Locator(".tool-approval").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Deny" }).ClickAsync();

        // Assert
        await Expect(Page.Locator("[data-role=assistant]").Last).ToContainTextAsync("The file was not written.");
        await Expect(Page.Locator(".tool-approval")).ToHaveCountAsync(0);
        File.Exists(ProjectFolder.Combine("note.txt")).Should().BeFalse();
    }

    protected override string[]? ChunksFor(string body)
    {
        if (body.Contains("\"role\":\"tool\"", StringComparison.Ordinal))
        {
            return [Chunk("""{"content":"The file was not written."}""", null)];
        }

        if (body.Contains("\"tools\"", StringComparison.Ordinal) && body.Contains("Write a file", StringComparison.Ordinal))
        {
            return
            [
                Chunk("""{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"write_file","arguments":"{\"path\":\"note.txt\",\"content\":\"hi\"}"}}]}""", null),
                Chunk("{}", "tool_calls"),
            ];
        }

        return base.ChunksFor(body);
    }

    private async Task SendAsync(string text)
    {
        await Page.GetByPlaceholder("Message the model").FillAsync(text);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
    }
}
