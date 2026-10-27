using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace BlazeHarness.Tests.Shared.Agent;

/// <summary>A chat client that replays scripted replies, one per request, and records what it was sent.</summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Script> _scripts = new();
    private readonly List<IReadOnlyList<ChatMessage>> _requests = [];
    private readonly List<ChatOptions?> _options = [];

    /// <summary>Initializes a new instance of the <see cref="ScriptedChatClient"/> class with its first scripted reply.</summary>
    /// <param name="updates">The updates to stream for the first request; empty when null.</param>
    /// <param name="failure">An exception to throw after the updates, or null.</param>
    /// <param name="cancelAfterwards">A source to cancel after the updates, or null.</param>
    public ScriptedChatClient(
        IReadOnlyList<ChatResponseUpdate>? updates = null,
        Exception? failure = null,
        CancellationTokenSource? cancelAfterwards = null)
    {
        Enqueue(updates ?? [], failure, cancelAfterwards);
    }

    /// <summary>Gets the messages of each request, as they were when the request was made.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Requests => _requests;

    /// <summary>Gets the options of each request.</summary>
    public IReadOnlyList<ChatOptions?> Options => _options;

    /// <summary>Add the reply for the next request that has no scripted reply yet.</summary>
    /// <param name="updates">The updates to stream.</param>
    /// <param name="failure">An exception to throw after the updates, or null.</param>
    /// <param name="cancelAfterwards">A source to cancel after the updates, or null.</param>
    /// <returns>This client, so calls can be chained.</returns>
    public ScriptedChatClient Enqueue(
        IReadOnlyList<ChatResponseUpdate> updates,
        Exception? failure = null,
        CancellationTokenSource? cancelAfterwards = null)
    {
        _scripts.Enqueue(new Script(updates, failure, cancelAfterwards));
        return this;
    }

    /// <summary>Create an update that carries assistant text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The update.</returns>
    public static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, [new TextContent(text)]);

    /// <summary>Create an update that asks for a tool call.</summary>
    /// <param name="callId">The call's identifier.</param>
    /// <param name="name">The tool's name.</param>
    /// <param name="argumentsJson">The call's arguments as a JSON object.</param>
    /// <returns>The update.</returns>
    public static ChatResponseUpdate Call(string callId, string name, string argumentsJson = "{}")
    {
        var arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _requests.Add([.. messages]);
        _options.Add(options);
        var script = _scripts.Count > 0 ? _scripts.Dequeue() : new Script([], null, null);

        foreach (var update in script.Updates)
        {
            yield return update;
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (script.CancelAfterwards is not null)
        {
            await script.CancelAfterwards.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (script.Failure is not null)
        {
            throw script.Failure;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed record Script(
        IReadOnlyList<ChatResponseUpdate> Updates,
        Exception? Failure,
        CancellationTokenSource? CancelAfterwards);
}
