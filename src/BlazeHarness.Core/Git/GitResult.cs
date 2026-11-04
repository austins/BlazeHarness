namespace BlazeHarness.Core.Git;

/// <summary>
/// What a run of the git program produced.
/// </summary>
/// <param name="ExitCode">The exit code, or -1 when git could not be started or took too long.</param>
/// <param name="Output">The standard output as text.</param>
/// <param name="Truncated">Whether the output was cut short because it was too large.</param>
internal sealed record GitResult(int ExitCode, string Output, bool Truncated)
{
    /// <summary>
    /// Gets the result of a run that could not happen.
    /// </summary>
    public static GitResult Unavailable { get; } = new(-1, string.Empty, false);

    /// <summary>
    /// Gets a value indicating whether git ran and succeeded.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}
