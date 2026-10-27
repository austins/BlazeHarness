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
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the section shown, or null while the dialog is closed.
    /// </summary>
    public SettingsSection? Section { get; private set; }

    /// <summary>
    /// Open the dialog, or switch it to another section.
    /// </summary>
    /// <param name="section">The section to show.</param>
    public void Open(SettingsSection section = SettingsSection.Providers)
    {
        Section = section;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Close the dialog.
    /// </summary>
    public void Close()
    {
        if (Section is null)
        {
            return;
        }

        Section = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
