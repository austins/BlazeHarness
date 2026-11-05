using System.Text.Json;

namespace BlazeHarness.Web.Tests.E2E.Components.Conversation;

public sealed class QuestionCardTests(BrowserFixture browser) : TestBase(browser)
{
    private string? _toolRequest;

    [Fact]
    public async Task SingleChoice_ClickingThePickedOptionAgain_UnpicksItAndAllowsANoteOnlyAnswer()
    {
        // Arrange
        await LaunchAsync();
        await AskAsync("Ask single");
        var card = Page.Locator(".question[data-state=waiting]");
        var alpha = card.GetByRole(AriaRole.Radio).First;
        var beta = card.GetByRole(AriaRole.Radio).Nth(1);
        var send = card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Send answer" });

        // Act
        await alpha.ClickAsync();
        await Expect(alpha).ToBeCheckedAsync();
        await beta.ClickAsync();
        await Expect(beta).ToBeCheckedAsync();
        await Expect(alpha).Not.ToBeCheckedAsync();
        await beta.ClickAsync();

        // Assert
        await Expect(beta).Not.ToBeCheckedAsync();
        await Expect(send).ToBeDisabledAsync();
        await card.GetByRole(AriaRole.Textbox).FillAsync("just a note");
        await send.ClickAsync();
        await Expect(Page.Locator("[data-role=assistant]").Last).ToContainTextAsync("Thanks.");
        await Expect(Page.Locator("[data-role=assistant]").Last).ToHaveAttributeAsync("data-status", "done");
        _toolRequest.Should().Contain("The user answered: just a note").And.NotContain("The user chose");
    }

    [Fact]
    public async Task MultiSelect_ClickingAPickedOption_StillTogglesIt()
    {
        // Arrange
        await LaunchAsync();
        await AskAsync("Ask multi");
        var card = Page.Locator(".question[data-state=waiting]");

        // Act
        var alpha = card.GetByRole(AriaRole.Checkbox).First;
        await alpha.ClickAsync();
        await Expect(alpha).ToBeCheckedAsync();
        await alpha.ClickAsync();

        // Assert
        await Expect(alpha).Not.ToBeCheckedAsync();
    }

    protected override string[]? ChunksFor(string body)
    {
        if (body.Contains("\"role\":\"tool\"", StringComparison.Ordinal))
        {
            _toolRequest = body;
            return [Chunk("""{"content":"Thanks."}""", null)];
        }

        if (body.Contains("\"tools\"", StringComparison.Ordinal))
        {
            var multi = body.Contains("Ask multi", StringComparison.Ordinal);
            var arguments = JsonSerializer.Serialize(new { question = "Which one?", options = new[] { new { label = "Alpha" }, new { label = "Beta" } }, multi_select = multi });
            var delta = JsonSerializer.Serialize(new { tool_calls = new[] { new { index = 0, id = "call_1", type = "function", function = new { name = "ask_user", arguments } } } });
            return [Chunk(delta, null), Chunk("{}", "tool_calls")];
        }

        return base.ChunksFor(body);
    }

    private async Task AskAsync(string text)
    {
        await Page.GetByPlaceholder("Message the model").FillAsync(text);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        await Expect(Page.Locator(".question[data-state=waiting]")).ToBeVisibleAsync();
    }
}
