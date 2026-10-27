using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class AppConfigSerializationTests
{
    [Fact]
    public void RoundTrip_LevelsAndUnknownFields_ArePreserved()
    {
        // Arrange
        const string json = """
            {
              "providers": [{
                "id": "p", "displayName": "P", "baseUrl": "http://x/v1",
                "models": [{ "id": "m", "levels": { "off": { "value": "none" }, "xhigh": { "value": "max" } } }]
              }],
              "futureSetting": 42
            }
            """;

        // Act
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options)!;
        var written = JsonSerializer.Serialize(config, JsonDefaults.Options);

        // Assert
        var levels = config.Providers[0].Models[0].Levels!;
        levels[ThinkingLevel.Off].Value.Should().Be("none");
        levels[ThinkingLevel.Xhigh].Value.Should().Be("max");
        written.Should().Contain("futureSetting");
        written.Should().Contain("\"xhigh\"");
    }

    [Fact]
    public void Serialize_DoesNotContainApiKeyValues()
    {
        // Arrange
        var config = new AppConfig
        {
            Providers = [new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = new Uri("http://x"), ApiKeyRef = "${KEY}" }],
        };

        // Act
        var json = JsonSerializer.Serialize(config, JsonDefaults.Options);

        // Assert
        json.Should().Contain("${KEY}").And.NotContain("sk-");
    }
}
