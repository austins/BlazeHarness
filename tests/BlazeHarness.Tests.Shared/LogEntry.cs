using Microsoft.Extensions.Logging;

namespace BlazeHarness.Tests.Shared;

/// <summary>One entry a <see cref="LoggerFake"/> recorded.</summary>
public sealed class LogEntry
{
    /// <summary>Gets the severity.</summary>
    public required LogLevel LogLevel { get; init; }

    /// <summary>Gets the exception that was logged, or null.</summary>
    public required Exception? Exception { get; init; }

    /// <summary>Gets the formatted message.</summary>
    public required string Message { get; init; }
}
