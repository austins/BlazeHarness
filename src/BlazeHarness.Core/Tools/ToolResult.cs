using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// What a tool call produced.
/// </summary>
/// <param name="Output">The text sent back to the model.</param>
/// <param name="IsError">Whether the call failed, so the model should adjust rather than rely on the output.</param>
public sealed record ToolResult(string Output, bool IsError = false)
{
    /// <summary>
    /// Gets the pictures the call produced. The chat keeps a copy of each and shows them to the model after the results, when the model accepts images.
    /// </summary>
    public IReadOnlyList<ToolImage> Images { get; init; } = [];

    /// <summary>
    /// Create a successful result with one picture.
    /// </summary>
    /// <param name="output">The text sent back to the model.</param>
    /// <param name="image">The picture.</param>
    /// <returns>The result.</returns>
    public static ToolResult WithImage(string output, DataContent image) => new(output) { Images = [new ToolImage(image)] };

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
