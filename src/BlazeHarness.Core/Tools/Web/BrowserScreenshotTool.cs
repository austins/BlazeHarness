using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Saves a picture of the page the browser has open into the working folder and shows it to the user in the chat.
/// </summary>
/// <remarks>
/// The model does not see the picture; it reads the page through <c>browser_snapshot</c>.
/// </remarks>
public sealed class BrowserScreenshotTool : ITool
{
    /// <summary>
    /// The name the model calls the tool by.
    /// </summary>
    public const string ToolName = "browser_screenshot";

    /// <summary>
    /// The name of the argument that holds the path of the picture.
    /// </summary>
    public const string PathArgument = "path";

    private readonly IBrowserEngine _engine;
    private readonly string _chatId;

    internal BrowserScreenshotTool(IBrowserEngine engine, string chatId)
    {
        _engine = engine;
        _chatId = chatId;
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Save a PNG picture of the page browser_navigate opened to a file in the working folder; it also appears in the chat for the user. " +
        "You can't see the picture yourself: read the page with browser_snapshot.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The PNG file to write, relative to the working folder, such as screenshots/home.png." },
            "full_page": { "type": "boolean", "description": "Capture the whole page rather than what fits the window. Defaults to false." }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public IReadOnlyList<string> PathArguments { get; } = [PathArgument];

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, PathArgument);

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var path = arguments.RequiredString(PathArgument);
        if (!context.TryResolve(path, out var full, out var error))
        {
            return ToolResult.Error(error);
        }

        if (ToolSupport.FolderInsteadOfFile(context, full) is { } folder)
        {
            return ToolResult.Error(folder);
        }

        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResult.Error("The picture is a PNG, so the file name has to end in .png.");
        }

        try
        {
            var png = await _engine.ScreenshotAsync(_chatId, arguments.OptionalBool("full_page"), cancellationToken);
            AtomicFile.Write(full, png);
            return ToolResult.Ok($"Saved {context.Relative(full)} ({FileSize.Format(png.Length)}) and showed it to the user.");
        }
        catch (BrowserException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
