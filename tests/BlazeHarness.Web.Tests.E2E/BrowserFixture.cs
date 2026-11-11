using BlazeHarness.Tests.Shared;
using Microsoft.Playwright.TestAdapter;

namespace BlazeHarness.Web.Tests.E2E;

/// <summary>
/// Installs the Playwright browser and starts it once for the whole assembly.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }

    /// <summary>
    /// Gets the extra Chrome arguments for Windows.
    /// </summary>
    /// <remarks>
    /// On Windows 11 and Server 2025 Chrome's port randomization makes loopback connections fail now and then with ERR_NO_BUFFER_SPACE
    /// when many open and close, as the tests do (https://github.com/microsoft/playwright/issues/43545). Chrome reads only the last
    /// <c>--disable-features</c> and Playwright doesn't merge ours into its own (https://github.com/microsoft/playwright/issues/43557),
    /// so this repeats Playwright 1.64.1's list from <c>chromiumSwitches.ts</c>. Recheck it when Playwright is updated.
    /// </remarks>
    private static string[] WindowsArgs => OperatingSystem.IsWindows()
        ? [
            "--disable-features=AvoidUnnecessaryBeforeUnloadCheckSync,DestroyProfileOnBrowserClose,DialMediaRouteProvider,GlobalMediaControls,"
            + "HttpsUpgrades,LensOverlay,MediaRouter,PaintHolding,ThirdPartyStoragePartitioning,BlockOriginHeaderModificationOnRedirect,"
            + "AvoidCorsURLLoaderRestartOnRedirect,Translate,AutoDeElevate,OptimizationHints,NetworkTimeServiceQuerying,AimEnabled,"
            + "msForceBrowserSignIn,msEdgeUpdateLaunchServicesPreferredVersion,TcpPortRandomizationWin"
          ]
        : [];

    public async ValueTask InitializeAsync()
    {
        Assertions.SetDefaultExpectTimeout((float)TestTimeouts.Default.TotalMilliseconds);

        var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", PlaywrightSettingsProvider.BrowserName]));
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to install Playwright browser. Exit code: {exitCode}");
        }

#pragma warning disable IDISP003 // Set once.
        _playwright = await Playwright.CreateAsync();
#pragma warning restore IDISP003

        Browser = await _playwright[PlaywrightSettingsProvider.BrowserName].LaunchAsync(new() { Args = WindowsArgs });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
