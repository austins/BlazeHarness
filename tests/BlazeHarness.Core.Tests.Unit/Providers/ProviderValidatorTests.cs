using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ProviderValidatorTests
{
    private static ProviderConfig Valid => new()
    {
        Id = "p",
        DisplayName = "P",
        BaseUrl = "https://api.example.com/v1",
        Models = [new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 }],
    };

    [Fact]
    public void Validate_ValidProvider_HasNoIssues()
    {
        // Act
        var issues = ProviderValidator.Validate(Valid);

        // Assert
        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://host/v1")]
    public void Validate_BadBaseUrl_ReportsIssue(string baseUrl)
    {
        // Act
        var issues = ProviderValidator.Validate(Valid with { BaseUrl = baseUrl });

        // Assert
        issues.Should().ContainSingle(i => i.Path == "baseUrl");
    }

    [Fact]
    public void Validate_LiteralApiKeyRef_ReportsIssue()
    {
        // Act
        var issues = ProviderValidator.Validate(Valid with { ApiKeyRef = "sk-secret" });

        // Assert
        issues.Should().ContainSingle(i => i.Path == "apiKeyRef");
    }

    [Fact]
    public void Validate_EnvApiKeyRef_IsAccepted()
    {
        // Act
        var issues = ProviderValidator.Validate(Valid with { ApiKeyRef = "${MY_KEY}" });

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_DuplicateModelIds_ReportsIssue()
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model, model] });

        // Assert
        issues.Should().Contain(i => i.Message.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MaxOutputNotBelowContext_ReportsIssue()
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 1000, MaxOutputTokens = 1000 };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().ContainSingle(i => i.Path.EndsWith("maxOutputTokens", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NonPositiveContextWindow_ReportsIssue()
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 0, MaxOutputTokens = 100 };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().Contain(i => i.Path.EndsWith("contextWindow", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(-0.1)]
    public void Validate_TemperatureOutOfRange_ReportsIssue(double temperature)
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            ContextWindow = 8000,
            MaxOutputTokens = 1000,
            SamplingParams = new SamplingParams { Temperature = temperature },
        };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().ContainSingle(i => i.Path.EndsWith("temperature", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LevelOverrideOutOfRange_ReportsIssueAtLevelPath()
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            ContextWindow = 8000,
            MaxOutputTokens = 1000,
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.High] = new() { Overrides = new SamplingParams { TopP = 3 } },
            },
        };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().ContainSingle(i => i.Path.Contains("levels.high", StringComparison.Ordinal));
    }
}
