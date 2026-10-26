using BlazeHarness.Core.Config;

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

/// <summary>
/// A short-lived message shown in the corner of the window.
/// </summary>
/// <param name="Id">A unique id, so the host can dismiss this exact toast.</param>
/// <param name="Message">The text to show.</param>
/// <param name="Kind">The look of the toast.</param>
public sealed record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>
/// Collects toasts from any component for <c>ToastHost</c> to display.
/// </summary>
/// <remarks>
/// Register it as scoped so each browser tab sees only its own toasts.
/// </remarks>
public sealed class ToastService
{
    /// <summary>
    /// Raised when a toast should be shown.
    /// </summary>
    public event EventHandler<ValueEventArgs<Toast>>? Shown;

    /// <summary>
    /// Show a toast.
    /// </summary>
    /// <param name="message">The text to show.</param>
    /// <param name="kind">The look of the toast.</param>
    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        Shown?.Invoke(this, new ValueEventArgs<Toast>(new Toast(Guid.NewGuid(), message, kind)));
    }
}
