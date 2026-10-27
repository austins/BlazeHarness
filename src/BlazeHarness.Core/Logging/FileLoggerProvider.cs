using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Logging;

/// <summary>
/// Writes log entries to one text file per day, and keeps the folder small.
/// </summary>
/// <remarks>
/// A day's file stops growing at <see cref="MaxFileBytes"/> and files older than <see cref="RetainedDays"/> days are deleted,
/// so the logs can't fill the disk. A failure to write is ignored, because logging must never break the app.
/// Callers must log identifiers and counts, never the text of messages or files; an exception is written as its type,
/// HTTP status and stack trace, never its message, which can echo what was sent or received.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// The prefix of every log file name.
    /// </summary>
    public const string FilePrefix = "blazeharness-";

    /// <summary>
    /// The extension of every log file name.
    /// </summary>
    public const string FileExtension = ".log";

    /// <summary>
    /// The size at which a day's file stops taking new entries.
    /// </summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// The number of days, counting today, that log files are kept.
    /// </summary>
    public const int RetainedDays = 7;

    private const string DateFormat = "yyyyMMdd";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private FileStream? _stream;
    private DateOnly _streamDay;
    private long _size;
    private bool _full;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerProvider"/> class.
    /// </summary>
    /// <param name="directory">The folder for the log files, which is created when the first entry is written.</param>
    /// <param name="time">The clock that decides the entry times and the day's file.</param>
    public FileLoggerProvider(string directory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);
        _directory = directory;
        _time = time;
    }

    /// <summary>
    /// Get the name of the log file of a day.
    /// </summary>
    /// <param name="day">The day, in UTC.</param>
    /// <returns>The file name, without a folder.</returns>
    public static string FileNameOf(DateOnly day)
    {
        return FilePrefix + day.ToString(DateFormat, CultureInfo.InvariantCulture) + FileExtension;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            CloseStream();
        }
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _time.GetUtcNow();
        var entry = new StringBuilder();
        AppendLine(entry, now, Abbreviation(level), category, message);
        if (exception is not null)
        {
            AppendException(entry, exception);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        try
        {
            lock (_gate)
            {
                if (_stream is null || _streamDay != today)
                {
                    OpenStream(today);
                }

                if (_full)
                {
                    return;
                }

                Append(entry.ToString());

                // One marker, so a day with missing entries is not mistaken for a quiet one.
                if (_size >= MaxFileBytes)
                {
                    _full = true;
                    var marker = new StringBuilder();
                    AppendLine(marker, now, Abbreviation(LogLevel.Warning), nameof(FileLoggerProvider), "Log file is full; later entries of this day are dropped.");
                    Append(marker.ToString());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must not break the app, and there is nowhere else to report this. The next entry opens the file again.
            CloseStream();
        }
    }

    private static void AppendLine(StringBuilder entry, DateTimeOffset now, string level, string category, string message)
    {
        entry.Append(now.ToString(TimestampFormat, CultureInfo.InvariantCulture))
            .Append(' ').Append(level)
            .Append(' ').Append(category)
            .Append(' ').Append(message)
            .Append('\n');
    }

    private static void AppendException(StringBuilder entry, Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            entry.Append(current.GetType().FullName);
            if (current is HttpRequestException { StatusCode: { } status })
            {
                entry.Append(" (HTTP ").Append((int)status).Append(')');
            }

            entry.Append('\n');
            if (current.StackTrace is { } trace)
            {
                entry.Append(trace).Append('\n');
            }
        }
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };

    // Runs on the first entry of each day: makes the folder, deletes files past the retention and opens the day's file.
    private void OpenStream(DateOnly today)
    {
        _stream?.Dispose();
        _stream = null;
        Directory.CreateDirectory(_directory);
        var oldest = today.AddDays(1 - RetainedDays);
        foreach (var file in Directory.EnumerateFiles(_directory, FilePrefix + "*" + FileExtension))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];
            if (DateOnly.TryParseExact(stamp, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < oldest)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // It is tried again the next day.
                }
            }
        }

        _stream = new FileStream(
            Path.Combine(_directory, FileNameOf(today)),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        _streamDay = today;
        _size = _stream.Length;

        // A file that was already full got its marker before.
        _full = _size >= MaxFileBytes;
    }

    private void Append(string text)
    {
        var bytes = Utf8WithoutBom.GetBytes(text);
        _stream!.Write(bytes);
        _stream.Flush();
        _size += bytes.Length;
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception).ReplaceLineEndings(" "), exception);
            }
        }
    }
}
