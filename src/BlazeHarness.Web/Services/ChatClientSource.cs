using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Web.Services;

/// <summary>
/// Creates the chat client for a provider, looking up its key and using a client that allows long, slow replies.
/// </summary>
/// <param name="providers">The provider service, which resolves API keys.</param>
/// <param name="httpClients">The factory for HTTP clients.</param>
public sealed class ChatClientSource(ProviderService providers, IHttpClientFactory httpClients)
{
    /// <summary>
    /// The name of the HTTP client used for chat requests.
    /// </summary>
    public const string HttpClientName = "chat";

    /// <summary>
    /// Create a chat client for a target.
    /// </summary>
    /// <param name="target">The provider and model to talk to.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The chat client, which the caller disposes.</returns>
    public async Task<IChatClient> CreateAsync(ChatTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var key = await providers.ResolveKeyAsync(target.Provider, cancellationToken);
#pragma warning disable IDISP004 // The factory-managed handler is pooled, and disposing this wrapper client would not free anything useful.
        return ChatClientFactory.Create(target.Provider, target.Model.Id, key, httpClients.CreateClient(HttpClientName));
#pragma warning restore IDISP004
    }
}
