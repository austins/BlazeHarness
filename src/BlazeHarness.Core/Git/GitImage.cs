namespace BlazeHarness.Core.Git;

/// <summary>
/// The picture file that a change leaves in the working tree.
/// </summary>
/// <param name="MediaType">The media type, decided from the file's first bytes: <c>image/png</c>, <c>image/jpeg</c>, <c>image/gif</c> or <c>image/webp</c>.</param>
/// <param name="Content">The file's bytes.</param>
#pragma warning disable CA1819 // The picture's bytes are handed to a response as they are; a copy per read would only waste memory.
public sealed record GitImage(string MediaType, byte[] Content);
#pragma warning restore CA1819
