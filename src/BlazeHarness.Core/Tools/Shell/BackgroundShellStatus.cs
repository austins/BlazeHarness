namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// How a background command stands.
/// </summary>
public enum BackgroundShellStatus
{
    /// <summary>
    /// The command is still running.
    /// </summary>
    Running,

    /// <summary>
    /// The command ended on its own.
    /// </summary>
    Exited,

    /// <summary>
    /// The model or the user stopped the command.
    /// </summary>
    Stopped,

    /// <summary>
    /// The command was stopped because it ran as long as its time limit.
    /// </summary>
    TimedOut,
}
