using Microsoft.Extensions.Configuration;
using PuppeteerSharp;

namespace BlazeHarness.Core.Tests.Integration.Web.Browser;

/// <summary>
/// Gets a Chrome headless shell for the tests that drive a real browser, downloading the one the app uses on first need.
/// </summary>
/// <remarks>
/// The download goes to the app's own browser folder (<see cref="AppPaths.BrowserDirectory"/>, honoring BLAZEHARNESS_HOME), so
/// a browser installed from the app is reused and one downloaded here is reused by the app. The tests touch nothing else there.
/// Set BLAZEHARNESS_TEST_CHROME to use a Chrome program you already have, for example when offline.
/// </remarks>
internal static class TestChrome
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Gets the path of a Chrome headless shell program, or null when none is installed.</summary>
    public static string? Path { get; } = Find();

    /// <summary>Gets a value indicating whether a headless shell was found; the skip condition for browser tests.</summary>
    public static bool IsAvailable => Path is not null;

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("BLAZEHARNESS_TEST_CHROME") is { Length: > 0 } configured && File.Exists(configured))
        {
            return configured;
        }

        try
        {
            return Task.Run(FindOrDownloadAsync).WaitAsync(DownloadTimeout).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is PuppeteerException or HttpRequestException or IOException or TimeoutException)
        {
            // Offline or blocked: the browser tests skip instead of failing every run.
            return null;
        }
    }

    private static async Task<string?> FindOrDownloadAsync()
    {
        var cache = AppPaths.FromConfiguration(new ConfigurationBuilder().AddEnvironmentVariables().Build()).BrowserDirectory;
        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Browser = SupportedBrowser.ChromeHeadlessShell, Path = cache });
        var installed = fetcher.GetInstalledBrowsers().FirstOrDefault() ?? await fetcher.DownloadAsync();
        return installed.GetExecutablePath();
    }
}
