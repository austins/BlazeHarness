namespace BlazeHarness.Core.Agent;

/// <summary>
/// Hands the agent loop messages the user wrote while the reply was running.
/// </summary>
internal interface IRunInbox
{
    /// <summary>
    /// Take everything that is waiting, so it is not offered again.
    /// </summary>
    /// <returns>The waiting messages merged into one text, or null when there are none.</returns>
    string? TakeMessages();
}
