using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Replaces images with a note in every request, when the model can't view them.
/// </summary>
/// <remarks>
/// Images are dropped when a request is sent rather than from the history, so a picture a tool call adds during a reply is dropped too
/// and the history keeps it for a later model that accepts images.
/// </remarks>
/// <param name="inner">The client that sends the requests. Disposing this client disposes it.</param>
/// <param name="acceptsImages">Whether the model accepts images, in which case requests are passed on unchanged.</param>
internal sealed class ImageOmittingChatClient(IChatClient inner, bool acceptsImages) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(Omit(messages), options, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(Omit(messages), options, cancellationToken);
    }

    private IEnumerable<ChatMessage> Omit(IEnumerable<ChatMessage> messages)
    {
        if (acceptsImages)
        {
            return messages;
        }

        List<ChatMessage> copy = [.. messages];
        AttachmentMessage.OmitImages(copy);
        return copy;
    }
}
