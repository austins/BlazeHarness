namespace BlazeHarness.Web.Services;

/// <summary>
/// Tracks whether the settings dialog is open and which section it shows, so any component can open it.
/// </summary>
/// <remarks>
/// Register it as scoped so each browser tab has its own dialog.
/// </remarks>
public sealed class SettingsDialogState
{
    /// <summary>
    /// Raised after the dialog opened, closed or changed section.
    /// </summary>
    /// <remarks>Every handler runs at once, and <see cref="OpenAsync"/> and <see cref="CloseAsync"/> complete when all of them have.</remarks>
#pragma warning disable CA1003, S3264 // A handler returns the task the raiser awaits, which EventHandler can't; RaiseChangedAsync invokes it through GetInvocationList.
    public event Func<Task>? Changed;
#pragma warning restore CA1003, S3264

    /// <summary>
    /// Gets the section shown, or null while the dialog is closed.
    /// </summary>
    public SettingsSection? Section { get; private set; }

    /// <summary>
    /// Open the dialog, or switch it to another section.
    /// </summary>
    /// <param name="section">The section to show.</param>
    /// <returns>A task that completes when every <see cref="Changed"/> handler has.</returns>
    public Task OpenAsync(SettingsSection section = SettingsSection.General)
    {
        Section = section;
        return RaiseChangedAsync();
    }

    /// <summary>
    /// Close the dialog.
    /// </summary>
    /// <returns>A task that completes when every <see cref="Changed"/> handler has.</returns>
    public Task CloseAsync()
    {
        if (Section is null)
        {
            return Task.CompletedTask;
        }

        Section = null;
        return RaiseChangedAsync();
    }

    private Task RaiseChangedAsync()
    {
        return Changed is { } handlers
            ? Task.WhenAll(handlers.GetInvocationList().Select(handler => ((Func<Task>)handler)()))
            : Task.CompletedTask;
    }
}
