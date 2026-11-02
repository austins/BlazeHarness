namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// The contents of <c>cache/mcp-tools.json</c>.
/// </summary>
internal sealed record McpToolCacheDocument
{
    /// <summary>
    /// Gets the tools of each server by server name.
    /// </summary>
    public IReadOnlyDictionary<string, McpToolCacheEntry> Servers { get; init; } = new Dictionary<string, McpToolCacheEntry>(StringComparer.Ordinal);
}
