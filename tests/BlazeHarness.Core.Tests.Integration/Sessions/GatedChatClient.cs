using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace BlazeHarness.Core.Tests.Integration.Sessions;

/// <summary>A chat client that streams its first chunk, then waits for the test before streaming the rest.</summary>
internal sealed class GatedChatClient : IChatClient
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes once the first chunk has been streamed.</summary>
#pragma warning disable VSTHRD003 // The source is completed by this class.
    public Task Started => _started.Task;
#pragma warning restore VSTHRD003

    /// <summary>Let the rest of the reply stream.</summary>
    public void Release() => _gate.TrySetResult();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hel")]);
        _started.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("lo")]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
