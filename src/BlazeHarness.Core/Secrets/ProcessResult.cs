namespace BlazeHarness.Core.Secrets;

/// <summary>The exit code and captured output of a helper process.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StdOut">Everything the process wrote to stdout.</param>
/// <param name="StdErr">Everything the process wrote to stderr.</param>
internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
