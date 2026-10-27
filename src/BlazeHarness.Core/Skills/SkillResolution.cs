namespace BlazeHarness.Core.Skills;

/// <summary>
/// The skills that apply to a chat after duplicate names are settled.
/// </summary>
/// <param name="Skills">One skill for each name, in name order.</param>
/// <param name="Shadowed">Skills that lost to another with the same name.</param>
/// <param name="Problems">Messages about skills that could not be read.</param>
public sealed record SkillResolution(IReadOnlyList<SkillInfo> Skills, IReadOnlyList<SkillInfo> Shadowed, IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Settle duplicate names across folders.
    /// </summary>
    /// <remarks>
    /// The chat's own folder wins over BlazeHarness's folder, which wins over <c>~/.agents/skills</c>. Within one folder
    /// the first folder in name order wins.
    /// </remarks>
    /// <param name="scans">The scans of every folder, in any order.</param>
    /// <returns>The skills to use, and the ones that were passed over.</returns>
    public static SkillResolution Of(params SkillScan[] scans)
    {
        ArgumentNullException.ThrowIfNull(scans);

        var all = scans.SelectMany(scan => scan.Skills)
            .OrderBy(skill => skill.Source)
            .ThenBy(skill => skill.Name, StringComparer.Ordinal)
            .ThenBy(skill => skill.Directory, StringComparer.Ordinal)
            .ToList();
        var winners = all.DistinctBy(skill => skill.Name, StringComparer.Ordinal).ToList();
        var shadowed = all.Except(winners).ToList();
        return new SkillResolution(
            [.. winners.OrderBy(skill => skill.Name, StringComparer.Ordinal)],
            shadowed,
            [.. scans.SelectMany(scan => scan.Problems)]);
    }
}
