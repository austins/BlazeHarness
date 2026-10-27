using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Chooses the secret store backend for the current OS.
/// </summary>
public static class SecretStoreFactory
{
    /// <summary>
    /// Create the best secret store available on this machine.
    /// </summary>
    /// <remarks>
    /// Windows uses DPAPI, macOS the Keychain, and Linux the Secret Service (libsecret) when it responds.
    /// Otherwise it falls back to an owner-only file, which reports <see cref="ISecretStore.IsSecure"/> as false.
    /// </remarks>
    /// <param name="paths">The app paths, for the file-backed stores.</param>
    /// <param name="cancellationToken">A token to cancel probing for the OS store.</param>
    /// <returns>The store. The caller owns it and must dispose it.</returns>
    public static async Task<ISecretStore> CreateAsync(AppPaths paths, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretStore(paths.SecretsFile);
        }

        if (MacKeychainStore.IsAvailable())
        {
            return new MacKeychainStore();
        }

        if (await LibsecretStore.IsAvailableAsync(cancellationToken))
        {
            return new LibsecretStore();
        }

        return new FileSecretStore(paths.SecretsFile);
    }

    /// <summary>
    /// Get the secret store key that holds a provider's API key.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The key name, derived from the provider id.</returns>
    public static string KeyFor(ProviderConfig provider)
    {
        return KeyFor(provider.Id);
    }

    /// <summary>
    /// Get the secret store key that holds a provider's API key.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <returns>The key name.</returns>
    public static string KeyFor(string providerId)
    {
        return "provider:" + providerId;
    }
}
