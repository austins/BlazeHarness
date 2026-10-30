using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Sends the model's earlier thinking back as <c>reasoning_content</c> on the assistant messages of the current turn.
/// </summary>
/// <remarks>
/// <para>
/// The OpenAI client reads <c>reasoning_content</c> from the stream but leaves <see cref="TextReasoningContent"/> out of requests,
/// so without this a model would lose what it worked out after every tool call. Chat templates such as Qwen's keep that thinking for the
/// assistant messages after the last user message, which is where this sends it; thinking from earlier turns stays out, as those templates drop it too.
/// </para>
/// <para>
/// Thinking only exists when the server sent it in that field, so a server that doesn't know the field never receives it.
/// </para>
/// </remarks>
/// <param name="inner">The client that sends the request.</param>
internal sealed class ReasoningReplayChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(WithReasoning(messages), options, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(WithReasoning(messages), options, cancellationToken);
    }

    /// <summary>
    /// Give each assistant message after the last user message that carries thinking a raw form with <c>reasoning_content</c> set.
    /// </summary>
    /// <param name="messages">The messages of the request.</param>
    /// <returns>The messages to send.</returns>
    internal static List<ChatMessage> WithReasoning(IEnumerable<ChatMessage> messages)
    {
        var list = messages.ToList();
        var lastUser = list.FindLastIndex(m => m.Role == ChatRole.User);
        for (var i = lastUser + 1; i < list.Count; i++)
        {
            var message = list[i];
            if (message.Role == ChatRole.Assistant && message.RawRepresentation is null && Reasoning(message) is { Length: > 0 } reasoning)
            {
                list[i] = new ChatMessage(message.Role, message.Contents)
                {
                    AuthorName = message.AuthorName,
                    MessageId = message.MessageId,
                    RawRepresentation = ToRaw(message, reasoning),
                };
            }
        }

        return list;
    }

    private static string Reasoning(ChatMessage message)
    {
        return string.Concat(message.Contents.OfType<TextReasoningContent>().Select(r => r.Text));
    }

    // Built the way the OpenAI client builds an assistant message, so the request is the same apart from the added field.
    private static AssistantChatMessage ToRaw(ChatMessage message, string reasoning)
    {
        var text = message.Contents.OfType<TextContent>().Where(t => t.Text.Length > 0).Select(t => ChatMessageContentPart.CreateTextPart(t.Text)).ToList();
        var calls = message.Contents.OfType<FunctionCallContent>()
            .Select(call => ChatToolCall.CreateFunctionToolCall(
                call.CallId,
                call.Name,
                new BinaryData(JsonSerializer.SerializeToUtf8Bytes(call.Arguments, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))))))
            .ToList();

        AssistantChatMessage raw;
        if (text.Count > 0)
        {
            raw = new AssistantChatMessage(text);
            foreach (var call in calls)
            {
                raw.ToolCalls.Add(call);
            }
        }
        else
        {
            raw = calls.Count > 0 ? new AssistantChatMessage(calls) : new AssistantChatMessage(ChatMessageContentPart.CreateTextPart(string.Empty));
        }

        raw.Patch.Set("$.reasoning_content"u8, reasoning);
        return raw;
    }
}
