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
