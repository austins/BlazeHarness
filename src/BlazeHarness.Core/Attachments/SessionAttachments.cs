using BlazeHarness.Core.Tools;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// The files attached in one session, kept in the session's <c>attachments</c> folder.
/// </summary>
/// <remarks>
/// A file is stored as <c>&lt;id&gt;-&lt;name&gt;</c>. The id is generated here and the name is cleaned, so nothing a client sends
/// is ever used as a path. The type is decided from the content, not from the name.
/// </remarks>
public sealed partial class SessionAttachments
{
    /// <summary>
    /// The largest file that can be attached, in bytes.
    /// </summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>
    /// The most files one message can carry.
    /// </summary>
    public const int MaxFilesPerMessage = 10;

    /// <summary>
    /// The most the files of one message can add up to, in bytes.
    /// </summary>
    public const long MaxMessageBytes = 50L * 1024 * 1024;

    private const int MaxNameLength = 80;

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionAttachments"/> class.
    /// </summary>
    /// <param name="directory">The folder the files are kept in. It is created when the first file is saved.</param>
    public SessionAttachments(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    /// <summary>
    /// Gets the folder the files are kept in.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Save an uploaded file.
    /// </summary>
    /// <param name="fileName">The name the client gave the file. Any path in it is dropped.</param>
    /// <param name="content">The file content, read once and not buffered whole.</param>
    /// <param name="cancellationToken">A token that stops the upload.</param>
    /// <returns>The saved file.</returns>
    /// <exception cref="AttachmentException">The file is empty, too large, or neither an image nor text.</exception>
    public async Task<AttachmentInfo> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var name = CleanName(fileName);
        System.IO.Directory.CreateDirectory(_directory);
        var temp = Path.Combine(_directory, ".upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var size = await CopyAsync(content, temp, name, cancellationToken);
            var sniffed = Sniff(temp);
            var id = NewId();
            var lines = 0;
            if (sniffed is null)
            {
                if (ToolSupport.LooksBinary(temp))
                {
                    throw new AttachmentException($"'{name}' can't be attached. Only images (PNG, JPEG, GIF, WebP) and text files are supported.");
                }

                lines = CountLines(temp);
                sniffed = ("text/plain", string.Empty);
                if (MediaTypeOf(name) != "text/plain")
                {
                    name = WithExtension(name, ".txt");
                }
            }
            else
            {
                name = WithExtension(Path.GetFileNameWithoutExtension(name), sniffed.Value.Extension);
            }

            File.Move(temp, Path.Combine(_directory, $"{id}-{name}"));
            return new AttachmentInfo(id, name, sniffed.Value.MediaType, size, lines);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// Find a saved file by its id.
    /// </summary>
    /// <param name="id">The id from <see cref="AttachmentInfo.Id"/>.</param>
    /// <returns>The file, or null when the id is not valid or no such file is saved.</returns>
    public AttachmentFile? Find(string? id)
    {
        if (id is null || !IdPattern().IsMatch(id) || !System.IO.Directory.Exists(_directory))
        {
            return null;
        }

        var path = System.IO.Directory.EnumerateFiles(_directory, id + "-*").FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        var name = Path.GetFileName(path)[(id.Length + 1)..];
        return new AttachmentFile(path, name, MediaTypeOf(name));
    }

    // A short id can clash; Find would then return another file's name.
    private string NewId()
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N")[..8];
        }
        while (Find(id) is not null);

        return id;
    }

    private static string WithExtension(string name, string extension)
    {
        var stem = name.Length + extension.Length > MaxNameLength ? name[..(MaxNameLength - extension.Length)] : name;
        return stem + extension;
    }

    private static string CleanName(string? fileName)
    {
        var name = fileName ?? string.Empty;
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().Trim('.');
        if (cleaned.Length > MaxNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            cleaned = extension.Length is > 0 and <= 10
                ? cleaned[..(MaxNameLength - extension.Length)] + extension
                : cleaned[..MaxNameLength];
        }

        return cleaned.Length == 0 ? "file" : cleaned;
    }

    private static async Task<long> CopyAsync(Stream content, string path, string name, CancellationToken cancellationToken)
    {
        long total = 0;
        var buffer = new byte[81_920];
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
            {
                throw new AttachmentException(string.Create(CultureInfo.InvariantCulture, $"'{name}' is larger than {MaxFileBytes / (1024 * 1024)} MB."));
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total == 0 ? throw new AttachmentException($"'{name}' is empty.") : total;
    }

    private static (string MediaType, string Extension)? Sniff(string path)
    {
        Span<byte> head = stackalloc byte[ImageFiles.SniffBytes];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        head = head[..stream.Read(head)];
        return ImageFiles.SniffRaster(head);
    }

    private static int CountLines(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = 0;
        while (reader.ReadLine() is not null)
        {
            lines++;
        }

        return lines;
    }

    private static string MediaTypeOf(string name)
    {
        return Path.GetExtension(name).ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".JPG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".WEBP" => "image/webp",
            _ => "text/plain",
        };
    }

    [GeneratedRegex("^[0-9a-f]{8}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdPattern();
}
