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
    /// <returns>The tool set.</returns>
    public static ToolSet Create(string folder)
    {
        return new ToolSet(
            [new ReadFileTool(), new ListFilesTool(), new SearchFilesTool(), new WriteFileTool(), new EditFileTool()],
            new ToolContext(folder));
    }
}
