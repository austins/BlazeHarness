namespace BlazeHarness.Web.Services;

/// <summary>
/// A short-lived message shown in the corner of the window.
/// </summary>
/// <param name="Id">A unique id, so the host can dismiss this exact toast.</param>
/// <param name="Message">The text to show.</param>
/// <param name="Kind">The look of the toast.</param>
public sealed record Toast(Guid Id, string Message, ToastKind Kind);
