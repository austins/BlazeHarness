using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The part of a chat's metadata that is exported.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="TitleSource">Where the title came from, so a title the user typed is not replaced.</param>
/// <param name="CreatedAt">When the chat was created.</param>
/// <param name="UpdatedAt">When the chat last had activity.</param>
/// <param name="ProviderId">The id of the provider the chat last used.</param>
/// <param name="ModelId">The id of the model the chat last used.</param>
/// <param name="Level">The thinking level the chat last used.</param>
internal sealed record SessionDocumentInfo(
    string Title,
    TitleSource TitleSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ProviderId,
    string? ModelId,
    ThinkingLevel? Level);
