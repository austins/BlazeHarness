using BlazeHarness.Core.Agent;

namespace BlazeHarness.Web.Components.Conversation;

/// <summary>
/// What the composer hands over when the user sends.
/// </summary>
/// <param name="Text">The message text, which is empty when only files are sent.</param>
/// <param name="Files">The files attached to the message.</param>
/// <param name="Target">The provider, model and thinking level chosen for the reply.</param>
public sealed record ComposerMessage(string Text, IReadOnlyList<PendingFile> Files, ChatTarget Target);
