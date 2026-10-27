namespace BlazeHarness.Core.Secrets;

/// <summary>
/// macOS Keychain through the <c>security</c> CLI.
/// </summary>
/// <remarks>
/// Commands go over stdin (<c>security -i</c>), not arguments, so values never appear in the process list.
/// </remarks>
/// <param name="serviceName">The service name the secrets are filed under.</param>
internal sealed class MacKeychainStore(string serviceName = "blazeharness") : ISecretStore
{
    private const string Tool = "/usr/bin/security";

    /// <inheritdoc />
    public string Name => "macOS Keychain";

    /// <inheritdoc />
    public bool IsSecure => true;

    /// <summary>
    /// Check whether the Keychain CLI is present.
    /// </summary>
    /// <returns>True on macOS when <c>/usr/bin/security</c> exists.</returns>
    public static bool IsAvailable()
    {
        return OperatingSystem.IsMacOS() && File.Exists(Tool);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            Tool,
            ["find-generic-password", "-s", serviceName, "-a", key, "-w"],
            null,
            cancellationToken);
        return result.ExitCode == 0 ? result.StdOut.TrimEnd('\n') : null;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (value.AsSpan().ContainsAny('\r', '\n') || key.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException("The key and value cannot contain line breaks.", nameof(value));
        }

        var command = $"add-generic-password -U -s {Quote(serviceName)} -a {Quote(key)} -w {Quote(value)}\n";
        var result = await ProcessRunner.RunAsync(Tool, ["-i"], command, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The Keychain refused to store the key.");
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return ProcessRunner.RunAsync(
            Tool,
            ["delete-generic-password", "-s", serviceName, "-a", key],
            null,
            cancellationToken);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
