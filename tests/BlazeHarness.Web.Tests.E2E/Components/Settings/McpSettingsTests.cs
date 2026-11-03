using BlazeHarness.Core.Mcp.Config;

namespace BlazeHarness.Web.Tests.E2E.Components.Settings;

public sealed class McpSettingsTests(BrowserFixture browser) : TestBase(browser)
{
    [Fact]
    public async Task Server_Expanded_ShowsControlsAndKeepsTheEnabledSwitchVisible()
    {
        // Arrange
        var config = Factory.Services.GetRequiredService<McpConfigFile>();
        await config.SaveAsync(
            """{"mcpServers":{"example":{"url":"https://example.com/mcp","disabled":true}}}""",
            TestContext.Current.CancellationToken);
        await LaunchAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Settings", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "MCP servers", Exact = true }).ClickAsync();
        var server = Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "example", Exact = true });
        var edit = server.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit", Exact = true });

        // Act
        await Expect(edit).Not.ToBeVisibleAsync();
        await Expect(server.GetByLabel("On for new chats")).ToBeVisibleAsync();
        await server.Locator("summary").First.ClickAsync();

        // Assert
        await Expect(edit).ToBeVisibleAsync();
        await Expect(server.GetByLabel("On for new chats")).Not.ToBeCheckedAsync();
        await edit.ClickAsync();
        await Expect(server.GetByRole(AriaRole.Form, new LocatorGetByRoleOptions { Name = "Edit example", Exact = true })).ToBeVisibleAsync();
    }
}
