namespace BlazeHarness.Core.Agent;

/// <summary>
/// Receives what a running tool call prints, so the user can follow it before the call ends.
/// </summary>
/// <remarks>
/// What it receives is only shown; the model gets the call's result as usual, and nothing is saved.
/// </remarks>
internal interface IToolProgress
{
    /// <summary>
    /// Report one line a running call printed.
    /// </summary>
    /// <param name="callId">The provider's identifier for the call.</param>
    /// <param name="line">The line, without its line ending.</param>
    public void ReportLine(string callId, string line);
}
