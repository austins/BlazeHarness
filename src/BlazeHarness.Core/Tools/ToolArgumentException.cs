namespace BlazeHarness.Core.Tools;

/// <summary>
/// A tool call's arguments were missing or the wrong shape, with a message that tells the model what to fix.
/// </summary>
public sealed class ToolArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the arguments.</param>
    public ToolArgumentException(string message)
        : base(message)
    {
    }
}
