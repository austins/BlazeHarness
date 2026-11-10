using BlazeHarness.Web.Hosting;
using System.Net;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class ClientMachineTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("::1", "::1", true)]
    [InlineData("::ffff:127.0.0.1", "::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.5", "192.168.1.5", true)]
    [InlineData("::ffff:192.168.1.5", "192.168.1.5", true)]
    [InlineData("192.168.1.9", "192.168.1.5", false)]
    public void IsLocalConnection_Addresses_ComparesThem(string remote, string local, bool expected)
    {
        // Act
        var isLocal = ClientMachine.IsLocalConnection(IPAddress.Parse(remote), IPAddress.Parse(local));

        // Assert
        isLocal.Should().Be(expected);
    }

    [Fact]
    public void IsLocalConnection_NoRemoteAddress_ReturnsFalse()
    {
        // Act
        var isLocal = ClientMachine.IsLocalConnection(null, IPAddress.Loopback);

        // Assert
        isLocal.Should().BeFalse();
    }
}
