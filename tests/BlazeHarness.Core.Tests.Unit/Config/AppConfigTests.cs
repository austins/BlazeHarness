using BlazeHarness.Core.Config;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Config;

public sealed class AppConfigTests
{
    [Fact]
    public void RoundTrip_UnrecognizedSettings_ArePreserved()
    {
        // Arrange
        const string json = """{"sideTaskModel":"provider/model","futureSetting":42}""";

        // Act
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options)!;
        var written = JsonSerializer.Serialize(config with { TodosEnabled = false }, JsonDefaults.Options);
        using var document = JsonDocument.Parse(written);

        // Assert
        config.Extra!["sideTaskModel"].GetString().Should().Be("provider/model");
        document.RootElement.GetProperty("sideTaskModel").GetString().Should().Be("provider/model");
        document.RootElement.GetProperty("futureSetting").GetInt32().Should().Be(42);
        document.RootElement.GetProperty("todosEnabled").GetBoolean().Should().BeFalse();
    }
}
