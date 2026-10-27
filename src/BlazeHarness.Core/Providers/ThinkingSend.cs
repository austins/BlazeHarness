namespace BlazeHarness.Core.Providers;

/// <summary>
/// The wire format used to send a thinking level.
/// </summary>
public sealed record ThinkingSend
{
    /// <summary>
    /// Gets how the level is sent.
    /// </summary>
    public ThinkingSendMode Mode { get; init; } = ThinkingSendMode.ReasoningEffort;

    /// <summary>
    /// Gets the dotted body path; only used with <see cref="ThinkingSendMode.BodyField"/>.
    /// </summary>
    public string? Field { get; init; }
}
