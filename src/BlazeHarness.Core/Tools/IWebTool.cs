using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// A tool that reaches the internet, so each call needs the user's approval unless the session is unrestricted.
/// </summary>
internal interface IWebTool : ITool
{
    /// <summary>
    /// Describe what a call would reach.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The request, or null when the arguments are malformed and the tool will refuse them.</returns>
    WebRequest? Describe(JsonElement arguments);
}
