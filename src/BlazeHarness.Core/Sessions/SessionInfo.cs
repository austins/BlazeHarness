using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Where a session's title came from.
/// </summary>
public enum TitleSource
{
    /// <summary>A placeholder, such as the first words of the first message, until a real title arrives.</summary>
    Placeholder,

    /// <summary>The model wrote it after the first exchange.</summary>
    Generated,

    /// <summary>The user typed it, so it is never overwritten.</summary>
    User,
}

/// <summary>
/// The contents of a session's <c>session.json</c>: its title, times, folder and the settings it was last used with.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>
    /// Gets the session id, which is also the name of its folder.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title, or an empty string before the first message.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets where <see cref="Title"/> came from.
    /// </summary>
    public TitleSource TitleSource { get; init; }

    /// <summary>
    /// Gets when the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when the session last had activity, which orders the session list.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets the folder the session works in, or null for a session without one.
    /// </summary>
    public string? FolderPath { get; init; }

    /// <summary>
    /// Gets the id of the provider the session last used.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets the id of the model the session last used.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the thinking level the session last used, or null for the server default.
    /// </summary>
    public ThinkingLevel? Level { get; init; }

    /// <summary>
    /// Gets the sampling overrides set for this session, or null for none.
    /// </summary>
    public SamplingParams? Sampling { get; init; }
}
