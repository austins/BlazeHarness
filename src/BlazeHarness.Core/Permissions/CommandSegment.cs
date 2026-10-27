namespace BlazeHarness.Core.Permissions;

/// <summary>
/// One simple command found inside a shell command line.
/// </summary>
/// <param name="Program">The program name without a folder or extension. Compare it ignoring case.</param>
/// <param name="Arguments">The arguments after the program, with quotes and escapes removed.</param>
/// <param name="Opaque">A sentence that says why the command can't be checked, such as text built at run time, or null when it can.</param>
internal sealed record CommandSegment(string Program, IReadOnlyList<string> Arguments, string? Opaque = null);
