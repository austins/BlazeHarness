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
