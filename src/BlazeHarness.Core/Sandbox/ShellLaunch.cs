using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// One command to start in a shell.
/// </summary>
/// <param name="Shell">The shell that runs the command.</param>
/// <param name="Command">The command text, in the syntax of <paramref name="Shell"/>.</param>
/// <param name="WorkingDirectory">The real path of the working folder, which is the current directory of the command.</param>
/// <param name="Mode">How far the command may reach.</param>
/// <param name="ScratchDirectory">A folder the command may always write to, which holds its home and temporary files. It is created when needed.</param>
/// <param name="Timeout">How long the command may run before the sandbox stops it.</param>
/// <param name="ReadFolders">Folders outside the working folder that the command may also read, because the user approved them.</param>
public sealed record ShellLaunch(
    ShellInfo Shell,
    string Command,
    string WorkingDirectory,
    ShellMode Mode,
    string ScratchDirectory,
    TimeSpan Timeout,
    IReadOnlyList<string>? ReadFolders = null);
