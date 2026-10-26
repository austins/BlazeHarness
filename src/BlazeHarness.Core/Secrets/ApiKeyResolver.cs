using BlazeHarness.Core.Providers;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Secrets;

/// <summary>Finds a provider's API key: an environment variable reference, else the secret store.</summary>
public sealed partial class ApiKeyResolver(ISecretStore store, Func<string, string?>? environment = null)
{
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    public async Task<string?> ResolveAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(provider.ApiKeyRef))
        {
            var match = EnvReference().Match(provider.ApiKeyRef);
            return match.Success ? _environment(match.Groups[1].Value) : null;
        }

        return await store.GetAsync(SecretStoreFactory.KeyFor(provider), cancellationToken);
    }

    [GeneratedRegex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$")]
    private static partial Regex EnvReference();
}
