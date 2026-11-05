using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Scrolls the page the browser has open. The page was already approved when it was opened, so scrolling needs no approval of its own.
/// </summary>
internal sealed class BrowserScrollTool : IWebTool
{
    internal const string ToolName = "browser_scroll";

    private readonly IBrowserEngine _engine;
    private readonly string _chatId;

    public BrowserScrollTool(IBrowserEngine engine, string chatId)
    {
        _engine = engine;
        _chatId = chatId;
    }

    public string Name => ToolName;

    public string Description =>
        "Scroll the page browser_navigate opened by one screen, or to its top or bottom, and return what it shows afterwards. " +
        "The page text already covers the whole page, so use this to make a page load more, such as a list that adds items as you reach its end, or to see the effect of scrolling.";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "direction": { "type": "string", "enum": ["down", "up", "top", "bottom"], "description": "Where to scroll: one screen down or up, or to the top or bottom of the page." }
          },
          "required": ["direction"]
        }
        """;

    public bool IsReadOnly => false;

    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "direction");

    /// <inheritdoc />
    /// <remarks>The page was opened by an approved browser_navigate; scrolling stays inside that page.</remarks>
    public WebRequest? Describe(JsonElement arguments) => null;

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Enum.TryParse<BrowserScrollDirection>(arguments.RequiredString("direction").Trim(), ignoreCase: true, out var direction) || !Enum.IsDefined(direction))
        {
            return ToolResult.Error("The direction is down, up, top or bottom.");
        }

        try
        {
            return ToolResult.Ok(BrowserPageRenderer.Render(await _engine.ScrollAsync(_chatId, direction, cancellationToken)));
        }
        catch (BrowserException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
