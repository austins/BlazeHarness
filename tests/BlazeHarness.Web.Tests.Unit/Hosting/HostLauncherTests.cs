using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class HostLauncherTests
{
    [Theory]
    [InlineData("--port", "8123", 8123)]
    [InlineData("--port", "abc", 0)]
    [InlineData("--port", "70000", 0)]
    public void ConfiguredPort_FromArgs_ParsesOrFallsBackToFreePort(string flag, string value, int expected)
    {
        // Act
        var port = HostLauncher.ConfiguredPort([flag, value]);

        // Assert
        port.Should().Be(expected);
    }
}
