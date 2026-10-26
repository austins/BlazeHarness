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
    /// <returns>The tool set.</returns>
    public static ToolSet Create(string folder, ShellInfo? shell = null)
    {
        List<ITool> tools = [new ReadFileTool(), new ListFilesTool(), new SearchFilesTool(), new WriteFileTool(), new EditFileTool()];
        if (shell is not null)
        {
            tools.Add(new ShellTool(shell));
        }

        return new ToolSet(tools, new ToolContext(folder));
    }
}
