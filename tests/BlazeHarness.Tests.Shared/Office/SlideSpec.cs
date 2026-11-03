namespace BlazeHarness.Tests.Shared.Office;

/// <summary>
/// Describes one slide for <see cref="OfficeFiles.Presentation"/>.
/// </summary>
/// <param name="Title">The title placeholder text, or null for none.</param>
/// <param name="Lines">The paragraphs of a body shape, or null for none.</param>
/// <param name="Notes">The speaker notes, or null for none.</param>
/// <param name="Table">The rows of a table, or null for none.</param>
/// <param name="Grouped">Whether the body shape sits inside a group shape.</param>
public sealed record SlideSpec(
    string? Title = null,
    IReadOnlyList<string>? Lines = null,
    string? Notes = null,
    IReadOnlyList<IReadOnlyList<string>>? Table = null,
    bool Grouped = false);
