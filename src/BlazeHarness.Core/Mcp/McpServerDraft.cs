using System.Collections.ObjectModel;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The settings of one server as the form edits them, before they are written to <c>mcp.json</c>.
/// </summary>
public sealed class McpServerDraft
{
    /// <summary>
    /// Gets a value indicating whether the server is being added, so its name can still be chosen.
    /// </summary>
    public bool IsNew { get; init; } = true;

    /// <summary>
    /// Gets or sets the name of the server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how to reach the server.
    /// </summary>
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>
    /// Gets or sets the program to start, for a <see cref="McpTransport.Stdio"/> server.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arguments of the program, one per line.
    /// </summary>
    public string ArgsText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder to start the program in, or empty for the app's own.
    /// </summary>
    public string Cwd { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address of a remote server.
    /// </summary>
#pragma warning disable CA1056 // Holds text the user is still typing, which may not be an address yet.
    public string Url { get; set; } = string.Empty;
#pragma warning restore CA1056

    /// <summary>
    /// Gets the environment variables to give the program.
    /// </summary>
    public Collection<McpDraftEntry> Env { get; } = [];

    /// <summary>
    /// Gets the HTTP headers to send to a remote server.
    /// </summary>
    public Collection<McpDraftEntry> Headers { get; } = [];
}
