using BlazeHarness.Core.Diff;

namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// How a tool call would change a file, worked out before it asks for approval or runs.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Lines">The lines of the file, each marked as kept, removed or added.</param>
internal sealed record ToolChangePreview(string CallId, IReadOnlyList<DiffLine> Lines) : AgentEvent;
