using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Secrets;

public static class SecretStoreFactory
{
    /// <summary>The OS secret store when one works here, otherwise the owner-only file fallback.</summary>
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

    public static string KeyFor(ProviderConfig provider)
    {
        return "provider:" + provider.Id;
    }
}
