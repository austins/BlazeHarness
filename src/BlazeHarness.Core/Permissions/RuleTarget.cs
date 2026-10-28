namespace BlazeHarness.Core.Permissions;

/// <summary>
/// What a <see cref="PermissionRule"/> is matched against.
/// </summary>
public enum RuleTarget
{
    /// <summary>The command text of a shell call, or any simple command inside it.</summary>
    ShellCommand,

    /// <summary>A path a file tool reads or changes.</summary>
    FilePath,

    /// <summary>An MCP tool, written as the server name and the tool's own name joined by <c>/</c>.</summary>
    McpTool,

    /// <summary>
    /// The host a web fetch reads from (<c>example.com</c>), or <c>web_search</c> for a web search.
    /// </summary>
    WebHost,
}
