namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The outcome of adding a pasted snippet to the text of <c>mcp.json</c>.
/// </summary>
/// <param name="Text">The new text of the file, or null when the snippet could not be added.</param>
/// <param name="Added">The names of servers that are new to the file.</param>
/// <param name="Replaced">The names of servers that took the place of one with the same name.</param>
/// <param name="Error">What went wrong, written for the user, or null when it worked.</param>
public sealed record McpMergeResult(string? Text, IReadOnlyList<string> Added, IReadOnlyList<string> Replaced, string? Error)
{
    /// <summary>
    /// Get a result for a snippet that could not be added.
    /// </summary>
    /// <param name="error">What went wrong, written for the user.</param>
    /// <returns>The failed result.</returns>
    public static McpMergeResult Failed(string error) => new(null, [], [], error);
}
