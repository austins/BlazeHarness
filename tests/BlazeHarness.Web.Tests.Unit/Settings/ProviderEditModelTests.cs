using BlazeHarness.Core.Providers;
using BlazeHarness.Web.Settings;

namespace BlazeHarness.Web.Tests.Unit.Settings;

public sealed class ProviderEditModelTests
{
    [Fact]
    public void ToConfig_RoundTrip_PreservesProvider()
    {
        // Arrange
        var provider = new ProviderConfig
        {
            Id = "ds",
            DisplayName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            ApiKeyRef = "${DEEPSEEK_API_KEY}",
            Headers = new Dictionary<string, string> { ["X-Org"] = "acme" },
            SendStreamUsage = false,
            LegacyMaxTokens = false,
            ThinkingSend = new ThinkingSend { Mode = ThinkingSendMode.BodyField, Field = "thinking.level" },
            Models =
            [
                new ModelConfig
                {
                    Id = "deepseek-chat",
                    DisplayName = "Chat",
                    ContextWindow = 64000,
                    MaxOutputTokens = 8000,
                    Reasoning = true,
                    Input = InputTypes.Text | InputTypes.Image,
                },
            ],
        };

        // Act
        var result = ProviderEditModel.FromConfig(provider).ToConfig();

        // Assert
        result.Id.Should().Be("ds");
        result.BaseUrl.Should().Be("https://api.deepseek.com/v1");
        result.ApiKeyRef.Should().Be("${DEEPSEEK_API_KEY}");
        result.Headers.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("X-Org", "acme"));
        result.SendStreamUsage.Should().BeFalse();
        result.LegacyMaxTokens.Should().BeFalse();
        result.ThinkingSend.Mode.Should().Be(ThinkingSendMode.BodyField);
        result.ThinkingSend.Field.Should().Be("thinking.level");
        result.Models.Should().ContainSingle().Which.Should().BeEquivalentTo(provider.Models[0]);
    }

    [Fact]
    public void ToConfig_NoKeyOrHeaders_LeavesThemNull()
    {
        // Arrange
        var sut = new ProviderEditModel { Id = "local", DisplayName = "Local", BaseUrl = "http://localhost:8080/v1" };

        // Act
        var result = sut.ToConfig();

        // Assert
        result.ApiKeyRef.Should().BeNull();
        result.Headers.Should().BeNull();
    }

    [Fact]
    public void ToConfig_BlankAndDuplicateHeaders_DropsBlankAndKeepsLast()
    {
        // Arrange
        var sut = new ProviderEditModel { Id = "p", DisplayName = "P", BaseUrl = "http://x/v1" };
        sut.Headers.Add(new HeaderRow { Name = " ", Value = "ignored" });
        sut.Headers.Add(new HeaderRow { Name = "X-A", Value = "1" });
        sut.Headers.Add(new HeaderRow { Name = "x-a", Value = "2" });

        // Act
        var result = sut.ToConfig();

        // Assert
        result.Headers.Should().ContainSingle().Which.Value.Should().Be("2");
    }

    [Fact]
    public void ToConfig_NonBodyFieldMode_DropsField()
    {
        // Arrange
        var sut = new ProviderEditModel
        {
            Id = "p",
            DisplayName = "P",
            BaseUrl = "http://x/v1",
            ThinkingMode = ThinkingSendMode.ReasoningEffort,
            ThinkingField = "left.over",
        };

        // Act
        var result = sut.ToConfig();

        // Assert
        result.ThinkingSend.Field.Should().BeNull();
    }

    [Fact]
    public void FromId_KnownModel_AppliesPreset()
    {
        // Act
        var result = ModelEditModel.FromId("openai/gpt-4o");

        // Assert
        result.ContextWindow.Should().BeGreaterThan(0);
        result.MaxOutputTokens.Should().BeGreaterThan(0);
        result.Image.Should().BeTrue();
    }

    [Fact]
    public void FromId_UnknownModel_LeavesLimitsAtZero()
    {
        // Act
        var result = ModelEditModel.FromId("mystery");

        // Assert
        result.ContextWindow.Should().Be(0);
        result.MaxOutputTokens.Should().Be(0);
    }

    [Fact]
    public void ToConfig_ModelWithoutImage_IsTextOnly()
    {
        // Arrange
        var sut = new ModelEditModel { Id = " m ", DisplayName = " ", ContextWindow = 10, MaxOutputTokens = 5 };

        // Act
        var result = sut.ToConfig();

        // Assert
        result.Id.Should().Be("m");
        result.DisplayName.Should().BeNull();
        result.Input.Should().Be(InputTypes.Text);
    }
}
