using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// One entry of the forbidden command list, as written in the data file.
/// </summary>
/// <remarks>
/// A command matches when every condition that is set holds. A rule with neither programs nor text to look for is ignored.
/// </remarks>
internal sealed record ForbiddenRule
{
    /// <summary>
    /// Gets the program names the rule applies to, without a path or extension. A trailing <c>*</c> matches any ending. Empty means any program.
    /// </summary>
    public IReadOnlyList<string> Programs { get; init; } = [];

    /// <summary>
    /// Gets the arguments the command must start with, in order, ignoring case.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// Gets the options of which at least one must be present.
    /// </summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>
    /// Gets how <see cref="Flags"/> are matched.
    /// </summary>
    public FlagStyle FlagStyle { get; init; }

    /// <summary>
    /// Gets the text of which at least one must appear, ignoring case: in an argument, or anywhere in the command line when the rule names no program. Backslashes count as slashes.
    /// </summary>
    public IReadOnlyList<string> Contains { get; init; } = [];

    /// <summary>
    /// Gets the operating systems the rule applies to. Empty means all of them.
    /// </summary>
    public IReadOnlyList<SandboxPlatform> Platforms { get; init; } = [];

    /// <summary>
    /// Gets the sentence that tells the user and the model what is risky about the command.
    /// </summary>
    public string Risk { get; init; } = string.Empty;
}
