using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Decides how attached files reach the model.
/// </summary>
public static class AttachmentPolicy
{
    private const int DefaultInlineTokens = 4_000;
    private const int WindowDivisor = 10;

    /// <summary>
    /// Get whether a model accepts images.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>True when the model takes image input.</returns>
    public static bool AcceptsImages(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Input.HasFlag(InputTypes.Image);
    }

    /// <summary>
    /// Get how many tokens of attached text a single message may carry inline for a model.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>A tenth of the context window, or a fixed amount when the window is not known.</returns>
    public static int InlineTokenBudget(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.ContextWindow > 0 ? model.ContextWindow / WindowDivisor : DefaultInlineTokens;
    }

    /// <summary>
    /// Decide how each file of a message reaches a model.
    /// </summary>
    /// <remarks>
    /// Images are sent as images. Text files go inline while together they fit the model's inline budget, and the
    /// rest are read on demand. A file that doesn't fit never takes space from the files after it.
    /// </remarks>
    /// <param name="files">The files, in the order they were attached.</param>
    /// <param name="model">The model the message is sent to.</param>
    /// <returns>The files with their delivery.</returns>
    public static IReadOnlyList<SentAttachment> Plan(IReadOnlyList<AttachmentInfo> files, ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(files);

        var deliveries = Deliveries([.. files.Select(file => (file.IsImage, file.Size))], model);
        return [.. files.Select((file, index) => new SentAttachment(file, deliveries[index]))];
    }

    /// <summary>
    /// Decide how each file reaches a model from its kind and size alone, which the composer uses before the files are saved.
    /// </summary>
    /// <remarks>
    /// The rules are those of <see cref="Plan"/>.
    /// </remarks>
    /// <param name="files">Whether each file is an image, and its size in bytes, in the order they were attached.</param>
    /// <param name="model">The model the message is sent to.</param>
    /// <returns>The delivery of each file, in the same order.</returns>
    public static IReadOnlyList<AttachmentDelivery> Deliveries(IReadOnlyList<(bool IsImage, long Size)> files, ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(model);

        var left = (long)InlineTokenBudget(model);
        List<AttachmentDelivery> planned = [];
        foreach (var (isImage, size) in files)
        {
            if (isImage)
            {
                planned.Add(AttachmentDelivery.Image);
                continue;
            }

            var tokens = EstimateTokens(size);
            if (tokens <= left)
            {
                left -= tokens;
                planned.Add(AttachmentDelivery.Inline);
            }
            else
            {
                planned.Add(AttachmentDelivery.OnDemand);
            }
        }

        return planned;
    }

    /// <summary>
    /// Estimate how many tokens a text file takes.
    /// </summary>
    /// <param name="bytes">The file size in bytes.</param>
    /// <returns>About one token for every four bytes.</returns>
    public static long EstimateTokens(long bytes) => (bytes + 3) / 4;
}
