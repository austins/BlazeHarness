using BlazeHarness.Core.Web;
using System.Net;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class WebAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void IsPublic_PrivateOrLocal_False(string address)
    {
        // Act
        var result = WebAddressPolicy.IsPublic(IPAddress.Parse(address));

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublic_PublicAddress_True(string address)
    {
        // Act
        var result = WebAddressPolicy.IsPublic(IPAddress.Parse(address));

        // Assert
        result.Should().BeTrue();
    }
}
