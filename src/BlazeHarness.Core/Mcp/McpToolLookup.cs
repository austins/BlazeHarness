namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The outcome of looking a tool up by name so it can be called.
/// </summary>
/// <param name="Tool">The tool, or null.</param>
/// <param name="Problem">Why there is no tool, or null when there is one.</param>
internal sealed record McpToolLookup(McpTool? Tool, string? Problem);
