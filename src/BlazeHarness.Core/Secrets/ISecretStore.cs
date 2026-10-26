namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Stores API keys outside <c>config.json</c> and session files.
/// </summary>
public interface ISecretStore : IDisposable
{
    /// <summary>
    /// Gets the human-readable backend name, e.g. "OS keychain".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the secrets are protected by the OS. False for the plaintext-file fallback, so the UI can flag it.
    /// </summary>
    bool IsSecure { get; }

    /// <summary>
    /// Read a secret.
    /// </summary>
    /// <param name="key">The secret's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The secret, or null when none is stored under <paramref name="key"/>.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store a secret, replacing any existing value.
    /// </summary>
    /// <param name="key">The secret's name.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once the secret is stored.</returns>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a secret. Removing one that does not exist is not an error.
    /// </summary>
    /// <param name="key">The secret's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once the secret is gone.</returns>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
