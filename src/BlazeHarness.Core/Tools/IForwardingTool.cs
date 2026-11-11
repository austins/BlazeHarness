namespace BlazeHarness.Core.Tools;

/// <summary>
/// A tool that does nothing itself and names the tool to call instead, so the harness can check the permission of the tool that really runs.
/// </summary>
internal interface IForwardingTool : ITool
{
    /// <summary>
    /// Work out which tool a call stands for.
    /// </summary>
    /// <param name="arguments">The arguments of the call to this tool.</param>
    /// <param name="cancellationToken">A token to cancel the wait, as the target's server may have to start.</param>
    /// <returns>The tool to run and its arguments, or the reason there is none.</returns>
    /// <exception cref="ToolArgumentException">The arguments are not valid.</exception>
    public Task<ForwardedCall> ResolveAsync(ToolArguments arguments, CancellationToken cancellationToken);
}
