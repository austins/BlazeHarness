namespace BlazeHarness.Core.Skills;

/// <summary>
/// Where a skill was found. When two skills share a name, the one with the lower value wins.
/// </summary>
public enum SkillSource
{
    /// <summary>
    /// The <c>.agents/skills</c> folder inside the chat's own folder.
    /// </summary>
    Project,

    /// <summary>
    /// The <c>skills</c> folder in BlazeHarness's own folder.
    /// </summary>
    App,

    /// <summary>
    /// The <c>~/.agents/skills</c> folder, which other tools read too.
    /// </summary>
    UserAgents,
}
