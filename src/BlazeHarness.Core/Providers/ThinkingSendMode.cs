namespace BlazeHarness.Core.Providers;

/// <summary>
/// How a thinking level is put on the wire; servers differ.
/// </summary>
public enum ThinkingSendMode
{
    /// <summary><c>reasoning_effort</c>: "value".</summary>
    ReasoningEffort,

    /// <summary><c>reasoning</c>: { "effort": "value" }.</summary>
    ReasoningObject,

    /// <summary>A custom body field addressed by a dotted path, e.g. <c>chat_template_kwargs.enable_thinking</c>.</summary>
    BodyField,
}
