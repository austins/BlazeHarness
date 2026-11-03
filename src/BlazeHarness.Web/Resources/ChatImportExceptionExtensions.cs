using BlazeHarness.Core.Chats.Export;

namespace BlazeHarness.Web.Resources;

internal static class ChatImportExceptionExtensions
{
    extension(ChatImportException exception)
    {
        // Gets the message in the user's language.
        public string LocalizedMessage => exception.Error switch
        {
            ChatImportError.NotAnExport => WebStrings.SidebarImportNotAnExport,
            ChatImportError.NewerVersion => WebStrings.SidebarImportNewerVersion,
            ChatImportError.NoMessages => WebStrings.SidebarImportNoMessages,
            _ => exception.Message,
        };
    }
}
