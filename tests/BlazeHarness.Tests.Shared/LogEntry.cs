namespace BlazeHarness.Tests.Shared;

/// <summary>One entry a <see cref="LoggerFake"/> recorded.</summary>
public sealed class LogEntry
{
    public required LogLevel LogLevel { get; init; }

    public required Exception? Exception { get; init; }

    public required string Message { get; init; }
}
