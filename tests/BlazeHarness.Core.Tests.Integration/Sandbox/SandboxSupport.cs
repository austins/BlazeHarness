using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

/// <summary>
/// Tells the sandbox tests whether this computer can run a sandbox, so they skip where it can't.
/// </summary>
internal static class SandboxSupport
{
    /// <summary>
    /// Gets a sandbox that protects nothing, shared so the tests run its self-tests once.
    /// </summary>
    /// <remarks>Each self-test starts a container, which takes seconds on Windows. Every command still gets its own container.</remarks>
    public static MxcShellSandbox Shared { get; } = new(() => SensitivePaths.None);

    public static bool IsAvailable => Shared.Status.IsAvailable;

    public static bool IsAvailableOnPosix => IsAvailable && !OperatingSystem.IsWindows();

    public static bool IsAvailableOnWindows => IsAvailable && OperatingSystem.IsWindows();
}
