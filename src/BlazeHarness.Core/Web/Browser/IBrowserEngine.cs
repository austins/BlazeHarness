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
    /// Click an element of the page a chat has open.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="elementRef">The ref the snapshot gave the element.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>What the page shows afterwards.</returns>
    /// <exception cref="BrowserException">No page is open, or the element is gone or can't be clicked.</exception>
    Task<BrowserPageView> ClickAsync(string chatId, string elementRef, CancellationToken cancellationToken);

    /// <summary>
    /// Type into a field of the page a chat has open, replacing what it holds, or pick an option of a drop-down list.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="elementRef">The ref the snapshot gave the field.</param>
    /// <param name="text">The text, or the option's text or value.</param>
    /// <param name="submit">Whether to press Enter afterwards.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>What the page shows afterwards.</returns>
    /// <exception cref="BrowserException">No page is open, or the element is gone or doesn't take text.</exception>
    Task<BrowserPageView> TypeAsync(string chatId, string elementRef, string text, bool submit, CancellationToken cancellationToken);

    /// <summary>
    /// Take a picture of the page a chat has open.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="fullPage">Whether to capture the whole page rather than what fits the window.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>The picture as PNG bytes.</returns>
    /// <exception cref="BrowserException">No page is open or it could not be captured.</exception>
    Task<byte[]> ScreenshotAsync(string chatId, bool fullPage, CancellationToken cancellationToken);

    /// <summary>
    /// Click an element that offers a file and save the file.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="elementRef">The ref the snapshot gave the element.</param>
    /// <param name="destination">The full path to save the file to; an existing file is replaced.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>What was saved.</returns>
    /// <exception cref="BrowserException">No page is open, the click started no download, or the download failed or was too big.</exception>
    Task<BrowserDownload> DownloadAsync(string chatId, string elementRef, string destination, CancellationToken cancellationToken);

    /// <summary>
    /// Pass a mouse click by the user on to the page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="x">How far across the page, from 0 to 1.</param>
    /// <param name="y">How far down the page, from 0 to 1.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>A task that completes when the click was sent.</returns>
    Task UserClickAsync(string chatId, double x, double y, CancellationToken cancellationToken);

    /// <summary>
    /// Pass a key press by the user on to the page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="key">A single character to type, or the name of a key such as Enter or ArrowDown. Other keys are ignored.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>A task that completes when the key was sent.</returns>
    Task UserKeyAsync(string chatId, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Pass a scroll by the user on to the page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="x">How far across the page the pointer is, from 0 to 1.</param>
    /// <param name="y">How far down the page the pointer is, from 0 to 1.</param>
    /// <param name="deltaY">How many pixels to scroll down; negative scrolls up.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>A task that completes when the scroll was sent.</returns>
    Task UserScrollAsync(string chatId, double x, double y, double deltaY, CancellationToken cancellationToken);

    /// <summary>
    /// Close a chat's page and drop what it stored.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    void Forget(string chatId);
}
