using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration.Providers;

public sealed class ProviderServiceTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-providers-");
    private readonly JsonFileStore<AppConfig> _config;
    private readonly FileSecretStore _secrets;
    private readonly ProviderService _sut;

    public ProviderServiceTests()
    {
        _config = new JsonFileStore<AppConfig>(Path.Combine(_temp.Root, "config.json"), new LoggerFake<ProviderServiceTests>(), watch: false);
        _secrets = new FileSecretStore(Path.Combine(_temp.Root, "secrets.json"));
        _sut = new ProviderService(_config, _secrets, new ApiKeyResolver(_secrets, _ => null));
    }

    public void Dispose()
    {
        _config.Dispose();
        _secrets.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task SaveAsync_NewProvider_StoresConfigAndKey_AndBecomesDefault()
    {
        // Arrange
        var provider = Provider("ds", "m1");

        // Act
        var issues = await _sut.SaveAsync(provider, "sk-1", TestContext.Current.CancellationToken);

        // Assert
        issues.Should().BeEmpty();
        _sut.Config.Providers.Should().ContainSingle(p => p.Id == "ds");
        _sut.Config.DefaultProviderId.Should().Be("ds");
        (await _secrets.GetAsync("provider:ds", TestContext.Current.CancellationToken)).Should().Be("sk-1");
        (await File.ReadAllTextAsync(Path.Combine(_temp.Root, "config.json"), TestContext.Current.CancellationToken)).Should().NotContain("sk-1");
    }

    [Fact]
    public async Task SaveAsync_NewProviderWithoutKey_SavesWithoutStoringKey()
    {
        // Arrange
        var provider = Provider("local", "m1");

        // Act
        var issues = await _sut.SaveAsync(provider, null, TestContext.Current.CancellationToken);

        // Assert
        issues.Should().BeEmpty();
        _sut.Config.Providers.Should().ContainSingle(p => p.Id == "local");
        (await _sut.HasKeyAsync(provider, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ExistingProviderWithoutNewKey_KeepsStoredKey()
    {
        // Arrange
        await _sut.SaveAsync(Provider("ds", "m1"), "sk-1", TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveAsync(Provider("ds", "m1") with { DisplayName = "Renamed" }, null, TestContext.Current.CancellationToken);

        // Assert
        _sut.Config.Providers.Should().ContainSingle().Which.DisplayName.Should().Be("Renamed");
        (await _secrets.GetAsync("provider:ds", TestContext.Current.CancellationToken)).Should().Be("sk-1");
    }

    [Fact]
    public async Task SaveAsync_InvalidProvider_ReturnsIssuesAndSavesNothing()
    {
        // Act
        var issues = await _sut.SaveAsync(Provider("ds", "m1") with { BaseUrl = "nope" }, "sk-1", TestContext.Current.CancellationToken);

        // Assert
        issues.Should().NotBeEmpty();
        _sut.Config.Providers.Should().BeEmpty();
        (await _secrets.GetAsync("provider:ds", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_DefaultProviderLosesItsDefaultModel_PicksItsFirstModel()
    {
        // Arrange
        await _sut.SaveAsync(Provider("ds", "m1"), null, TestContext.Current.CancellationToken);
        await _sut.SetDefaultAsync("ds", "m1", TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveAsync(Provider("ds", "m2"), null, TestContext.Current.CancellationToken);

        // Assert
        _sut.Config.DefaultModelId.Should().Be("m2");
    }

    [Fact]
    public async Task SaveAsync_KeyWithSurroundingWhitespace_StoresItTrimmed()
    {
        // Act
        await _sut.SaveAsync(Provider("ds", "m1"), " sk-1\n", TestContext.Current.CancellationToken);

        // Assert
        (await _secrets.GetAsync("provider:ds", TestContext.Current.CancellationToken)).Should().Be("sk-1");
    }

    [Fact]
    public async Task SaveAsync_RaisesChanged()
    {
        // Arrange
        var raised = 0;
        _sut.Changed += (_, _) => raised++;

        // Act
        await _sut.SaveAsync(Provider("ds", "m1"), null, TestContext.Current.CancellationToken);

        // Assert
        raised.Should().Be(1);
    }

    [Fact]
    public async Task ClearKeyAsync_DeletesOnlyThatProvidersKey_AndKeepsProvider()
    {
        // Arrange
        await _sut.SaveAsync(Provider("a", "m1"), "ka", TestContext.Current.CancellationToken);
        await _sut.SaveAsync(Provider("b", "m2"), "kb", TestContext.Current.CancellationToken);

        // Act
        await _sut.ClearKeyAsync("a", TestContext.Current.CancellationToken);

        // Assert
        (await _secrets.GetAsync("provider:a", TestContext.Current.CancellationToken)).Should().BeNull();
        (await _secrets.GetAsync("provider:b", TestContext.Current.CancellationToken)).Should().Be("kb");
        _sut.Config.Providers.Select(p => p.Id).Should().Equal("a", "b");
    }

    [Fact]
    public async Task RemoveAsync_DeletesKey_AndMovesDefaultToRemainingProvider()
    {
        // Arrange
        await _sut.SaveAsync(Provider("a", "m1"), "ka", TestContext.Current.CancellationToken);
        await _sut.SaveAsync(Provider("b", "m2"), "kb", TestContext.Current.CancellationToken);

        // Act
        await _sut.RemoveAsync("a", TestContext.Current.CancellationToken);

        // Assert
        _sut.Config.Providers.Select(p => p.Id).Should().Equal("b");
        _sut.Config.DefaultProviderId.Should().Be("b");
        _sut.Config.DefaultModelId.Should().Be("m2");
        (await _secrets.GetAsync("provider:a", TestContext.Current.CancellationToken)).Should().BeNull();
        (await _secrets.GetAsync("provider:b", TestContext.Current.CancellationToken)).Should().Be("kb");
    }

    [Fact]
    public async Task RemoveAsync_LastProvider_ClearsDefaults()
    {
        // Arrange
        await _sut.SaveAsync(Provider("a", "m1"), null, TestContext.Current.CancellationToken);

        // Act
        await _sut.RemoveAsync("a", TestContext.Current.CancellationToken);

        // Assert
        _sut.Config.DefaultProviderId.Should().BeNull();
        _sut.Config.DefaultModelId.Should().BeNull();
    }

    [Fact]
    public async Task SetDefaultAsync_UnknownModel_ReturnsFalse()
    {
        // Arrange
        await _sut.SaveAsync(Provider("a", "m1"), null, TestContext.Current.CancellationToken);

        // Act
        var saved = await _sut.SetDefaultAsync("a", "nope", TestContext.Current.CancellationToken);

        // Assert
        saved.Should().BeFalse();
    }

    [Fact]
    public async Task SetDefaultAsync_KnownModel_Saves()
    {
        // Arrange
        await _sut.SaveAsync(Provider("a", "m1", "m2"), null, TestContext.Current.CancellationToken);

        // Act
        var saved = await _sut.SetDefaultAsync("a", "m2", TestContext.Current.CancellationToken);

        // Assert
        saved.Should().BeTrue();
        _sut.Config.DefaultModelId.Should().Be("m2");
    }

    [Fact]
    public async Task HasKeyAsync_ReflectsStoredKey()
    {
        // Arrange
        var provider = Provider("a", "m1");
        await _sut.SaveAsync(provider, null, TestContext.Current.CancellationToken);
        var before = await _sut.HasKeyAsync(provider, TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveAsync(provider, "k", TestContext.Current.CancellationToken);
        var after = await _sut.HasKeyAsync(provider, TestContext.Current.CancellationToken);

        // Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }

    [Theory]
    [InlineData("DeepSeek", "deepseek")]
    [InlineData("  My Local  LLM! ", "my-local-llm")]
    [InlineData("!!!", "provider")]
    public void NewId_Slugifies(string name, string expected)
    {
        // Act
        var id = _sut.NewId(name);

        // Assert
        id.Should().Be(expected);
    }

    [Fact]
    public async Task NewId_TakenId_AppendsNumber()
    {
        // Arrange
        await _sut.SaveAsync(Provider("deepseek", "m1"), null, TestContext.Current.CancellationToken);

        // Act
        var id = _sut.NewId("DeepSeek");

        // Assert
        id.Should().Be("deepseek-2");
    }

    private static ProviderConfig Provider(string id, params string[] models)
    {
        return new ProviderConfig
        {
            Id = id,
            DisplayName = id,
            BaseUrl = "https://api.example.com/v1",
            Models = [.. models.Select(m => new ModelConfig { Id = m, ContextWindow = 8000, MaxOutputTokens = 1000 })],
        };
    }
}
