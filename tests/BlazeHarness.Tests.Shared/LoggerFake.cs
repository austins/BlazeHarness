namespace BlazeHarness.Tests.Shared;

/// <summary>A logger that keeps what it is given in memory. It is safe to log to from timers and background threads.</summary>
public class LoggerFake : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly Type? _categoryName;

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

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.Trace;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;
}
