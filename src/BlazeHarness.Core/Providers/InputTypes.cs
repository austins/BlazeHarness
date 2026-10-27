namespace BlazeHarness.Core.Providers;

/// <summary>
/// The kinds of input a model accepts.
/// </summary>
[Flags]
public enum InputTypes
{
    /// <summary>No input.</summary>
    None = 0,

    /// <summary>Text.</summary>
    Text = 1,

    /// <summary>Images.</summary>
    Image = 2,
}
