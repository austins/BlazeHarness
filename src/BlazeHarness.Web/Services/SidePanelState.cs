namespace BlazeHarness.Web.Services;

/// <summary>
/// Tracks whether the side panel next to the chat is open, which view it shows and which views have something to show.
/// </summary>
/// <remarks>
/// Register it as scoped so each browser tab has its own panel. The panel component reports what is available;
/// the button that reopens the panel and the panel itself read this state.
/// </remarks>
public sealed class SidePanelState
{
    /// <summary>
    /// Raised after any value changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether the user wants the panel open. The panel only shows when <see cref="Current"/> is not null as well.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Gets the view the user chose last.
    /// </summary>
    public SidePanelTab Selected { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the assistant has a page open.
    /// </summary>
    public bool HasBrowser { get; private set; }

    /// <summary>
    /// Gets how many files changed in the repository, or 0 when there is no repository.
    /// </summary>
    public int ChangeCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there are changed files to show.
    /// </summary>
    public bool HasChanges => ChangeCount > 0;

    /// <summary>
    /// Gets the view to show: the chosen one when it has something to show, otherwise another that has, or null when none has.
    /// </summary>
    public SidePanelTab? Current => Selected switch
    {
        SidePanelTab.Browser when HasBrowser => SidePanelTab.Browser,
        SidePanelTab.Changes when HasChanges => SidePanelTab.Changes,
        _ when HasBrowser => SidePanelTab.Browser,
        _ when HasChanges => SidePanelTab.Changes,
        _ => null,
    };

    /// <summary>
    /// Gets a value indicating whether the panel is showing.
    /// </summary>
    public bool IsVisible => IsOpen && Current is not null;

    /// <summary>
    /// Open the panel, on a view if one is given.
    /// </summary>
    /// <param name="tab">The view to show, or null to keep the last one.</param>
    public void Open(SidePanelTab? tab = null)
    {
        var selected = tab ?? Selected;
        if (IsOpen && selected == Selected)
        {
            return;
        }

        IsOpen = true;
        Selected = selected;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Close the panel.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Close the panel when it shows, and open it otherwise.
    /// </summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// Record whether the assistant has a page open.
    /// </summary>
    /// <param name="available">True while a page is open.</param>
    public void SetBrowser(bool available)
    {
        if (HasBrowser == available)
        {
            return;
        }

        HasBrowser = available;
        Settle();
    }

    /// <summary>
    /// Record how many files changed in the repository.
    /// </summary>
    /// <param name="count">The number of changed files; 0 when there are none or no repository.</param>
    public void SetChangeCount(int count)
    {
        count = Math.Max(0, count);
        if (ChangeCount == count)
        {
            return;
        }

        ChangeCount = count;
        Settle();
    }

    /// <summary>
    /// Forget everything, for another chat.
    /// </summary>
    public void Reset()
    {
        if (!IsOpen && Selected == SidePanelTab.Browser && !HasBrowser && ChangeCount == 0)
        {
            return;
        }

        IsOpen = false;
        Selected = SidePanelTab.Browser;
        HasBrowser = false;
        ChangeCount = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Close an empty panel and notify listeners of the changed state.
    /// </summary>
    /// <remarks>An empty panel must not open by itself when something shows up later.</remarks>
    private void Settle()
    {
        if (Current is null)
        {
            IsOpen = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
