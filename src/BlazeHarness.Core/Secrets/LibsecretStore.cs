namespace BlazeHarness.Core.Secrets;

/// <summary>Linux: the freedesktop Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> CLI.</summary>
public sealed class LibsecretStore(string serviceName = "blazeharness") : ISecretStore
{
    private const string Tool = "secret-tool";

    public void Dispose()
    {
    }

    public string Name => "Secret Service (libsecret)";

    public bool IsSecure => true;

    /// <summary>True when <c>secret-tool</c> exists and a Secret Service answers on the session bus.</summary>
    public static async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            // A lookup of a key that cannot exist exits 1 with no output when the service works.
            var probe = await ProcessRunner.RunAsync(
                Tool,
                ["lookup", "service", "blazeharness-probe", "account", "probe"],
                null,
                cancellationToken);
            return probe.ExitCode is 0 or 1 && string.IsNullOrWhiteSpace(probe.StdErr);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(Tool, ["lookup", "service", serviceName, "account", key], null, cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Length > 0 ? result.StdOut : null;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            Tool,
            ["store", "--label=BlazeHarness " + key, "service", serviceName, "account", key],
            value,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The Secret Service refused to store the key.");
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return ProcessRunner.RunAsync(Tool, ["clear", "service", serviceName, "account", key], null, cancellationToken);
    }
}
