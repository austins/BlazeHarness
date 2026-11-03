using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Types into a field of the page the browser has open. The page was already approved when it was opened, so typing needs no approval of its own.
/// </summary>
internal sealed class BrowserTypeTool : IWebTool
{
    internal const string ToolName = "browser_type";

    private readonly IBrowserEngine _engine;
    private readonly string _chatId;

    public BrowserTypeTool(IBrowserEngine engine, string chatId)
    {
        _engine = engine;
        _chatId = chatId;
    }

    public string Name => ToolName;

    public string Description =>
        "Type text into a text field or text area of the page browser_navigate opened, by the ref the page text shows, replacing what the field holds. " +
        "For a drop-down list, give the option's text or value instead. Set submit to press Enter afterwards, for example to send a form. " +
        "Returns what the page shows afterwards. Use browser_click for buttons, checkboxes and links.";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "ref": { "type": "string", "description": "The field's ref from the page text, such as e42." },
            "text": { "type": "string", "description": "The text to type, or the option to pick. Empty clears the field." },
            "submit": { "type": "boolean", "description": "Press Enter afterwards. Defaults to false." }
          },
          "required": ["ref", "text"]
        }
        """;

    public bool IsReadOnly => false;

    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "ref");

    // The page was opened by an approved browser_navigate; what typing does stays inside that page.
    public WebRequest? Describe(JsonElement arguments) => null;

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            var view = await _engine.TypeAsync(
                _chatId,
                arguments.RequiredString("ref"),
                arguments.OptionalString("text") ?? string.Empty,
                arguments.OptionalBool("submit"),
                cancellationToken);
            return ToolResult.Ok(BrowserPageRenderer.Render(view));
        }
        catch (BrowserException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
