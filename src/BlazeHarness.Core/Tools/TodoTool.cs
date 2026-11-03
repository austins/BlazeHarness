using BlazeHarness.Core.Agent;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Lets the model keep a short to-do list for a long task, which the chat shows to the user.
/// </summary>
/// <remarks>
/// Each call replaces the whole list. The tool changes nothing on disk and reads no paths, so it is read-only for the permission check.
/// The list itself is read back from the saved calls (see <see cref="TodoList"/>).
/// </remarks>
internal sealed class TodoTool : ITool
{
    /// <inheritdoc />
    public string Name => TodoList.ToolName;

    /// <inheritdoc />
    public string Description =>
        "Keep a short todo list for the current task, which the user sees in the chat. Use it for work with several steps, such as a change that touches many files, " +
        "and not for a simple question or a one or two step task. Write the list when you start, keep exactly one item in progress while you work, " +
        "and update it as you go: mark an item completed as soon as it is done, and add or remove items when the plan changes. When the last step is your final reply, mark it completed in the call just before you write that reply. " +
        "Each call replaces the whole list, so send every item every time. " +
        $"Items are short one-line steps (at most {TodoList.MaxTextLength} characters), and a list has at most {TodoList.MaxItems} items. " +
        "Do not put details, code or results in the list; say those in your reply.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "{{TodoList.ItemsArgument}}": {
              "type": "array",
              "maxItems": {{TodoList.MaxItems}},
              "description": "The whole list, in order.",
              "items": {
                "type": "object",
                "properties": {
                  "{{TodoList.TextArgument}}": { "type": "string", "maxLength": {{TodoList.MaxTextLength}}, "description": "The step, on one line." },
                  "{{TodoList.StatusArgument}}": { "type": "string", "enum": ["{{TodoList.PendingName}}", "{{TodoList.InProgressName}}", "{{TodoList.CompletedName}}"], "description": "Where the step stands. Keep exactly one in progress." }
                },
                "required": ["{{TodoList.TextArgument}}", "{{TodoList.StatusArgument}}"]
              }
            }
          },
          "required": ["{{TodoList.ItemsArgument}}"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        try
        {
            var list = TodoList.Parse(new ToolArguments(arguments));
            return string.Create(CultureInfo.InvariantCulture, $"{list.CompletedCount} of {list.Items.Count} done");
        }
        catch (ToolArgumentException)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var list = TodoList.Parse(arguments);
        return Task.FromResult(ToolResult.Ok(string.Create(CultureInfo.InvariantCulture, $"{TodoList.UpdatedPrefix}: {list.CompletedCount} of {list.Items.Count} done.")));
    }
}
