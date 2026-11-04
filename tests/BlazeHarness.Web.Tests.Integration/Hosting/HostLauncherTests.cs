using BlazeHarness.Tests.Shared;
using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class HostLauncherTests
{
    [Fact]
    public async Task ListeningPort_EphemeralListener_ReturnsTheBoundPort()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[HostLauncher.NoBrowserSettingKey] = "1";
        HostLauncher.ConfigureKestrel(builder, IPAddress.Loopback, 0);
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken).Bounded();
        try
        {
            // Act
            var port = HostLauncher.ListeningPort(app.Services.GetRequiredService<IServer>());

            // Assert
            port.Should().BeInRange(1, 65535);
            using var client = new HttpClient();
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"), TestContext.Current.CancellationToken).Bounded();
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken).Bounded();
        }
    }
}
