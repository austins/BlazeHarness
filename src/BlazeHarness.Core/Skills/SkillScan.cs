namespace BlazeHarness.Core.Skills;

/// <summary>
/// The skills found in one folder.
/// </summary>
/// <param name="Skills">The skills that could be read, in name order.</param>
/// <param name="Problems">One message for each skill folder that was skipped, naming the folder and why.</param>
public sealed record SkillScan(IReadOnlyList<SkillInfo> Skills, IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Gets a scan that found nothing.
    /// </summary>
    public static SkillScan Empty { get; } = new([], []);
}
