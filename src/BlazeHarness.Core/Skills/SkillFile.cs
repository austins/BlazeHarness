namespace BlazeHarness.Core.Skills;

/// <summary>
/// The parts of a <c>SKILL.md</c> file.
/// </summary>
/// <param name="Name">The <c>name</c> field, or null when it is missing.</param>
/// <param name="Description">The <c>description</c> field on one line, or null when it is missing.</param>
/// <param name="Body">The instructions after the front matter.</param>
/// <param name="Error">Why the file could not be read as a skill, or null when it can.</param>
/// <param name="DisableModelInvocation">True when the <c>disable-model-invocation</c> field is <c>true</c>, so only the user may start the skill.</param>
internal sealed record SkillFile(string? Name, string? Description, string Body, string? Error, bool DisableModelInvocation = false);
