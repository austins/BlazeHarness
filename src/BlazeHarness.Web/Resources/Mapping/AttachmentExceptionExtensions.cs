using BlazeHarness.Core.Attachments;
using System.Globalization;

namespace BlazeHarness.Web.Resources.Mapping;

internal static class AttachmentExceptionExtensions
{
    extension(AttachmentException exception)
    {
        /// <summary>
        /// Gets the message in the user's language.
        /// </summary>
        public string LocalizedMessage
        {
            get
            {
                var key = exception.Error switch
                {
                    AttachmentError.Unsupported => nameof(WebStrings.AttachmentUnsupported),
                    AttachmentError.TooLarge => nameof(WebStrings.AttachmentTooLarge),
                    AttachmentError.Empty => nameof(WebStrings.AttachmentEmpty),
                    _ => null,
                };
                return key is null
                    ? exception.Message
                    : ResourceMessageFormatter.Web.Format(
                        key,
                        new Dictionary<string, object?>
                        {
                            ["name"] = exception.FileName,
                            ["size"] = (ChatAttachments.MaxFileBytes / (1024 * 1024)).ToString(CultureInfo.CurrentCulture),
                        });
            }
        }
    }
}
