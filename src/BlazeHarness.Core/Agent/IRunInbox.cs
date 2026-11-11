namespace BlazeHarness.Core.Agent;

/// <summary>
/// Hands the agent loop messages the user wrote while the reply was running.
/// </summary>
internal interface IRunInbox
{
    /// <summary>
    /// Take what is waiting, so it is not offered again.
    /// </summary>
    /// <param name="replyEnded">True when the model has finished its reply, which takes every message; false between two steps, which takes only <see cref="QueueMode.Steer"/> messages.</param>
    /// <returns>The messages taken merged into one text, or null when there are none.</returns>
    public string? TakeMessages(bool replyEnded);
}
