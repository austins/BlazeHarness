namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The tools one server offered when it last connected.
/// </summary>
/// <param name="Fingerprint">Identifies the settings the server ran with, so a changed server is not answered from old data.</param>
/// <param name="Tools">The tools it listed.</param>
internal sealed record McpToolCacheEntry(string Fingerprint, IReadOnlyList<McpToolInfo> Tools);
