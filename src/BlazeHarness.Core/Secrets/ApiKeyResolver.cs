using BlazeHarness.Core.Providers;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Finds a provider's API key: an environment variable reference, else the secret store.
/// </summary>
/// <param name="store">The secret store consulted when the provider has no environment variable reference.</param>
/// <param name="environment">Reads an environment variable by name; defaults to the process environment. Replaceable for tests.</param>
public sealed partial class ApiKeyResolver(ISecretStore store, Func<string, string?>? environment = null)
{
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    /// <summary>
    /// Look up the API key for a provider.
    /// </summary>
    /// <remarks>
    /// A <see cref="ProviderConfig.ApiKeyRef"/> that is not a valid <c>${NAME}</c> reference resolves to no key,
    /// rather than being treated as the key itself.
    /// </remarks>
    /// <param name="provider">The provider to find the key for.</param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The key, or null when none is configured (for keyless local servers).</returns>
    public async Task<string?> ResolveAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(provider.ApiKeyRef))
        {
            var match = EnvReference().Match(provider.ApiKeyRef);
            return match.Success ? _environment(match.Groups[1].Value) : null;
        }

        return await store.GetAsync(SecretStoreFactory.KeyFor(provider), cancellationToken);
    }

    /// <summary>
    /// Check whether a key reference has the <c>${ENV_VAR}</c> form.
    /// </summary>
    /// <param name="reference">The reference to check.</param>
    /// <returns>True when it names an environment variable.</returns>
    public static bool IsEnvironmentReference(string reference)
    {
        return EnvReference().IsMatch(reference);
    }

    [GeneratedRegex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$")]
    private static partial Regex EnvReference();
}
