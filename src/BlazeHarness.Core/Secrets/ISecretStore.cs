namespace BlazeHarness.Core.Secrets;

/// <summary>Stores API keys outside <c>config.json</c> and session files.</summary>
public interface ISecretStore : IDisposable
{
    /// <summary>Human-readable backend name, e.g. "OS keychain".</summary>
    string Name { get; }

    /// <summary>False for the plaintext-file fallback, so the UI can flag it.</summary>
    bool IsSecure { get; }

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
