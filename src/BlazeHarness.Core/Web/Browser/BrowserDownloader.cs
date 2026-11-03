using PuppeteerSharp;
using System.Text.Json;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Lets a page's download through for one action, into a folder of its own, and waits for the file.
/// </summary>
internal static class BrowserDownloader
{
    internal const long MaxBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Stop a chat's page from saving files on its own.
    /// </summary>
    /// <param name="client">The browser-wide protocol session that stays open as long as the page.</param>
    /// <param name="contextId">The page's browser context id.</param>
    /// <returns>A task that completes when downloads are refused.</returns>
    public static Task DenyAsync(ICDPSession client, string contextId) =>
        client.SendAsync("Browser.setDownloadBehavior", new { behavior = "deny", browserContextId = contextId });

    /// <summary>
    /// Run an action that should start a download and wait for the file to be saved.
    /// </summary>
    /// <param name="client">The browser-wide protocol session that stays open as long as the page.</param>
    /// <param name="contextId">The page's browser context id.</param>
    /// <param name="folder">An empty folder to save the file into.</param>
    /// <param name="trigger">The action, such as a click, that starts the download.</param>
    /// <param name="time">The clock the wait is measured on.</param>
    /// <param name="timeout">How long to wait for the file once the action has run.</param>
    /// <param name="cancellationToken">A token that stops the wait.</param>
    /// <returns>The suggested file name and the path the file was saved to.</returns>
    /// <exception cref="BrowserException">The action started no download, or it failed, was too big or took too long.</exception>
    public static async Task<(string FileName, string Path)> RunAsync(
        ICDPSession client,
        string contextId,
        string folder,
        Func<Task> trigger,
        TimeProvider time,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<(string FileName, string Path)>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? name = null;
        var tooBig = false;

        void OnMessage(object? sender, MessageEventArgs args)
        {
            try
            {
                var data = args.MessageData;
                switch (args.MessageID)
                {
                    case "Browser.downloadWillBegin":
                        name = data.GetProperty("suggestedFilename").GetString();
                        break;
                    case "Browser.downloadProgress":
                        OnProgress(data);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
            {
                finished.TrySetException(new BrowserException("The download could not be tracked."));
            }
        }

        void OnProgress(JsonElement data)
        {
            var guid = data.GetProperty("guid").GetString() ?? string.Empty;
            switch (data.GetProperty("state").GetString())
            {
                case "completed":
                    finished.TrySetResult((name ?? "download", Path.Combine(folder, guid)));
                    break;
                case "canceled":
                    finished.TrySetException(new BrowserException(tooBig ? $"The file is larger than {MaxBytes / (1024 * 1024)} MB, so it was not saved." : "The download was canceled."));
                    break;
                default:
                    if (data.GetProperty("receivedBytes").GetDouble() > MaxBytes)
                    {
                        tooBig = true;
                        _ = client.SendAsync("Browser.cancelDownload", new { guid, browserContextId = contextId });
                    }

                    break;
            }
        }

        client.MessageReceived += OnMessage;
        try
        {
            await client.SendAsync("Browser.setDownloadBehavior", new { behavior = "allowAndName", browserContextId = contextId, downloadPath = folder, eventsEnabled = true });
            await trigger();
            try
            {
                return await finished.Task.WaitAsync(timeout, time, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new BrowserException(name is null ? "Nothing was downloaded. Use browser_click instead for elements that don't offer a file." : "The download did not finish in time.");
            }
        }
        finally
        {
            client.MessageReceived -= OnMessage;
            try
            {
                await DenyAsync(client, contextId);
            }
            catch (PuppeteerException)
            {
                // The page closed, and its downloads went with it.
            }
        }
    }
}
