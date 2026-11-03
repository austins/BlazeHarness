namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// A headless browser that gives each chat its own page.
/// </summary>
internal interface IBrowserEngine : IDisposable
{
    /// <summary>
    /// Occurs when a chat's page shows something new, and with no frame when the page is closed. It may be raised on any thread.
    /// </summary>
    event EventHandler<BrowserFrameChangedEventArgs>? FrameChanged;

    /// <summary>
    /// Get the last picture of a chat's page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <returns>The picture, or null when the chat has no page open.</returns>
    BrowserFrame? LatestFrame(string chatId);

    /// <summary>
    /// Tell whether the browser program is on this computer.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the check.</param>
    /// <returns>True when pages can be opened.</returns>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Download the browser program.
    /// </summary>
    /// <param name="cancellationToken">A token that stops waiting for the download.</param>
    /// <returns>True when the browser is installed afterwards.</returns>
    Task<bool> InstallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Open an address in a chat's page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="url">The address, which the caller has had approved.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>What the page shows once it has loaded.</returns>
    /// <exception cref="BrowserException">The address is refused, the browser is missing or the page could not be opened.</exception>
    Task<BrowserPageView> NavigateAsync(string chatId, Uri url, CancellationToken cancellationToken);

    /// <summary>
    /// Read the page a chat has open.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>What the page shows now.</returns>
    /// <exception cref="BrowserException">No page is open or it could not be read.</exception>
    Task<BrowserPageView> SnapshotAsync(string chatId, CancellationToken cancellationToken);

    /// <summary>
    /// Close a chat's page and drop what it stored.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    void Forget(string chatId);
}
