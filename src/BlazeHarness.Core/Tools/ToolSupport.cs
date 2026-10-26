using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Small helpers the built-in tools share.
/// </summary>
internal static class ToolSupport
{
    private const int BinarySampleBytes = 8192;

    /// <summary>
    /// Read a text property of a call's arguments for a summary.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The text, or an empty string when it is missing or not text.</returns>
    public static string Text(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    /// <summary>
    /// Get whether the start of a file looks like binary data rather than text.
    /// </summary>
    /// <remarks>
    /// A file with a Unicode byte order mark is text, even though UTF-16 and UTF-32 text contains zero bytes.
    /// </remarks>
    /// <param name="path">The full path of the file.</param>
    /// <returns>True when the file has a zero byte in its first 8 KiB and no byte order mark.</returns>
    public static bool LooksBinary(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[BinarySampleBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (HasUnicodeBom(buffer.AsSpan(0, read)))
        {
            return false;
        }

        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>
    /// Get whether bytes start with a UTF-8, UTF-16 or UTF-32 byte order mark.
    /// </summary>
    /// <param name="bytes">The start of a file.</param>
    /// <returns>True when a byte order mark is present.</returns>
    public static bool HasUnicodeBom(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
            || bytes.StartsWith(new byte[] { 0xFF, 0xFE })
            || bytes.StartsWith(new byte[] { 0xFE, 0xFF });
    }

    /// <summary>
    /// Describe a file or folder that a path points at, or the reason it can't be used for a file operation.
    /// </summary>
    /// <param name="context">The working folder.</param>
    /// <param name="full">The resolved path.</param>
    /// <returns>An error message when the path is a folder, otherwise null.</returns>
    public static string? FolderInsteadOfFile(ToolContext context, string full)
    {
        return Directory.Exists(full)
            ? $"'{context.Relative(full)}' is a folder. Use list_files to see what it contains."
            : null;
    }
}
