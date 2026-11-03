namespace BlazeHarness.Core.Agent;

/// <summary>
/// When a message the user writes during a reply is added to the conversation.
/// </summary>
public enum QueueMode
{
    /// <summary>
    /// After the tool results of the current step, so the model changes course while it works.
    /// </summary>
    Steer,

    /// <summary>
    /// Once the model has finished its reply, as the next message.
    /// </summary>
    AfterReply,
}
