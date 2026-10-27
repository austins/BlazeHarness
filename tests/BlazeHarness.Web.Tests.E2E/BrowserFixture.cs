namespace BlazeHarness.Web.Tests.E2E;

/// <summary>
/// Installs Chromium and starts it once for the whole assembly. When that is impossible (for example, no network
/// for the download), <see cref="SkipReason"/> is set and every test skips instead of failing.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private const string BrowserName = "chromium";

    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Assertions.SetDefaultExpectTimeout((float)TestTimeouts.Default.TotalMilliseconds);

        try
        {
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", BrowserName]));
            if (exitCode != 0)
            {
                SkipReason = $"The Playwright {BrowserName} download failed (exit code {exitCode}); the browser is not available.";
                return;
            }

#pragma warning disable IDISP003 // Set once.
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync();
#pragma warning restore IDISP003
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException)
        {
            SkipReason = $"Playwright {BrowserName} is not available: {ex.GetType().Name}.";
        }
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
