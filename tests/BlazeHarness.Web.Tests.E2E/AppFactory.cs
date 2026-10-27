using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace BlazeHarness.Web.Tests.E2E;

/// <summary>
/// Hosts the app on a real Kestrel port with its own data folder, so instances can run side by side.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _home;

    public AppFactory(string home)
    {
        _home = home;
        UseKestrel(0);
        StartServer();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Static web assets are read from the project's manifest instead of published wwwroot files.
        builder.UseStaticWebAssets();
        builder.UseEnvironment(Environments.Staging);
        builder.UseSetting(AppPaths.HomeSettingKey, _home);
    }
}
