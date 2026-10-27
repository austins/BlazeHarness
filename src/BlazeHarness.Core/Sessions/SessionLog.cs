using BlazeHarness.Core.Config;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A session's append-only <c>messages.jsonl</c>: one JSON event per line.
/// </summary>
/// <remarks>
/// Appends are serialized, so concurrent writers never interleave lines. A crash can leave a truncated last line;
/// reading skips it, and the next append starts on a fresh line so it is never glued to the fragment.
/// A tool result longer than <see cref="OutputFileThreshold"/> characters is saved in its own file in the <c>tool-outputs</c> folder next to the log,
/// and the log keeps only its start. Reading puts the whole text back, so callers never see the difference.
/// </remarks>
public sealed partial class SessionLog : ISessionLog, IDisposable
{
    /// <summary>
    /// The name of the folder next to the log that holds long tool results.
    /// </summary>
    public const string OutputsFolder = "tool-outputs";

    /// <summary>
    /// The length in characters above which a tool result is saved in its own file.
    /// </summary>
    public const int OutputFileThreshold = 8_000;

    private const int PreviewLength = 1_000;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _checkedEnding;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLog"/> class.
    /// </summary>
    /// <param name="path">The full path of the log file. It is created on the first append.</param>
    /// <param name="logger">The logger for lines that cannot be read.</param>
    public SessionLog(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(logger);
        _path = path;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(IReadOnlyCollection<SessionEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var e in events)
        {
            var line = e is ToolResultEvent { OutputFile: null } result && result.Output.Length > OutputFileThreshold
                ? await StoreOutputAsync(result, cancellationToken)
                : e;
            text.Append(JsonSerializer.Serialize(line, JsonDefaults.Compact)).Append('\n');
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (!_checkedEnding && File.Exists(_path) && new FileInfo(_path).Length > 0 && !EndsWithNewline())
            {
                text.Insert(0, '\n');
            }

            await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            await stream.WriteAsync(Utf8.GetBytes(text.ToString()), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            _checkedEnding = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var events = new List<SessionEvent>();
            var lineNumber = 0;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
            using var reader = new StreamReader(stream, Utf8);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<SessionEvent>(line, JsonDefaults.Compact) is { } e)
                    {
                        events.Add(e is ToolResultEvent { OutputFile: not null } stored ? await LoadOutputAsync(stored, cancellationToken) : e);
                    }
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    LogSkippedLine(ex, Path.GetFileName(Path.GetDirectoryName(_path)), lineNumber);
                }
            }

            return events;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    private string OutputPath(string file) => Path.Combine(Path.GetDirectoryName(_path)!, OutputsFolder, file);

    private async Task<ToolResultEvent> StoreOutputAsync(ToolResultEvent result, CancellationToken cancellationToken)
    {
        var file = Guid.NewGuid().ToString("N") + ".txt";
        var path = OutputPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, result.Output, Utf8, cancellationToken);

        var length = PreviewLength;
        if (char.IsHighSurrogate(result.Output[length - 1]))
        {
            length--;
        }

        return result with { Output = result.Output[..length], OutputFile = file };
    }

    private async Task<ToolResultEvent> LoadOutputAsync(ToolResultEvent stored, CancellationToken cancellationToken)
    {
        var file = stored.OutputFile!;
        if (file != Path.GetFileName(file))
        {
            return stored with { OutputFile = null };
        }

        try
        {
            return stored with { Output = await File.ReadAllTextAsync(OutputPath(file), Utf8, cancellationToken) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMissingOutput(ex, Path.GetFileName(Path.GetDirectoryName(_path)), file);
            return stored with { Output = stored.Output + "\n[The rest of this output could not be read.]" };
        }
    }

    private bool EndsWithNewline()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the saved tool output {File} of session {Session}.")]
    private partial void LogMissingOutput(Exception ex, string? session, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping an unreadable line {Line} in the message log of session {Session}.")]
    private partial void LogSkippedLine(Exception ex, string? session, int line);
}
