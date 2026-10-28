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

        Browser = await _playwright[PlaywrightSettingsProvider.BrowserName].LaunchAsync();
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
