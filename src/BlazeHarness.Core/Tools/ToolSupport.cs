using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Small helpers the built-in tools share.
/// </summary>
internal static class ToolSupport
{
    private const int BinarySampleBytes = 8192;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> Utf16LittleEndianBom => [0xFF, 0xFE];

    private static ReadOnlySpan<byte> Utf16BigEndianBom => [0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf32BigEndianBom => [0x00, 0x00, 0xFE, 0xFF];

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
    /// Get whether a file has no content, as far as a tool can tell without opening it.
    /// </summary>
    /// <remarks>
    /// A named pipe or device reports a length of zero, and opening a pipe blocks until something writes to it, which can be never.
    /// So a file of length zero is never opened.
    /// </remarks>
    /// <param name="path">The full path of the file.</param>
    /// <returns>True when the file reports a length of zero.</returns>
    public static bool IsEmpty(string path) => new FileInfo(path).Length == 0;

    /// <summary>
    /// Get whether the start of a file looks like binary data rather than text.
    /// </summary>
    /// <remarks>
    /// A file with a Unicode byte order mark is text, even though UTF-16 and UTF-32 text contains zero bytes.
    /// A file that reports a length of zero is not opened and counts as text (see <see cref="IsEmpty"/>).
    /// </remarks>
    /// <param name="path">The full path of the file.</param>
    /// <returns>True when the file has a zero byte in its first 8 KiB and no byte order mark.</returns>
    public static bool LooksBinary(string path)
    {
        if (IsEmpty(path))
        {
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[BinarySampleBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        return LooksBinary(buffer.AsSpan(0, read));
    }

    /// <summary>
    /// Get whether the start of a file's bytes looks like binary data rather than text.
    /// </summary>
    /// <param name="bytes">The bytes of the file, or the start of them.</param>
    /// <returns>True when there is a zero byte in the first 8 KiB and no byte order mark.</returns>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes[..Math.Min(bytes.Length, BinarySampleBytes)];
        return !HasUnicodeBom(sample) && sample.Contains((byte)0);
    }

    /// <summary>
    /// Get whether bytes start with a UTF-8, UTF-16 or UTF-32 byte order mark.
    /// </summary>
    /// <param name="bytes">The start of a file.</param>
    /// <returns>True when a byte order mark is present.</returns>
    public static bool HasUnicodeBom(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(Utf8Bom) || bytes.StartsWith(Utf16LittleEndianBom) || bytes.StartsWith(Utf16BigEndianBom) || bytes.StartsWith(Utf32BigEndianBom);
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
