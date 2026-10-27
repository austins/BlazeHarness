using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Windows: values encrypted with DPAPI for the current user and kept in a file.
/// </summary>
/// <param name="path">The full path of the file holding the encrypted values.</param>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore(string path) : FileBackedSecretStore(path, Protect, Unprotect, ownerOnly: false)
{
    /// <inheritdoc />
    public override string Name => "Windows DPAPI";

    /// <inheritdoc />
    public override bool IsSecure => true;

    private static string Protect(string value)
    {
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string stored)
    {
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
    }
}
