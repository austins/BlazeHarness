namespace BlazeHarness.Core.Secrets;

/// <summary>macOS Keychain through the <c>security</c> CLI. Commands go over stdin (<c>security -i</c>), not arguments.</summary>
public sealed class MacKeychainStore(string serviceName = "blazeharness") : ISecretStore
{
    private const string Tool = "/usr/bin/security";

    public void Dispose()
    {
    }

    public string Name => "macOS Keychain";

    public bool IsSecure => true;

    public static bool IsAvailable()
    {
        return OperatingSystem.IsMacOS() && File.Exists(Tool);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            Tool,
            ["find-generic-password", "-s", serviceName, "-a", key, "-w"],
            null,
            cancellationToken);
        return result.ExitCode == 0 ? result.StdOut.TrimEnd('\n') : null;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var command = $"add-generic-password -U -s {Quote(serviceName)} -a {Quote(key)} -w {Quote(value)}\n";
        var result = await ProcessRunner.RunAsync(Tool, ["-i"], command, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The Keychain refused to store the key.");
        }
    }

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
