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
    public void ReachableUrls_AddressesAndName_ListsEachWithThePort()
    {
        // Act
        var urls = HostLauncher.ReachableUrls([IPAddress.Parse("192.168.1.38"), IPAddress.Parse("10.0.0.4")], "mypc", 5877);

        // Assert
        urls.Should().Equal(
            "http://192.168.1.38:5877",
            "http://10.0.0.4:5877",
            "http://mypc:5877 (only if your network resolves the name)");
    }

    [Fact]
    public void ReachableUrls_NoName_ListsOnlyAddresses()
    {
        // Act
        var urls = HostLauncher.ReachableUrls([IPAddress.Parse("192.168.1.38")], string.Empty, 5877);

        // Assert
        urls.Should().Equal("http://192.168.1.38:5877");
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

    [Fact]
    public void ConfiguredPort_NothingSet_UsesTheDefault()
    {
        // Act
        var port = HostLauncher.ConfiguredPort([], Setting("x", null));

        // Assert
        port.Should().Be(HostLauncher.DefaultPort);
    }

    [Theory]
    [InlineData("8123", 8123)]
    [InlineData("0", 0)]
    public void ConfiguredPort_FromArgs_ParsesIt(string value, int expected)
    {
        // Act
        var port = HostLauncher.ConfiguredPort(["--port", value], Setting("x", null));

        // Assert
        port.Should().Be(expected);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("70000")]
    [InlineData("-1")]
    public void ConfiguredPort_InvalidArg_Throws(string value)
    {
        // Act
        var act = () => HostLauncher.ConfiguredPort(["--port", value], Setting("x", null));

        // Assert
        act.Should().Throw<InvalidOperationException>();
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
    public void ConfiguredPort_InvalidEnvironment_Throws(string value)
    {
        // Act
        var act = () => HostLauncher.ConfiguredPort([], Setting(HostLauncher.PortSettingKey, value));

        // Assert
        act.Should().Throw<InvalidOperationException>();
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
