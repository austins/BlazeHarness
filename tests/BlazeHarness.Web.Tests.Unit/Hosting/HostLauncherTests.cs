using BlazeHarness.Web.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class HostLauncherTests
{
    [Theory]
    [InlineData("0.0.0.0", true)]
    [InlineData(" 0.0.0.0 ", true)]
    [InlineData("localhost", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("192.168.1.5", false)]
    [InlineData("", false)]
    public void ConfiguredHost_FromEnvironment_OnlyAllInterfacesOptsIn(string value, bool expectedAny)
    {
        // Act
        var address = HostLauncher.ConfiguredHost([], Setting(HostLauncher.HostSettingKey, value));

        // Assert
        address.Should().Be(expectedAny ? IPAddress.Any : IPAddress.Loopback);
    }

    [Fact]
    public void ConfiguredHost_NothingSet_IsLoopback()
    {
        // Act
        var address = HostLauncher.ConfiguredHost([], Setting("x", null));

        // Assert
        address.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public void ConfiguredHost_ArgAndEnvironment_ArgWins()
    {
        // Act
        var address = HostLauncher.ConfiguredHost(["--host", "0.0.0.0"], Setting(HostLauncher.HostSettingKey, "localhost"));

        // Assert
        address.Should().Be(IPAddress.Any);
    }

    [Theory]
    [InlineData("--port", "8123", 8123)]
    [InlineData("--port", "abc", 0)]
    [InlineData("--port", "70000", 0)]
    [InlineData("--port", "-1", 0)]
    public void ConfiguredPort_FromArgs_ParsesOrFallsBackToFreePort(string flag, string value, int expected)
    {
        // Act
        var port = HostLauncher.ConfiguredPort([flag, value], Setting("x", null));

        // Assert
        port.Should().Be(expected);
    }

    [Fact]
    public void ConfiguredPort_OnlyEnvironment_UsesIt()
    {
        // Act
        var port = HostLauncher.ConfiguredPort([], Setting(HostLauncher.PortSettingKey, "9001"));

        // Assert
        port.Should().Be(9001);
    }

    [Fact]
    public void ConfiguredPort_ArgAndEnvironment_ArgWins()
    {
        // Act
        var port = HostLauncher.ConfiguredPort(["--port", "8123"], Setting(HostLauncher.PortSettingKey, "9001"));

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
        var port = HostLauncher.ConfiguredPort([], Setting(HostLauncher.PortSettingKey, value));

        // Assert
        port.Should().Be(0);
    }

    [Fact]
    public void ConfiguredPort_PortFlagWithoutValue_FallsBackToTheEnvironment()
    {
        // Act
        var port = HostLauncher.ConfiguredPort(["--port"], Setting(HostLauncher.PortSettingKey, "9001"));

        // Assert
        port.Should().Be(9001);
    }

    [Fact]
    public void ShouldOpenBrowser_Nothing_IsTrue()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], Setting("x", null));

        // Assert
        open.Should().BeTrue();
    }

    [Fact]
    public void ShouldOpenBrowser_NoBrowserFlag_IsFalse()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser(["--no-browser"], Setting("x", null));

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void ShouldOpenBrowser_EnvironmentSet_IsFalse()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], Setting(HostLauncher.NoBrowserSettingKey, "1"));

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void ShouldOpenBrowser_EnvironmentEmpty_IsTrue()
    {
        // Act
        var open = HostLauncher.ShouldOpenBrowser([], Setting(HostLauncher.NoBrowserSettingKey, string.Empty));

        // Assert
        open.Should().BeTrue();
    }

    private static IConfiguration Setting(string key, string? value)
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration[key].Returns(value);
        return configuration;
    }
}
