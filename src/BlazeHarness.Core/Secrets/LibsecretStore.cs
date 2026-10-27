namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Linux: the freedesktop Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> CLI.
/// </summary>
/// <remarks>
/// Values are passed to <c>secret-tool</c> over stdin so they never appear in the process list.
/// </remarks>
/// <param name="serviceName">The service attribute the secrets are filed under. Tests use a throwaway name.</param>
public sealed class LibsecretStore(string serviceName = "blazeharness") : ISecretStore
{
    private const string Tool = "secret-tool";

    // A locked keyring can wait on a prompt forever; without this startup would hang.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public string Name => "Secret Service (libsecret)";

    /// <inheritdoc />
    public bool IsSecure => true;

    /// <summary>
    /// Check whether <c>secret-tool</c> exists and a Secret Service answers on the session bus.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>True when the store can be used; always false on other operating systems.</returns>
    public static async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            // A lookup of a key that cannot exist exits 1 with no output when the service works.
            var probe = await ProcessRunner.RunAsync(
                Tool,
                ["lookup", "service", "blazeharness-probe", "account", "probe"],
                null,
                timeout.Token);
            return probe.ExitCode is 0 or 1 && string.IsNullOrWhiteSpace(probe.StdErr);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(Tool, ["lookup", "service", serviceName, "account", key], null, cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Length > 0 ? result.StdOut : null;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return ProcessRunner.RunAsync(Tool, ["clear", "service", serviceName, "account", key], null, cancellationToken);
    }
}
