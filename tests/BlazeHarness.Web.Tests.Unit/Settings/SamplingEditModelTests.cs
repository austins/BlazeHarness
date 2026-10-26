using BlazeHarness.Core.Providers;
using BlazeHarness.Web.Settings;
using System.Text.Json;

namespace BlazeHarness.Web.Tests.Unit.Settings;

public sealed class SamplingEditModelTests
{
    [Fact]
    public void ToConfig_NothingSet_ReturnsNull()
    {
        // Arrange
        var sut = new SamplingEditModel();

        // Act
        var result = sut.ToConfig();

        // Assert
        sut.IsEmpty.Should().BeTrue();
        result.Should().BeNull();
    }

    [Fact]
    public void ToConfig_RoundTrip_PreservesValuesAndExtras()
    {
        // Arrange
        var original = new SamplingParams
        {
            Temperature = 0.6,
            TopK = 20,
            Extra = new Dictionary<string, JsonElement> { ["mirostat"] = JsonDocument.Parse("2").RootElement.Clone() },
        };

        // Act
        var result = SamplingEditModel.FromConfig(original).ToConfig();

        // Assert
        result.Should().NotBeNull();
        result.Temperature.Should().Be(0.6);
        result.TopK.Should().Be(20);
        result.TopP.Should().BeNull();
        result.Extra!["mirostat"].GetRawText().Should().Be("2");
    }

    [Fact]
    public void Set_Null_UnsetsValue()
    {
        // Arrange
        var sut = new SamplingEditModel();
        sut.Set("temperature", 0.5);

        // Act
        sut.Set("temperature", null);

        // Assert
        sut.Get("temperature").Should().BeNull();
        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToConfig_ExtraWithPlainText_SendsAsString()
    {
        // Arrange
        var sut = new SamplingEditModel();
        sut.Extra.Add(new ExtraParamRow { Name = "grammar", Json = "root ::= x" });

        // Act
        var result = sut.ToConfig();

        // Assert
        result!.Extra!["grammar"].ValueKind.Should().Be(JsonValueKind.String);
    }
}
