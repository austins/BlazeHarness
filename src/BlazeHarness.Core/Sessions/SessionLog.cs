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
/// </remarks>
public sealed partial class SessionLog : ISessionLog, IDisposable
{
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
            text.Append(JsonSerializer.Serialize(e, JsonDefaults.Compact)).Append('\n');
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
                        events.Add(e);
                    }
                }
                catch (JsonException ex)
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

    private bool EndsWithNewline()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping an unreadable line {Line} in the message log of session {Session}.")]
    private partial void LogSkippedLine(Exception ex, string? session, int line);
}
