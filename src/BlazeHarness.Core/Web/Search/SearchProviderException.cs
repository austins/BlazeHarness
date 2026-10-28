namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// A search provider failed in a way that is worth reporting and, for some kinds, retrying.
/// </summary>
/// <remarks>
/// The message never holds the query or anything the provider returned.
/// </remarks>
public sealed class SearchProviderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchProviderException"/> class.
    /// </summary>
    /// <param name="failure">What went wrong.</param>
    /// <param name="retryAfter">How long the provider asked to wait, when it said.</param>
    public SearchProviderException(SearchFailure failure, TimeSpan? retryAfter = null)
        : base($"The search provider failed: {failure}.")
    {
        Failure = failure;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets what went wrong.
    /// </summary>
    public SearchFailure Failure { get; }

    /// <summary>
    /// Gets how long the provider asked to wait, or null when it did not say.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
