using BlazeHarness.Core.Providers;

namespace BlazeHarness.Web.Resources.Mapping;

internal static class ConnectionTestResultExtensions
{
    private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>();

    extension(ConnectionTestResult result)
    {
        /// <summary>
        /// Gets the message in the user's language.
        /// </summary>
        public string LocalizedMessage
        {
            get
            {
                var key = result.Code switch
                {
                    ConnectionTestCode.Connected => nameof(WebStrings.ConnectionTestConnected),
                    ConnectionTestCode.ConnectedNoModels => nameof(WebStrings.ConnectionTestConnectedNoModels),
                    ConnectionTestCode.InvalidBaseUrl => nameof(WebStrings.ConnectionTestInvalidBaseUrl),
                    ConnectionTestCode.ModelListTooLarge => nameof(WebStrings.ConnectionTestModelListTooLarge),
                    ConnectionTestCode.Timeout => nameof(WebStrings.ConnectionTestTimeout),
                    ConnectionTestCode.ConnectionFailed => nameof(WebStrings.ConnectionTestConnectionFailed),
                    ConnectionTestCode.NotAModelList => nameof(WebStrings.ConnectionTestNotAModelList),
                    ConnectionTestCode.Unauthorized => nameof(WebStrings.ConnectionTestUnauthorized),
                    ConnectionTestCode.NotFound => nameof(WebStrings.ConnectionTestNotFound),
                    ConnectionTestCode.RateLimited => nameof(WebStrings.ConnectionTestRateLimited),
                    ConnectionTestCode.HttpError => nameof(WebStrings.ConnectionTestHttpError),
                    _ => null,
                };
                return key is null
                    ? result.Message
                    : ResourceMessageFormatter.Web.Format(key, result.Arguments ?? NoArguments);
            }
        }
    }
}
