using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// Writes summaries without a model, by picking what to keep from the messages and formatting it.
/// </summary>
/// <remarks>
/// <para>
/// The summary has short sections of one-line entries: the first requests, the files touched, the latest commands, the user's newest messages
/// and the latest steps. Each section is capped, and a new summary merges the previous one into it, so repeated compactions stay about the same size.
/// Tool output is never copied; only the names, paths and commands of the calls are, with a note when one failed.
/// </para>
/// <para>
/// The same messages always give the same summary, and the text is copied as written, so it works in any language.
/// What is left out can be found again with the <c>search_chat</c> tool.
/// </para>
/// </remarks>
internal sealed class ExtractiveSummarizer : ISummarizer
{
    private const string Requests = "Opening asks";
    private const string Files = "Touched files";
    private const string Commands = "Shell runs";
    private const string UserMessages = "Later asks";
    private const string Steps = "Latest actions";
    private const string Footer = "Details left out of these notes can be looked up with the search_chat tool, when it is available.";
    private const int MaxRequests = 2;
    private const int MaxFiles = 25;
    private const int MaxCommands = 12;
    private const int MaxUserMessages = 12;
    private const int MaxSteps = 30;
    private const int MessageCharacters = 240;
    private const int StepCharacters = 140;
    private const int PathCharacters = 200;
    private const int EarlierNotesCharacters = 2_000;
    private const string ChangedPrefix = "- changed ";
    private const string ReadPrefix = "- read ";

    private static readonly string[] SectionOrder = [Requests, Files, Commands, UserMessages, Steps];

    /// <inheritdoc />
    public Task<string> SummarizeAsync(string? previousSummary, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var sections = Parse(previousSummary);
        Fold(sections, messages, firstSummary: string.IsNullOrWhiteSpace(previousSummary));
        return Task.FromResult(Render(sections));
    }

    private static Dictionary<string, List<string>> Parse(string? summary)
    {
        var sections = SectionOrder.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return sections;
        }

        List<string>? current = null;
        foreach (var line in summary.Split('\n'))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = sections.GetValueOrDefault(line[1..^1]);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                current?.Add(line.TrimEnd());
            }
        }

        // A summary written by the model has no sections, so it is kept as it was instead of being dropped.
        if (sections.Values.All(entries => entries.Count == 0))
        {
            sections[Requests].Add("- (earlier notes) " + Flatten(summary, EarlierNotesCharacters));
        }

        return sections;
    }

    private static void Fold(Dictionary<string, List<string>> sections, IReadOnlyList<ChatMessage> messages, bool firstSummary)
    {
        var failed = messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Where(r => r.Result?.ToString()?.StartsWith("Error:", StringComparison.Ordinal) == true)
            .Select(r => r.CallId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent { Text: { Length: > 0 } text } when message.Role == ChatRole.User:
                        if (firstSummary && sections[Requests].Count < MaxRequests)
                        {
                            sections[Requests].Add("- " + Flatten(text, MessageCharacters));
                        }

                        sections[UserMessages].Add("- " + Flatten(text, MessageCharacters));
                        sections[Steps].Add("- user: " + Flatten(text, StepCharacters));
                        break;
                    case TextContent { Text: { Length: > 0 } text } when message.Role == ChatRole.Assistant:
                        sections[Steps].Add("- assistant: " + Flatten(text, StepCharacters));
                        break;
                    case FunctionCallContent call:
                        FoldCall(sections, call, failed.Contains(call.CallId));
                        break;
                    default:
                        break;
                }
            }
        }

        Cap(sections[UserMessages], MaxUserMessages);
        Cap(sections[Steps], MaxSteps);
        Cap(sections[Commands], MaxCommands);
        CapFiles(sections[Files]);
    }

    private static void FoldCall(Dictionary<string, List<string>> sections, FunctionCallContent call, bool failed)
    {
        var path = Argument(call, "path");
        var command = Argument(call, "command");
        var note = failed ? " (failed)" : string.Empty;

        if (path is not null && call.Name is "write_file" or "edit_file")
        {
            AddFile(sections[Files], ChangedPrefix, path);
        }
        else if (path is not null && call.Name == "read_file")
        {
            AddFile(sections[Files], ReadPrefix, path);
        }

        if (command is not null && call.Name == "shell")
        {
            sections[Commands].Add("- " + Flatten(command, StepCharacters));
        }

        var target = command ?? path;
        sections[Steps].Add(target is null ? $"- {call.Name}{note}" : $"- {call.Name} {Flatten(target, StepCharacters)}{note}");
    }

    private static void AddFile(List<string> files, string prefix, string path)
    {
        var entry = prefix + Flatten(path, PathCharacters);
        var other = (prefix == ChangedPrefix ? ReadPrefix : ChangedPrefix) + entry[prefix.Length..];

        // A file that was changed stays listed as changed even if it is read afterwards.
        if (prefix == ReadPrefix && files.Contains(other))
        {
            return;
        }

        files.Remove(other);
        files.Remove(entry);
        files.Add(entry);
    }

    private static void CapFiles(List<string> files)
    {
        var changed = files.Where(f => f.StartsWith(ChangedPrefix, StringComparison.Ordinal)).TakeLast(MaxFiles);
        var read = files.Where(f => f.StartsWith(ReadPrefix, StringComparison.Ordinal)).TakeLast(MaxFiles);
        var kept = changed.Concat(read).ToList();
        files.Clear();
        files.AddRange(kept);
    }

    private static void Cap(List<string> entries, int max)
    {
        if (entries.Count > max)
        {
            entries.RemoveRange(0, entries.Count - max);
        }
    }

    private static string? Argument(FunctionCallContent call, string name)
    {
        if (call.Arguments is null || !call.Arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            string text => text,
            _ => null,
        };
    }

    // One line, so an entry can never look like a section heading or a second entry.
    private static string Flatten(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= max ? line : string.Concat(line.AsSpan(0, max), "…");
    }

    private static string Render(Dictionary<string, List<string>> sections)
    {
        var text = new StringBuilder();
        foreach (var name in SectionOrder.Where(name => sections[name].Count > 0))
        {
            text.Append('[').Append(name).Append("]\n");
            foreach (var entry in sections[name])
            {
                text.Append(entry).Append('\n');
            }

            text.Append('\n');
        }

        return text.Append(Footer).ToString();
    }
}
