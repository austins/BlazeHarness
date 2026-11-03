using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// The agent's to-do list: the steps of a longer task and where each stands.
/// </summary>
/// <remarks>
/// The list is not stored on its own. Each <c>update_todos</c> call replaces the whole list, so the current list is the arguments of the last call
/// that succeeded, and rewinding or editing the chat rolls the list back with the messages.
/// </remarks>
public sealed class TodoList
{
    /// <summary>
    /// The name the model calls the tool that updates the list by.
    /// </summary>
    public const string ToolName = "update_todos";

    /// <summary>
    /// The most items a list may have.
    /// </summary>
    public const int MaxItems = 20;

    /// <summary>
    /// The most characters an item's text may have.
    /// </summary>
    public const int MaxTextLength = 150;

    /// <summary>
    /// The text a successful <c>update_todos</c> result starts with, which marks the call as one that replaced the list.
    /// </summary>
    internal const string UpdatedPrefix = "Todo list updated";

    internal const string ItemsArgument = "items";
    internal const string TextArgument = "text";
    internal const string StatusArgument = "status";
    internal const string PendingName = "pending";
    internal const string InProgressName = "in_progress";
    internal const string CompletedName = "completed";

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoList"/> class.
    /// </summary>
    /// <param name="items">The items, in order.</param>
    public TodoList(IReadOnlyList<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
    }

    /// <summary>
    /// Gets a list with no items.
    /// </summary>
    public static TodoList Empty { get; } = new([]);

    /// <summary>
    /// Gets the items, in order.
    /// </summary>
    public IReadOnlyList<TodoItem> Items { get; }

    /// <summary>
    /// Gets how many items are completed.
    /// </summary>
    public int CompletedCount => Items.Count(item => item.Status == TodoStatus.Completed);

    /// <summary>
    /// Gets the item to show as the one being worked on: the first one in progress, otherwise the first one not started, or null when there is none.
    /// </summary>
    public TodoItem? Current =>
        Items.FirstOrDefault(item => item.Status == TodoStatus.InProgress) ?? Items.FirstOrDefault(item => item.Status == TodoStatus.Pending);

    /// <summary>
    /// Get the list that the last successful <c>update_todos</c> call set.
    /// </summary>
    /// <param name="entries">The messages of the chat, oldest first.</param>
    /// <returns>The list, or an empty list when the chat has none.</returns>
    public static TodoList From(IEnumerable<ChatEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        TodoCache cache = new();
        return FromCached(entries as IReadOnlyList<ChatEntry> ?? [.. entries], cache);
    }

    /// <summary>
    /// Read the list from the arguments of an <c>update_todos</c> call, for showing the call.
    /// </summary>
    /// <param name="argumentsJson">The call arguments as a JSON object string.</param>
    /// <returns>The list, or null when the arguments are not a valid list.</returns>
    public static TodoList? TryParse(string argumentsJson)
    {
        try
        {
            return Parse(ToolArguments.Parse(argumentsJson));
        }
        catch (ToolArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the list that the last successful <c>update_todos</c> call set, parsing it only when that call is not the one the cache holds.
    /// </summary>
    /// <param name="entries">The messages of the chat, oldest first.</param>
    /// <param name="cache">The last call that was read and its list; it is updated when a different call is found.</param>
    /// <returns>The list, or an empty list when the chat has none.</returns>
    internal static TodoList FromCached(IReadOnlyList<ChatEntry> entries, TodoCache cache)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var calls = entries[i].ToolCalls;
            for (var j = calls.Count - 1; j >= 0; j--)
            {
                var call = calls[j];
                if (call.Name != ToolName || call.Status != ToolCallStatus.Succeeded)
                {
                    continue;
                }

                if (cache.TryGet(call, out var known))
                {
                    return known;
                }

                if (TryParse(call.ArgumentsJson) is { } list)
                {
                    cache.Set(call, list);
                    return list;
                }
            }
        }

        return Empty;
    }

    /// <summary>
    /// Get the list that the last successful <c>update_todos</c> call set, from the messages sent to the model.
    /// </summary>
    /// <param name="messages">The conversation, oldest first.</param>
    /// <returns>The list, or an empty list when the conversation has none.</returns>
    internal static TodoList From(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var all = messages.ToList();
        var succeeded = all
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Where(r => r.Result?.ToString()?.StartsWith(UpdatedPrefix, StringComparison.Ordinal) == true)
            .Select(r => r.CallId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var call in all.AsEnumerable().Reverse().SelectMany(m => m.Contents.OfType<FunctionCallContent>().Reverse()))
        {
            if (call.Name == ToolName && succeeded.Contains(call.CallId)
                && TryParse(JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions)) is { } list)
            {
                return list;
            }
        }

        return Empty;
    }

    /// <summary>
    /// Read the list from the arguments of an <c>update_todos</c> call.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The list.</returns>
    /// <exception cref="ToolArgumentException">The arguments are not a valid list; the message says what to fix.</exception>
    internal static TodoList Parse(ToolArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.OptionalElement(ItemsArgument) is not { ValueKind: JsonValueKind.Array } array)
        {
            throw new ToolArgumentException($"The '{ItemsArgument}' argument is required: an array of {{\"{TextArgument}\", \"{StatusArgument}\"}} objects with the whole list.");
        }

        var count = array.GetArrayLength();
        if (count > MaxItems)
        {
            throw new ToolArgumentException($"The list has {count} items, but at most {MaxItems} are allowed. Merge steps or leave out small ones.");
        }

        var items = new List<TodoItem>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(ParseItem(array[i], i + 1));
        }

        return new TodoList(items);
    }

    private static TodoItem ParseItem(JsonElement element, int number)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ToolArgumentException($"Item {number} must be an object with '{TextArgument}' and '{StatusArgument}'.");
        }

        if (!element.TryGetProperty(TextArgument, out var textElement) || textElement.ValueKind != JsonValueKind.String)
        {
            throw new ToolArgumentException($"Item {number} needs a '{TextArgument}' string.");
        }

        var text = textElement.GetString()!.Trim();
        if (text.Length == 0)
        {
            throw new ToolArgumentException($"Item {number} has empty text.");
        }

        if (text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal))
        {
            throw new ToolArgumentException($"Item {number} must be one line. Keep each item to a short step and put details in your reply.");
        }

        if (text.Length > MaxTextLength)
        {
            throw new ToolArgumentException($"Item {number} has {text.Length} characters, but at most {MaxTextLength} are allowed. Shorten it.");
        }

        var status = element.TryGetProperty(StatusArgument, out var statusElement) && statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() : null;
        return status switch
        {
            PendingName => new TodoItem(text, TodoStatus.Pending),
            InProgressName => new TodoItem(text, TodoStatus.InProgress),
            CompletedName => new TodoItem(text, TodoStatus.Completed),
            _ => throw new ToolArgumentException($"Item {number} needs a '{StatusArgument}' of '{PendingName}', '{InProgressName}' or '{CompletedName}'."),
        };
    }
}
