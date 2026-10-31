namespace BlazeHarness.Core.Tools;

/// <summary>
/// The tool that a call to an <see cref="IForwardingTool"/> stands for.
/// </summary>
/// <param name="Tool">The tool to run, or null when there is none.</param>
/// <param name="ArgumentsJson">The arguments to give it, as a JSON object.</param>
/// <param name="Name">The name to show for the call.</param>
/// <param name="Problem">Why there is no tool, to tell the model.</param>
internal sealed record ForwardedCall(ITool? Tool, string ArgumentsJson, string Name, string? Problem);
