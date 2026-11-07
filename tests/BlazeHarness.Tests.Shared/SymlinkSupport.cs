namespace BlazeHarness.Tests.Shared;

/// <summary>Tells tests whether this process can create symbolic links, so they skip where it can't.</summary>
/// <remarks>Unix always can. Windows needs an elevated process or Developer Mode.</remarks>
public static class SymlinkSupport
{
    /// <summary>The skip reason of a test that needs symbolic links.</summary>
    public const string Reason = "Can't create symbolic links here (Windows needs an elevated process or Developer Mode).";

    /// <summary>Gets a value indicating whether a file link and a folder link can be created; the skip condition for symlink tests.</summary>
    public static bool CanCreate { get; } = Probe();

    private static bool Probe()
    {
        using var temp = new TempFolder("bh-symlink-probe-");
        try
        {
            var target = temp.Combine("target.txt");
            File.WriteAllText(target, "x");
            File.CreateSymbolicLink(temp.Combine("file-link"), target);
            Directory.CreateSymbolicLink(temp.Combine("folder-link"), temp.Root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
