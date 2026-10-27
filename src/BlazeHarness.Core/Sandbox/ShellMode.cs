namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// How much of the computer a shell command can reach.
/// </summary>
public enum ShellMode
{
    /// <summary>
    /// The command runs inside the sandbox and can change files in the working folder.
    /// </summary>
    Writable,

    /// <summary>
    /// The command runs inside the sandbox and can only read the working folder.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// The command runs as the user, with no sandbox.
    /// </summary>
    Unconfined,
}
