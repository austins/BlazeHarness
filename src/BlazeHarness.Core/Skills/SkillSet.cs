using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
    /// Get the names of the skills a user started by beginning a message with <c>/name</c>.
    /// </summary>
    /// <param name="userMessages">The text of the user's messages.</param>
    /// <returns>The names of skills in this set that were started.</returns>
    public IReadOnlySet<string> StartedIn(IEnumerable<string> userMessages)
    {
        ArgumentNullException.ThrowIfNull(userMessages);

        HashSet<string> started = new(StringComparer.Ordinal);
        foreach (var message in userMessages)
        {
            if (message.Length < 2 || message[0] != '/')
            {
                continue;
            }

            var end = message.AsSpan().IndexOfAny(" \t\r\n");
            var name = end < 0 ? message[1..] : message[1..end];
            if (Skills.Any(skill => skill.Name == name))
            {
                started.Add(name);
            }
        }

        return started;
    }

    /// <summary>
    /// Get the skills the model may load.
    /// </summary>
    /// <remarks>
    /// These are the skills that allow model invocation, the ones the user started, and any skill whose name appears in the instructions of a skill already in this list,
    /// so a skill can hand over to a hidden one. Instructions are read only when a hidden skill exists.
    /// </remarks>
    /// <param name="started">The names of skills the user started with <c>/name</c>.</param>
    /// <returns>The names of the skills the model may load.</returns>
    public IReadOnlySet<string> LoadableBy(IReadOnlySet<string> started)
    {
        ArgumentNullException.ThrowIfNull(started);

        var loadable = Skills.Where(skill => skill.ModelInvocable || started.Contains(skill.Name)).Select(skill => skill.Name).ToHashSet(StringComparer.Ordinal);
        var hidden = Skills.Where(skill => !loadable.Contains(skill.Name)).ToList();
        var queue = new Queue<SkillInfo>(Skills.Where(skill => loadable.Contains(skill.Name)));
        while (hidden.Count > 0 && queue.TryDequeue(out var current))
        {
            string body;
            try
            {
                body = File.ReadAllText(current.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var target in hidden.Where(skill => Mentions(body, skill.Name)).ToList())
            {
                hidden.Remove(target);
                loadable.Add(target.Name);
                queue.Enqueue(target);
            }
        }

        return loadable;
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
        prompt.AppendLine();
        var offered = Skills.Where(skill => skill.ModelInvocable).ToList();
        if (offered.Count == 0)
        {
            prompt.AppendLine("No skills are listed. A skill is loaded only when the user's message starts with its /skill-name.");
            return prompt.ToString();
        }

        prompt.AppendLine("Available skills:");

        var total = 0;
        var listed = 0;
        var namesOnly = false;
        foreach (var skill in offered)
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

        if (listed < offered.Count)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- ({offered.Count - listed} more skills are installed but not listed here.)");
        }

        return prompt.ToString();
    }

    private static bool Mentions(string body, string name)
    {
        return Regex.IsMatch(body, $"(?<![a-z0-9-]){Regex.Escape(name)}(?![a-z0-9-])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string Shorten(string description)
    {
        return description.Length <= MaxLineDescription ? description : description[..(MaxLineDescription - 1)] + "…";
    }
}
