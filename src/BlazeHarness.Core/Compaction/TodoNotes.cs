using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;
using System.Text;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// Adds the agent's current to-do list to the end of a summary, so compaction never hides it from the model.
/// </summary>
/// <remarks>
/// The list is written after whatever the summarizer produced and is cut off again before an older summary is given back to a summarizer,
/// so the list is always the latest one and the summarizers never have to know about it.
/// </remarks>
internal static class TodoNotes
{
    private const string Heading = "[Todo list, as the agent last set it with update_todos]";

    /// <summary>
    /// Add the current to-do list to a summary.
    /// </summary>
    /// <param name="summary">The summary.</param>
    /// <param name="messages">The whole conversation, which the list is read from.</param>
    /// <returns>The summary with the list after it, or the summary as it was when the conversation has no list.</returns>
    public static string Append(string summary, IEnumerable<ChatMessage> messages)
    {
        var list = TodoList.From(messages);
        if (list.Items.Count == 0)
        {
            return summary;
        }

        var text = new StringBuilder(summary).Append("\n\n").Append(Heading);
        foreach (var item in list.Items)
        {
            text.Append("\n- ").Append(item.Status switch
            {
                TodoStatus.Completed => "done: ",
                TodoStatus.InProgress => "in progress: ",
                _ => "not started: ",
            }).Append(item.Text);
        }

        return text.ToString();
    }

    /// <summary>
    /// Remove the to-do list that <see cref="Append"/> added from a summary.
    /// </summary>
    /// <param name="summary">The summary, or null.</param>
    /// <returns>The summary without the list, or null when it was null.</returns>
    public static string? Strip(string? summary)
    {
        if (summary is null)
        {
            return null;
        }

        var at = summary.LastIndexOf("\n\n" + Heading + "\n", StringComparison.Ordinal);
        return at < 0 ? summary : summary[..at];
    }
}
