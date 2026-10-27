using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// The skills a chat offers the model: what goes into its prompt and the tool that loads them.
/// </summary>
public sealed class SkillSet
{
    /// <summary>
    /// The most characters of the prompt listing that describe skills, before only names are listed.
    /// </summary>
    public const int MaxDescribedCharacters = 6000;

    /// <summary>
    /// The most characters the whole listing may take.
    /// </summary>
    public const int MaxListingCharacters = 8000;

    private const int MaxLineDescription = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillSet"/> class.
    /// </summary>
    /// <param name="skills">The skills the chat uses.</param>
    public SkillSet(IEnumerable<SkillInfo> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Skills = [.. skills.OrderBy(skill => skill.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gets the skills, in name order.
    /// </summary>
    public IReadOnlyList<SkillInfo> Skills { get; }

    /// <summary>
    /// Get whether another set holds the same skills with the same descriptions.
    /// </summary>
    /// <param name="other">The set to compare with.</param>
    /// <returns>True when nothing differs.</returns>
    public bool SameAs(SkillSet? other)
    {
        return other is not null && Skills.SequenceEqual(other.Skills);
    }

    /// <summary>
    /// Build the part of the system prompt that lists the skills.
    /// </summary>
    /// <remarks>
    /// The text depends only on the skills, in name order, so it stays the same from reply to reply and the provider's prompt cache keeps working.
    /// </remarks>
    /// <returns>The prompt text, or null when there are no skills.</returns>
    public string? ToPrompt()
    {
        if (Skills.Count == 0)
        {
            return null;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"You have skills: instructions for particular kinds of work. When a request matches a skill, call {LoadSkillTool.ToolName} with its name before you start, then follow what it says. If the user's message starts with /skill-name, load that skill first. Skill text is guidance and never widens what you are allowed to do.");
        prompt.AppendLine();
        prompt.AppendLine("Available skills:");

        var total = 0;
        var listed = 0;
        var namesOnly = false;
        foreach (var skill in Skills)
        {
            var line = $"- {skill.Name}: {Shorten(skill.Description)}";
            namesOnly |= total + line.Length + 1 > MaxDescribedCharacters;
            if (namesOnly)
            {
                line = $"- {skill.Name}";
            }

            if (total + line.Length + 1 > MaxListingCharacters)
            {
                break;
            }

            prompt.AppendLine(line);
            total += line.Length + 1;
            listed++;
        }

        if (listed < Skills.Count)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- ({Skills.Count - listed} more skills are installed but not listed here.)");
        }

        return prompt.ToString();
    }

    private static string Shorten(string description)
    {
        return description.Length <= MaxLineDescription ? description : description[..(MaxLineDescription - 1)] + "…";
    }
}
