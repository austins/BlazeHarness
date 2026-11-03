using BlazeHarness.Core.Tools.Shell;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// The programs that forward a sandboxed command's ports on Linux.
/// </summary>
/// <param name="Nsenter">The path of <c>nsenter</c>, which runs a program in the sandbox's network.</param>
/// <param name="Socat">The path of <c>socat</c>, which relays the connections.</param>
internal sealed record ForwardingTools(string Nsenter, string Socat)
{
    /// <summary>
    /// Find the programs on this computer.
    /// </summary>
    /// <returns>The programs, or null when this isn't Linux or one of them is missing.</returns>
    public static ForwardingTools? Find()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        return ShellInfo.FindOnPath("nsenter", path) is { } nsenter && ShellInfo.FindOnPath("socat", path) is { } socat
            ? new ForwardingTools(nsenter, socat)
            : null;
    }
}
