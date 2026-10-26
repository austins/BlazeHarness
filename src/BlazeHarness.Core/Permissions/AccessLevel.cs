namespace BlazeHarness.Core.Permissions;

/// <summary>
/// What the tools of a session are allowed to do.
/// </summary>
public enum AccessLevel
{
    /// <summary>The model can read files and can't change anything or run commands.</summary>
    ReadOnly,

    /// <summary>The model can read the folder and write inside it. This is the default.</summary>
    WriteInFolder,

    /// <summary>Tools can touch anything the user can.</summary>
    Unrestricted,
}
