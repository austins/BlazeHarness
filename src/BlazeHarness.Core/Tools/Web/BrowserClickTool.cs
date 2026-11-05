using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Clicks an element of the page the browser has open. The page was already approved when it was opened, so a click needs no approval of its own.
/// </summary>
internal sealed class BrowserClickTool : IWebTool
{
    internal const string ToolName = "browser_click";

    private readonly IBrowserEngine _engine;
    private readonly string _chatId;

    public BrowserClickTool(IBrowserEngine engine, string chatId)
    {
        _engine = engine;
        _chatId = chatId;
    }

    public string Name => ToolName;

    public string Description =>
        "Click a button, link, checkbox or other element of the page browser_navigate opened, by the ref the page text shows, such as e42. " +
        "Returns what the page shows afterwards. Refs belong to the page as last read; read it again with browser_snapshot after it changes a lot.";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "ref": { "type": "string", "description": "The element's ref from the page text, such as e42." }
          },
          "required": ["ref"]
        }
        """;

    public bool IsReadOnly => false;

    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "ref");

    /// <inheritdoc />
    /// <remarks>The page was opened by an approved browser_navigate; what a click does stays inside that page.</remarks>
    public WebRequest? Describe(JsonElement arguments) => null;

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            return ToolResult.Ok(BrowserPageRenderer.Render(await _engine.ClickAsync(_chatId, arguments.RequiredString("ref"), cancellationToken)));
        }
        catch (BrowserException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
