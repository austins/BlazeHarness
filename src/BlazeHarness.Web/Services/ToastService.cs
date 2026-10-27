using BlazeHarness.Core.Config;

namespace BlazeHarness.Web.Services;

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

    /// <summary>
    /// Run a save, and show an error toast instead of letting a storage failure end the connection.
    /// </summary>
    /// <remarks>
    /// Only failures to write a file or store a secret are caught, and the toast never includes the exception's message.
    /// </remarks>
    /// <param name="action">The save to run.</param>
    /// <param name="failureMessage">The text to show when the save fails.</param>
    /// <returns>True when the save ran to the end; false when it failed.</returns>
    public async Task<bool> TrySaveAsync(Func<Task> action, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrEmpty(failureMessage);

        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Show(failureMessage, ToastKind.Error);
            return false;
        }
    }
}
