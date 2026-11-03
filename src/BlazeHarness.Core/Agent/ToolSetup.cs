using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// The tools of a chat and the system prompt that goes with them, swapped as one value.
/// </summary>
/// <param name="Tools">The tools the model may call, or null for none.</param>
/// <param name="BasePrompt">The base system prompt, or null for none.</param>
internal sealed record ToolSetup(ToolSet? Tools, string? BasePrompt);
