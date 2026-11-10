namespace BlazeHarness.Web.Hosting;

/// <summary>
/// What came of opening a file in a text editor.
/// </summary>
internal enum EditorOpenResult
{
    /// <summary>The editor was started.</summary>
    Opened,

    /// <summary>No editor is set and the system has no text editor the app could find.</summary>
    NoEditor,

    /// <summary>The file is not inside the folder or no longer exists.</summary>
    NotFound,

    /// <summary>The editor could not be started.</summary>
    Failed,
}
