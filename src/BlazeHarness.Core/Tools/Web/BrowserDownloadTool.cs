using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Clicks an element of the page the browser has open that offers a file, and saves the file into the working folder.
/// </summary>
internal sealed class BrowserDownloadTool : ITool
{
    internal const string ToolName = "browser_download";

    private readonly IBrowserEngine _engine;
    private readonly string _chatId;

    public BrowserDownloadTool(IBrowserEngine engine, string chatId)
    {
        _engine = engine;
        _chatId = chatId;
    }

    public string Name => ToolName;

    public string Description =>
        "Click a download link or button of the page browser_navigate opened, by its ref, and save the file it offers to a path in the working folder. " +
        "An existing file is replaced. Files over 50 MB are not saved.";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "ref": { "type": "string", "description": "The ref of the link or button from the page text, such as e42." },
            "path": { "type": "string", "description": "The file to write, relative to the working folder." }
          },
          "required": ["ref", "path"]
        }
        """;

    public bool IsReadOnly => false;

    public IReadOnlyList<string> PathArguments { get; } = ["path"];

    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(90);

    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "path");

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.TryResolve(arguments.RequiredString("path"), out var full, out var error))
        {
            return ToolResult.Error(error);
        }

        if (ToolSupport.FolderInsteadOfFile(context, full) is { } folder)
        {
            return ToolResult.Error(folder);
        }

        try
        {
            var download = await _engine.DownloadAsync(_chatId, arguments.RequiredString("ref"), full, cancellationToken);
            return ToolResult.Ok($"Saved {download.FileName} as {context.Relative(full)} ({FileSize.Format(download.Bytes)}).");
        }
        catch (BrowserException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
