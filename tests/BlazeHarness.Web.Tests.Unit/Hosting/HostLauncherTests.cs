using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class HostLauncherTests
{
    [Theory]
    [InlineData("--port", "8123", 8123)]
    [InlineData("--port", "abc", 0)]
    [InlineData("--port", "70000", 0)]
    [InlineData("--port", "-1", 0)]
    public void ConfiguredPort_FromArgs_ParsesOrFallsBackToFreePort(string flag, string value, int expected)
    {
        // Act
        var port = HostLauncher.ConfiguredPort([flag, value], _ => null);

        // Assert
        port.Should().Be(expected);
    }

    [Fact]
    public void ConfiguredPort_OnlyEnvironment_UsesIt()
    {
        // Act
        var port = HostLauncher.ConfiguredPort([], name => name == HostLauncher.PortEnvironmentVariable ? "9001" : null);

        // Assert
        port.Should().Be(9001);
    }

    [Fact]
    public void ConfiguredPort_ArgAndEnvironment_ArgWins()
    {
        // Act
        var port = HostLauncher.ConfiguredPort(["--port", "8123"], _ => "9001");

        // Assert
        port.Should().Be(8123);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("99999")]
    [InlineData("")]
    public void ConfiguredPort_InvalidEnvironment_FallsBackToFreePort(string value)
    {
        // Act
        var port = HostLauncher.ConfiguredPort([], _ => value);

        // Assert
        port.Should().Be(0);
    }

    [Fact]
    public void ConfiguredPort_PortFlagWithoutValue_FallsBackToTheEnvironment()
    {
        // Act
        var port = HostLauncher.ConfiguredPort(["--port"], _ => "9001");

        // Assert
        port.Should().Be(9001);
    }

    [Fact]
    public void ShouldOpenBrowser_Nothing_IsTrue()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], _ => null);

        // Assert
        open.Should().BeTrue();
    }

    [Fact]
    public void ShouldOpenBrowser_NoBrowserFlag_IsFalse()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser(["--no-browser"], _ => null);

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void ShouldOpenBrowser_EnvironmentSet_IsFalse()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], name => name == HostLauncher.NoBrowserEnvironmentVariable ? "1" : null);

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void ShouldOpenBrowser_EnvironmentEmpty_IsTrue()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], _ => string.Empty);

        // Assert
        open.Should().BeTrue();
    }
}
