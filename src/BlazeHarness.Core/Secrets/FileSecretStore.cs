namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Plaintext JSON file readable only by the current user (mode 0600, or an owner-only ACL on Windows).
/// </summary>
/// <remarks>
/// A fallback for machines without a keyring; the UI flags it as insecure.
/// </remarks>
/// <param name="path">The full path of the secrets file.</param>
internal sealed class FileSecretStore(string path) : FileBackedSecretStore(path, value => value, value => value, ownerOnly: true)
{
    /// <inheritdoc />
    public override string Name => "Plaintext file (owner-only)";

    /// <inheritdoc />
    public override bool IsSecure => false;
}
