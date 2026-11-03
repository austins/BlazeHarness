using BlazeHarness.Core.Providers;

namespace BlazeHarness.Web.Resources;

internal static class ValidationIssueExtensions
{
    private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>();

    extension(ValidationIssue issue)
    {
        // Gets the message in the user's language.
        public string LocalizedMessage
        {
            get
            {
                var key = issue.Code switch
                {
                    ValidationCode.IdRequired => nameof(WebStrings.ValidationIdRequired),
                    ValidationCode.NameRequired => nameof(WebStrings.ValidationNameRequired),
                    ValidationCode.BaseUrlInvalid => nameof(WebStrings.ValidationBaseUrlInvalid),
                    ValidationCode.ApiKeyRefInvalid => nameof(WebStrings.ValidationApiKeyRefInvalid),
                    ValidationCode.ThinkingFieldRequired => nameof(WebStrings.ValidationThinkingFieldRequired),
                    ValidationCode.ThinkingFieldInvalid => nameof(WebStrings.ValidationThinkingFieldInvalid),
                    ValidationCode.ModelIdRequired => nameof(WebStrings.ValidationModelIdRequired),
                    ValidationCode.ModelIdDuplicate => nameof(WebStrings.ValidationModelIdDuplicate),
                    ValidationCode.ContextWindowInvalid => nameof(WebStrings.ValidationContextWindowInvalid),
                    ValidationCode.MaxOutputInvalid => nameof(WebStrings.ValidationMaxOutputInvalid),
                    ValidationCode.MaxOutputTooLarge => nameof(WebStrings.ValidationMaxOutputTooLarge),
                    ValidationCode.DefaultLevelInvalid => nameof(WebStrings.ValidationDefaultLevelInvalid),
                    ValidationCode.ExtraFieldInvalid => nameof(WebStrings.ValidationExtraFieldInvalid),
                    ValidationCode.SamplingOutOfRange => nameof(WebStrings.ValidationSamplingOutOfRange),
                    _ => null,
                };
                return key is null
                    ? issue.Message
                    : ResourceMessageFormatter.Web.Format(key, issue.Arguments ?? NoArguments);
            }
        }
    }
}
