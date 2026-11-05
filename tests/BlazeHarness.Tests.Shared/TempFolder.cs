namespace BlazeHarness.Tests.Shared;

/// <summary>A uniquely named folder under the system temp folder, deleted when disposed.</summary>
public sealed class TempFolder : IDisposable
{
    /// <summary>The number of times a failed delete is tried before the exception is reported.</summary>
    private const int DeleteAttempts = 20;

    /// <summary>The pause between two attempts to delete the folder.</summary>
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Initializes a new instance of the <see cref="TempFolder"/> class and creates the folder.</summary>
    /// <param name="prefix">A short name for the kind of test that owns the folder, to recognize leftovers.</param>
    public TempFolder(string prefix = "bh-")
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Gets the full path of the folder.</summary>
    public string Root { get; }

    /// <summary>Get a path inside the folder.</summary>
    /// <param name="parts">The folder and file names below the root.</param>
    /// <returns>The combined path. Nothing is created.</returns>
    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Create a folder inside this one, with any parents.</summary>
    /// <param name="parts">The folder names below the root.</param>
    /// <returns>The full path of the new folder.</returns>
    public string CreateDirectory(params string[] parts)
    {
        var path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Windows can keep a file or folder busy for a moment after its owner let go (antivirus scans, a server that is still shutting down),
    /// so a failed delete is retried for about five seconds before it is reported. This hides short-lived locks only:
    /// a test must still wait for its own background work to end, or the lock outlasts the retries and fails the last attempt.
    /// </remarks>
    public void Dispose()
    {
        for (var attempt = 1; attempt < DeleteAttempts; attempt++)
        {
            try
            {
                Delete();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(DeleteRetryDelay);
            }
        }

        Delete();
    }

    private void Delete()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            };
            foreach (var file in Directory.EnumerateFiles(Root, "*", options))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(Root, recursive: true);
    }
}
