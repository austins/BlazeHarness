using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Runs one assistant turn against a chat client and reports it as a stream of <see cref="AgentEvent"/> values.
/// </summary>
/// <remarks>
/// This is the tool-less turn. The tool loop of a later phase wraps the same event stream.
/// </remarks>
public static class ChatRunner
{
    /// <summary>
    /// Stream the assistant's reply to the conversation so far.
    /// </summary>
    /// <remarks>
    /// The reply is appended to <paramref name="history"/> when the run ends, including a partial reply
    /// after a cancellation or an error, so the next turn sees what the user saw. The stream always ends with a <see cref="Done"/>.
    /// </remarks>
    /// <param name="client">The chat client to call.</param>
    /// <param name="history">The conversation so far, ending with the user's message.</param>
    /// <param name="options">The request options, including sampling and thinking fields.</param>
    /// <param name="cancellationToken">A token that stops the run.</param>
    /// <returns>The events of the run.</returns>
    public static IAsyncEnumerable<AgentEvent> RunAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(history);

        return RunCoreAsync(client, history, options, cancellationToken);
    }

    private static async IAsyncEnumerable<AgentEvent> RunCoreAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reply = new StringBuilder();
        var reason = StopReason.Completed;
        string? error = null;

        var stream = client.GetStreamingResponseAsync(history, options, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ChatResponseUpdate update;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                update = enumerator.Current;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                reason = StopReason.Cancelled;
                break;
            }
            catch (Exception ex)
            {
                reason = StopReason.Error;
                error = ex.Message;
                break;
            }

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent { Text.Length: > 0 } thinking:
                        yield return new ReasoningDelta(thinking.Text);
                        break;
                    case TextContent { Text.Length: > 0 } text:
                        reply.Append(text.Text);
                        yield return new TextDelta(text.Text);
                        break;
                    case UsageContent usage:
                        yield return new UsageReported(
                            usage.Details.InputTokenCount ?? 0,
                            usage.Details.OutputTokenCount ?? 0,
                            usage.Details.CachedInputTokenCount ?? 0);
                        break;
                    default:
                        break;
                }
            }
        }

        if (reply.Length > 0)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
        }

        if (error is not null)
        {
            yield return new AgentError(error);
        }

        yield return new Done(reason);
    }
}
