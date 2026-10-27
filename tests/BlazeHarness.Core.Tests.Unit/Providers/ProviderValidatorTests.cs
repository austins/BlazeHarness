using BlazeHarness.Core.Providers;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ProviderValidatorTests
{
    private static ProviderConfig Valid => new()
    {
        Id = "p",
        DisplayName = "P",
        BaseUrl = new Uri("https://api.example.com/v1"),
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
#pragma warning disable CA1054 // InlineData requires string constants, including deliberately malformed addresses.
    public void Validate_BadBaseUrl_ReportsIssue(string baseUrl)
#pragma warning restore CA1054
    {
        // Act
        var issues = ProviderValidator.Validate(Valid with { BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute) });

        // Assert
        issues.Should().ContainSingle(i => i.Path == "baseUrl");
    }

    [Theory]
    [InlineData("top k")]
    [InlineData("a[0]")]
    [InlineData("a\"b")]
    [InlineData("$.a")]
    public void Validate_ExtraKeyThatCannotBeAPath_ReportsIssue(string key)
    {
        // Arrange
        var model = Valid.Models[0] with { SamplingParams = new SamplingParams { Extra = new Dictionary<string, JsonElement> { [key] = JsonDocument.Parse("1").RootElement } } };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().ContainSingle(i => i.Path == "models[0].samplingParams.extra");
    }

    [Fact]
    public void Validate_ExtraKeyWithDotsAndDashes_IsValid()
    {
        // Arrange
        var model = Valid.Models[0] with { SamplingParams = new SamplingParams { Extra = new Dictionary<string, JsonElement> { ["chat_template_kwargs.enable-thinking"] = JsonDocument.Parse("true").RootElement } } };

        // Act
        var issues = ProviderValidator.Validate(Valid with { Models = [model] });

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ThinkingFieldWithASpace_ReportsIssue()
    {
        // Act
        var issues = ProviderValidator.Validate(Valid with { ThinkingSend = new ThinkingSend { Mode = ThinkingSendMode.BodyField, Field = "think ing" } });

        // Assert
        issues.Should().ContainSingle(i => i.Path == "thinkingSend.field");
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
