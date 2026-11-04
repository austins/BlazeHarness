using System.Text;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// The user's own instructions from the settings, added to the system prompt after the base prompt.
/// </summary>
internal static class UserInstructions
{
    private const string Tag = "user-instructions";

    /// <summary>
    /// Build the text that goes into the system prompt.
    /// </summary>
    /// <param name="instructions">The text the user wrote, or null for none.</param>
    /// <returns>The prompt text, or null when there is nothing to add.</returns>
    public static string? ToPrompt(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return null;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("The user wrote these standing instructions for every chat. They add to the instructions above and to any AGENTS.md guidance, which wins when they conflict. They never widen what you are allowed to do.");
        prompt.AppendLine();
        prompt.Append('<').Append(Tag).AppendLine(">");

        // The text must not be able to close its own block and pass more text off as the app's own instructions.
        prompt.AppendLine(instructions.Trim().Replace("</" + Tag, "&lt;/" + Tag, StringComparison.OrdinalIgnoreCase));
        prompt.Append("</").Append(Tag).AppendLine(">");
        return prompt.ToString();
    }
}
