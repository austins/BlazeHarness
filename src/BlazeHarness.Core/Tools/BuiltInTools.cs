using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Creates the tool set every session with a working folder gets.
/// </summary>
internal static class BuiltInTools
{
    /// <summary>
    /// Create the built-in tools over a working folder.
    /// </summary>
    /// <param name="folder">The working folder, which must exist.</param>
    /// <param name="shell">The shell for the <c>shell</c> tool, or null to leave that tool out.</param>
    /// <param name="attachments">The session's attached files for the <c>read_attachment</c> tool, or null to leave that tool out.</param>
    /// <param name="sandbox">Starts the shell commands. Required when <paramref name="shell"/> is set.</param>
    /// <param name="scratchDirectory">The folder confined shell commands may always write to. Required when <paramref name="shell"/> is set.</param>
    /// <param name="time">Times the tools' timeouts; the system clock when null.</param>
    /// <param name="chatSearch">The <c>search_chat</c> tool, or null to leave it out.</param>
    /// <returns>The tool set.</returns>
    public static ToolSet Create(
        string folder,
        ShellInfo? shell = null,
        SessionAttachments? attachments = null,
        IShellSandbox? sandbox = null,
        string? scratchDirectory = null,
        TimeProvider? time = null,
        ITool? chatSearch = null)
    {
        List<ITool> tools = [new ReadFileTool(), new ListFilesTool(), new SearchFilesTool(), new WriteFileTool(), new EditFileTool(), new ShowImageTool()];
        if (chatSearch is not null)
        {
            tools.Add(chatSearch);
        }

        if (shell is not null)
        {
            ArgumentNullException.ThrowIfNull(sandbox);
            ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
            tools.Add(new ShellTool(shell, sandbox, scratchDirectory, time));
        }

        if (attachments is not null)
        {
            tools.Add(new ReadAttachmentTool(attachments));
        }

        return new ToolSet(tools, new ToolContext(folder), time);
    }

    /// <summary>
    /// Create the tool set for a chat without a working folder, which can only read the files attached to it.
    /// </summary>
    /// <param name="attachments">The session's attached files.</param>
    /// <param name="time">Times the tool's timeout; the system clock when null.</param>
    /// <param name="chatSearch">The <c>search_chat</c> tool, or null to leave it out.</param>
    /// <returns>The tool set.</returns>
    public static ToolSet ForAttachments(SessionAttachments attachments, TimeProvider? time = null, ITool? chatSearch = null)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        Directory.CreateDirectory(attachments.Directory);
        List<ITool> tools = [new ReadAttachmentTool(attachments)];
        if (chatSearch is not null)
        {
            tools.Add(chatSearch);
        }

        return new ToolSet(tools, new ToolContext(attachments.Directory), time);
    }
}
