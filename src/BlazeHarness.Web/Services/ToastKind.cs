namespace BlazeHarness.Web.Services;

/// <summary>
/// The look of a toast message.
/// </summary>
public enum ToastKind
{
    /// <summary>A neutral notice.</summary>
    Info,

    /// <summary>A completed action.</summary>
    Success,

    /// <summary>A failure the user should notice.</summary>
    Error,
}
