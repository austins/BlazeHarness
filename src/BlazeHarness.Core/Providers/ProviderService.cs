using BlazeHarness.Core.Config;
using BlazeHarness.Core.Secrets;
using System.Text;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Reads and changes the configured providers, keeping their API keys in the secret store rather than in <c>config.json</c>.
/// </summary>
/// <param name="config">The store behind <c>config.json</c>.</param>
/// <param name="secrets">Where API keys are kept.</param>
/// <param name="keys">Resolves a provider's key, including environment variable references.</param>
public sealed class ProviderService(JsonFileStore<AppConfig> config, ISecretStore secrets, ApiKeyResolver keys)
{
    /// <summary>
    /// Raised after the configuration changed, whether through this service or an outside edit of the file.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    public AppConfig Config => config.Current;

    /// <summary>
    /// Gets the secret store, so the UI can say whether keys are held securely.
    /// </summary>
    public ISecretStore Secrets => secrets;

    /// <summary>
    /// Build a provider id from a display name, avoiding ids that are already taken.
    /// </summary>
    /// <param name="displayName">The name the user typed.</param>
    /// <returns>A lowercase, hyphenated id that no configured provider uses.</returns>
    public string NewId(string displayName)
    {
        var slug = new StringBuilder();
        foreach (var ch in displayName.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                slug.Append(char.ToLowerInvariant(ch));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var baseId = slug.ToString().Trim('-');
        if (baseId.Length == 0)
        {
            baseId = "provider";
        }

        var id = baseId;
        var n = 2;
        while (config.Current.Providers.Any(p => p.Id == id))
        {
            id = $"{baseId}-{n++}";
        }

        return id;
    }

    /// <summary>
    /// Look up the API key a provider would use, for testing a connection or starting a chat.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The key, or null when none is configured.</returns>
    public Task<string?> ResolveKeyAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        return keys.ResolveAsync(provider, cancellationToken);
    }

    /// <summary>
    /// Check whether a key is available for a provider, without returning it.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>True when a key is stored, or the referenced environment variable is set.</returns>
    public async Task<bool> HasKeyAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        return !string.IsNullOrEmpty(await keys.ResolveAsync(provider, cancellationToken));
    }

    /// <summary>
    /// Add a provider, or replace the one with the same id.
    /// </summary>
    /// <param name="provider">The provider to save.</param>
    /// <param name="newApiKey">A new key to store, or null to leave the stored key unchanged.</param>
    /// <param name="cancellationToken">A token to cancel the save.</param>
    /// <returns>The validation problems. The provider is saved only when the list is empty.</returns>
    public async Task<IReadOnlyList<ValidationIssue>> SaveAsync(
        ProviderConfig provider,
        string? newApiKey,
        CancellationToken cancellationToken = default)
    {
        var issues = ProviderValidator.Validate(provider);
        if (issues.Count > 0)
        {
            return issues;
        }

        if (!string.IsNullOrEmpty(newApiKey))
        {
            await secrets.SetAsync(SecretStoreFactory.KeyFor(provider), newApiKey, cancellationToken);
        }

        await config.UpdateAsync(
            current =>
            {
                var providers = current.Providers.ToList();
                var index = providers.FindIndex(p => p.Id == provider.Id);
                if (index >= 0)
                {
                    providers[index] = provider;
                }
                else
                {
                    providers.Add(provider);
                }

                var next = current with { Providers = providers };
                return next.DefaultProviderId is null ? next with { DefaultProviderId = provider.Id } : next;
            },
            cancellationToken);

        Changed?.Invoke(this, EventArgs.Empty);
        return [];
    }

    /// <summary>
    /// Remove a provider and its stored key.
    /// </summary>
    /// <remarks>
    /// If it was the default provider, the default moves to another provider, or is cleared when none is left.
    /// </remarks>
    /// <param name="providerId">The id of the provider to remove.</param>
    /// <param name="cancellationToken">A token to cancel the removal.</param>
    /// <returns>A task that completes once the provider is removed.</returns>
    public async Task RemoveAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await secrets.DeleteAsync("provider:" + providerId, cancellationToken);
        await config.UpdateAsync(
            current =>
            {
                var providers = current.Providers.Where(p => p.Id != providerId).ToList();
                var next = current with { Providers = providers };
                if (next.DefaultProviderId != providerId)
                {
                    return next;
                }

                var first = providers.Count > 0 ? providers[0] : null;
                return next with { DefaultProviderId = first?.Id, DefaultModelId = first is { Models: [var model, ..] } ? model.Id : null };
            },
            cancellationToken);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Choose the provider and model preselected for new chats.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <param name="modelId">The model id, which must belong to the provider.</param>
    /// <param name="cancellationToken">A token to cancel the save.</param>
    /// <returns>True when saved; false when the provider or model does not exist.</returns>
    public async Task<bool> SetDefaultAsync(string providerId, string modelId, CancellationToken cancellationToken = default)
    {
        var provider = config.Current.Providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || provider.Models.All(m => m.Id != modelId))
        {
            return false;
        }

        await config.UpdateAsync(current => current with { DefaultProviderId = providerId, DefaultModelId = modelId }, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
