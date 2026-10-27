namespace BlazeHarness.Tests.Shared;

/// <summary>A uniquely named folder under the system temp folder, deleted when disposed.</summary>
public sealed class TempFolder : IDisposable
{
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
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
