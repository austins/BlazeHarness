namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Linux: the freedesktop Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> CLI.
/// </summary>
/// <remarks>
/// Values are passed to <c>secret-tool</c> over stdin so they never appear in the process list.
/// </remarks>
/// <param name="serviceName">The service attribute the secrets are filed under. Tests use a throwaway name.</param>
/// <param name="time">Times keyring calls so a locked keyring cannot hang forever; defaults to the system clock.</param>
internal sealed class LibsecretStore(string serviceName = "blazeharness", TimeProvider? time = null) : ISecretStore
{
    private const string Tool = "secret-tool";

    /// <summary>
    /// The maximum wait for a keyring availability probe.
    /// </summary>
    /// <remarks>A locked keyring can wait on a prompt forever, which would otherwise hang startup.</remarks>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public string Name => "Secret Service (libsecret)";

    /// <inheritdoc />
    public bool IsSecure => true;

    /// <summary>
    /// Check whether <c>secret-tool</c> exists and a Secret Service answers on the session bus.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>True when the store can be used; always false on other operating systems.</returns>
    public static Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => IsAvailableAsync(null, cancellationToken);

    /// <summary>
    /// Check whether <c>secret-tool</c> exists and a Secret Service answers on the session bus.
    /// </summary>
    /// <param name="time">Times the probe; defaults to the system clock.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>True when the store can be used; always false on other operating systems.</returns>
    public static async Task<bool> IsAvailableAsync(TimeProvider? time, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        using var clockTimeout = new CancellationTokenSource(ProbeTimeout, time ?? TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clockTimeout.Token);
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
        var result = await RunBoundedAsync(["lookup", "service", serviceName, "account", key], null, cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Length > 0 ? result.StdOut : null;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await RunBoundedAsync(
            ["store", $"--label={AppConstants.AppName} {key}", "service", serviceName, "account", key],
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
        return RunBoundedAsync(["clear", "service", serviceName, "account", key], null, cancellationToken);
    }

    private async Task<ProcessResult> RunBoundedAsync(IReadOnlyList<string> arguments, string? stdin, CancellationToken cancellationToken)
    {
        using var clockTimeout = new CancellationTokenSource(CallTimeout, _time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clockTimeout.Token);
        try
        {
            return await ProcessRunner.RunAsync(Tool, arguments, stdin, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The Secret Service did not answer in time; the keyring may be locked.", ex);
        }
    }
}
