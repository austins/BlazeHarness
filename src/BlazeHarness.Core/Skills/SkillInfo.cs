namespace BlazeHarness.Core.Skills;

/// <summary>
/// A skill found on disk: a folder with a <c>SKILL.md</c> file.
/// </summary>
/// <param name="Name">The skill's name, such as <c>pdf-tools</c>. It is what the model passes to <c>load_skill</c>.</param>
/// <param name="Description">What the skill does and when to use it, on one line.</param>
/// <param name="Directory">The full path of the skill's folder.</param>
/// <param name="Source">Where the skill was found.</param>
/// <param name="ModelInvocable">False when the skill says <c>disable-model-invocation: true</c>: the model isn't told about it and loads it only after the user types <c>/name</c>.</param>
public sealed record SkillInfo(string Name, string Description, string Directory, SkillSource Source, bool ModelInvocable = true)
{
    /// <summary>
    /// The name of the file that holds a skill's instructions.
    /// </summary>
    public const string FileName = "SKILL.md";

    /// <summary>
    /// Gets the full path of the skill's <c>SKILL.md</c> file.
    /// </summary>
    public string FilePath => System.IO.Path.Combine(Directory, FileName);
}
