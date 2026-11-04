using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// A tool that sends a message to another chat, so the permission check can say where it goes.
/// </summary>
internal interface ISendToChatTool : ITool
{
    /// <summary>
    /// Work out where a call would send its message.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The plan, or the reason the call can go nowhere.</returns>
    SendToChatResolution Plan(JsonElement arguments);
}
