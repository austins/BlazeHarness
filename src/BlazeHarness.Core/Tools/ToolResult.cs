namespace BlazeHarness.Core.Tools;

/// <summary>
/// What a tool call produced.
/// </summary>
/// <param name="Output">The text sent back to the model.</param>
/// <param name="IsError">Whether the call failed, so the model should adjust rather than rely on the output.</param>
public sealed record ToolResult(string Output, bool IsError = false)
{
    /// <summary>
    /// Create a successful result.
    /// </summary>
    /// <param name="output">The text sent back to the model.</param>
    /// <returns>The result.</returns>
    public static ToolResult Ok(string output) => new(output);

    /// <summary>
    /// Create a failed result.
    /// </summary>
    /// <param name="message">What went wrong and, where it helps, how to fix the call.</param>
    /// <returns>The result.</returns>
    public static ToolResult Error(string message) => new(message, IsError: true);
}
