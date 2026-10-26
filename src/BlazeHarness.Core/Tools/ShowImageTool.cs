using BlazeHarness.Core.Attachments;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Shows an image from the open folder to the user in the chat.
/// </summary>
/// <remarks>
/// The chat itself loads the file through an endpoint that checks the path again, so this tool only has to confirm
/// that the file can be shown. The model does not see the image.
/// </remarks>
public sealed class ShowImageTool : ITool
{
    /// <inheritdoc />
    public string Name => "show_image";

    /// <inheritdoc />
    public string Description =>
        "Show an image file from the open folder to the user in the chat, for example a chart or screenshot a script wrote. " +
        "Supports PNG, JPEG, GIF, WebP, AVIF and SVG up to 10 MB. You can't see the image yourself through this tool.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The image file, relative to the open folder." }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public IReadOnlyList<string> PathArguments { get; } = ["path"];

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "path");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var path = arguments.RequiredString("path");
        return Task.FromResult(Check(context, path));
    }

    /// <summary>
    /// Find an image in a folder that the chat may show.
    /// </summary>
    /// <param name="context">The folder the path must stay inside.</param>
    /// <param name="path">The path as given, relative to the folder.</param>
    /// <param name="fullPath">The resolved path when the image can be shown.</param>
    /// <param name="mediaType">The media type of the image when it can be shown.</param>
    /// <param name="error">Why the image can't be shown, or an empty string.</param>
    /// <returns>True when the file is an image of a supported type, inside the folder and not over the size cap.</returns>
    public static bool TryFind(ToolContext context, string? path, out string fullPath, out string mediaType, out string error)
    {
        ArgumentNullException.ThrowIfNull(context);

        mediaType = string.Empty;
        if (!context.TryResolve(path, out fullPath, out error))
        {
            return false;
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            error = $"There is no file at '{path}'.";
            return false;
        }

        if (file.Length > ImageFiles.MaxDisplayBytes)
        {
            error = $"'{path}' is larger than {ImageFiles.MaxDisplayBytes / (1024 * 1024)} MB.";
            return false;
        }

        if (ImageFiles.DetectDisplayable(fullPath) is not { } detected)
        {
            error = $"'{path}' is not a PNG, JPEG, GIF, WebP, AVIF or SVG image.";
            return false;
        }

        mediaType = detected;
        error = string.Empty;
        return true;
    }

    private static ToolResult Check(ToolContext context, string path)
    {
        if (!TryFind(context, path, out var fullPath, out var mediaType, out var error))
        {
            return ToolResult.Error(error);
        }

        var size = new FileInfo(fullPath).Length;
        return ToolResult.Ok(string.Create(
            CultureInfo.InvariantCulture,
            $"Showing {context.Relative(fullPath)} ({mediaType}, {FileSize.Format(size)}) to the user."));
    }
}
