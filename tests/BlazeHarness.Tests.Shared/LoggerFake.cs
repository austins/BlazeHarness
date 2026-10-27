namespace BlazeHarness.Tests.Shared;

/// <summary>A logger that keeps what it is given in memory. It is safe to log to from timers and background threads.</summary>
public class LoggerFake : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly Type? _categoryName;

    /// <summary>Initializes a new instance of the <see cref="LoggerFake"/> class.</summary>
    /// <param name="categoryName">The type whose name labels the test output, or null.</param>
    protected LoggerFake(Type? categoryName = null)
    {
        _categoryName = categoryName;
    }

    /// <summary>Gets a copy of the entries logged so far.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var logEntry = new LogEntry { LogLevel = logLevel, Exception = exception, Message = formatter(state, exception) };
        lock (_gate)
        {
            _entries.Add(logEntry);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"[{nameof(LoggerFake)}] {logLevel} {_categoryName?.FullName}[{eventId.Id}]: {logEntry.Message}");
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.Trace;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;
}

/// <summary>A <see cref="LoggerFake"/> for the category of one class.</summary>
/// <typeparam name="TCategoryName">The class the logger belongs to.</typeparam>
public sealed class LoggerFake<TCategoryName>() : LoggerFake(typeof(TCategoryName)), ILogger<TCategoryName>;
