namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The outcome of saving a server to the text of <c>mcp.json</c>.
/// </summary>
/// <param name="Text">The new text of the file, or null when the server could not be saved.</param>
/// <param name="Added">The names of servers that are new to the file.</param>
/// <param name="Replaced">The names of servers that were already in the file and were changed.</param>
/// <param name="Error">What went wrong, written for the user, or null when it worked.</param>
public sealed record McpMergeResult(string? Text, IReadOnlyList<string> Added, IReadOnlyList<string> Replaced, string? Error)
{
    /// <summary>
    /// Get a result for a server that could not be saved.
    /// </summary>
    /// <param name="error">What went wrong, written for the user.</param>
    /// <returns>The failed result.</returns>
    public static McpMergeResult Failed(string error) => new(null, [], [], error);
}
