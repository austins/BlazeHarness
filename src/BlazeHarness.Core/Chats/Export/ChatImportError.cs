namespace BlazeHarness.Core.Chats.Export;

/// <summary>
/// Why a file can't be imported as a chat, so a UI can say it in the user's language.
/// </summary>
public enum ChatImportError
{
    /// <summary>The file is not a BlazeHarness chat export.</summary>
    NotAnExport,

    /// <summary>The file was exported by a newer version of the app.</summary>
    NewerVersion,

    /// <summary>The file has no messages.</summary>
    NoMessages,
}
