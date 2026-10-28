using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Providers;
using System.Text.Json;

namespace BlazeHarness.Web.Tests.E2E;

/// <summary>
/// Gives each test its own app instance (data folder, fake provider) and a fresh browser page.
/// </summary>
/// <remarks>
/// The launch token works once, so a shared app could not serve parallel tests. The browser is shared and each test
/// gets its own context, so cookies never leak between tests.
/// </remarks>
public abstract class TestBase : IAsyncLifetime
{
    protected const string ProviderName = "Fake Provider";
    private const string ModelId = "fake-model";

    private readonly BrowserFixture _browser;
    private readonly TempFolder _home = new("bh-e2e-");
    private readonly FakeOpenAiServer _provider;
    private AppFactory? _factory;
    private IBrowserContext? _context;

    protected TestBase(BrowserFixture browser)
    {
        _browser = browser;
        _provider = new FakeOpenAiServer(ChunksFor);
    }

    public async ValueTask InitializeAsync()
    {
        var providers = new ProvidersDocument
        {
            Providers =
            [
                new ProviderConfig
                {
                    Id = "fake",
                    DisplayName = ProviderName,
                    BaseUrl = _provider.BaseUrl,
                    Models = [new ModelConfig { Id = ModelId, ContextWindow = 8192, MaxOutputTokens = 1024 }],
                },
            ],
        };
        var config = new AppConfig
        {
            DefaultProviderId = "fake",
            DefaultModelId = ModelId,
        };
        await File.WriteAllTextAsync(_home.Combine("providers.json"), JsonSerializer.Serialize(providers, JsonDefaults.Options), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_home.Combine("config.json"), JsonSerializer.Serialize(config, JsonDefaults.Options), TestContext.Current.CancellationToken);

#pragma warning disable IDISP003 // Set once per test.
        _factory = new AppFactory(_home.Root);
        _context = await _browser.Browser!.NewContextAsync(new BrowserNewContextOptions { BaseURL = _factory.ClientOptions.BaseAddress.AbsoluteUri });
        _context.SetDefaultTimeout((float)TestTimeouts.Default.TotalMilliseconds);
        Page = await _context.NewPageAsync();
#pragma warning restore IDISP003
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await Page.CloseAsync();

        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _provider.Dispose();
        Workspace.Dispose();
        _home.Dispose();
    }

    protected TempFolder Workspace { get; } = new("bh-e2e-ws-");

    protected IPage Page { get; private set; } = null!;

    protected AppFactory Factory => _factory!;

    protected static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>Open the app and wait for the Blazor circuit.</summary>
    /// <param name="path">The page to open.</param>
    protected async Task LaunchAsync(string path = "/")
    {
        await Page.GotoAsync(path);
        await Expect(Page.Locator(".app")).ToHaveAttributeAsync("data-circuit", "ready");
    }

    /// <summary>Gets the chunks the fake provider streams for a request body. Override to script a chat.</summary>
    /// <param name="body">The request body.</param>
    /// <returns>The server-sent event payloads of the reply.</returns>
    protected virtual string[]? ChunksFor(string body) => [Chunk("""{"content":"Hello from the fake model."}""", null)];

    protected static string Chunk(string delta, string? finishReason)
    {
        var finish = finishReason is null ? "null" : $"\"{finishReason}\"";
        return $$"""{"id":"1","object":"chat.completion.chunk","model":"fake-model","choices":[{"index":0,"delta":{{delta}},"finish_reason":{{finish}}}]}""";
    }
}
