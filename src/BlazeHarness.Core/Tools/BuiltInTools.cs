using BlazeHarness.Core.Attachments;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Creates the tool set every session with a working folder gets.
/// </summary>
public static class BuiltInTools
{
    /// <summary>
    /// Create the built-in tools over a working folder.
    /// </summary>
    /// <param name="folder">The working folder, which must exist.</param>
    /// <param name="shell">The shell for the <c>shell</c> tool, or null to leave that tool out.</param>
    /// <param name="attachments">The session's attached files for the <c>read_attachment</c> tool, or null to leave that tool out.</param>
    /// <returns>The tool set.</returns>
    public static ToolSet Create(string folder, ShellInfo? shell = null, SessionAttachments? attachments = null)
    {
        List<ITool> tools = [new ReadFileTool(), new ListFilesTool(), new SearchFilesTool(), new WriteFileTool(), new EditFileTool(), new ShowImageTool()];
        if (shell is not null)
        {
            tools.Add(new ShellTool(shell));
        }

        if (attachments is not null)
        {
            tools.Add(new ReadAttachmentTool(attachments));
        }

        return new ToolSet(tools, new ToolContext(folder));
    }

    /// <summary>
    /// Create the tool set for a chat without a working folder, which can only read the files attached to it.
    /// </summary>
    /// <param name="attachments">The session's attached files.</param>
    /// <returns>The tool set.</returns>
    public static ToolSet ForAttachments(SessionAttachments attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        Directory.CreateDirectory(attachments.Directory);
        return new ToolSet([new ReadAttachmentTool(attachments)], new ToolContext(attachments.Directory));
    }
}
