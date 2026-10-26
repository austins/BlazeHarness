using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// A capability the model can call: a built-in file tool, the shell, or later a tool from an MCP server.
/// </summary>
/// <remarks>
/// The interface is generic on purpose. It knows nothing about files or the UI, so tools from any source share it.
/// </remarks>
public interface ITool
{
    /// <summary>
    /// Gets the name the model calls the tool by.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description the model reads to decide when to use the tool.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON Schema of the arguments object, as JSON text.
    /// </summary>
    string InputSchema { get; }

    /// <summary>
    /// Gets a value indicating whether the tool never changes anything, so it may run without approval and alongside other tools.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets how long one call may run before it is stopped.
    /// </summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// Describe a call in a few words, for the one-line summary in the UI and the approval prompt.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>A short description such as the path a file tool works on.</returns>
    string Summarize(JsonElement arguments);

    /// <summary>
    /// Run the tool.
    /// </summary>
    /// <remarks>
    /// A failure the model can fix, such as a missing file or a bad argument, is returned as an error result rather than thrown.
    /// </remarks>
    /// <param name="arguments">The call arguments, which the caller has not checked against the schema.</param>
    /// <param name="context">What the call runs against.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>The result sent back to the model.</returns>
    Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken);
}
