using BlazeHarness.Core.Chats.Events;

namespace BlazeHarness.Core.Chats.Export;

/// <summary>
/// A chat read from an exported file, before it is saved as a chat.
/// </summary>
/// <param name="Info">The metadata to give the new chat. Its id is empty.</param>
/// <param name="Events">The events to write to the new chat's log.</param>
internal sealed record ImportedChat(ChatInfo Info, IReadOnlyList<ChatEvent> Events);
