using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

/// <summary>
/// Tells the sandbox tests whether this computer can run a sandbox, so they skip where it can't.
/// </summary>
internal static class SandboxSupport
{
    private static readonly Lazy<bool> Available = new(() => new MxcShellSandbox(() => SensitivePaths.None).Status.IsAvailable);

    public static bool IsAvailable => Available.Value;

    public static bool IsAvailableOnPosix => IsAvailable && !OperatingSystem.IsWindows();
}
