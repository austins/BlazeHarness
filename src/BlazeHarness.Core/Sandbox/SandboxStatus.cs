namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Whether shell commands can run in a sandbox on this computer, and how.
/// </summary>
/// <param name="IsAvailable">True when commands can be confined.</param>
/// <param name="Name">A short name for the sandbox, such as <c>bubblewrap</c>, or an empty string when there is none.</param>
/// <param name="Detail">A sentence for the user: what the sandbox restricts, or why there is none.</param>
/// <param name="ForwardsPorts">True when the ports of servers that sandboxed background commands start are forwarded to localhost, so the browser tools and later commands reach them.</param>
public sealed record SandboxStatus(bool IsAvailable, string Name, string Detail, bool ForwardsPorts = false)
{
    /// <summary>
    /// Get the status of a computer where no sandbox can be used.
    /// </summary>
    /// <param name="reason">Why there is none.</param>
    /// <returns>The status.</returns>
    public static SandboxStatus Unavailable(string reason) => new(false, string.Empty, reason);
}
