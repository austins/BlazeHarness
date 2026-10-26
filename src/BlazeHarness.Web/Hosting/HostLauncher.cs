using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Diagnostics;
using System.Net;

namespace BlazeHarness.Web.Hosting;

/// <summary>Starts the server on a free loopback port and opens the browser with the one-time launch URL.</summary>
public static class HostLauncher
{
    public const string PortEnvironmentVariable = "BLAZEHARNESS_PORT";

    public const string NoBrowserEnvironmentVariable = "BLAZEHARNESS_NO_BROWSER";

    public static int ConfiguredPort(string[] args)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
        var index = Array.IndexOf(args, "--port");
        var text = index >= 0 && index + 1 < args.Length ? args[index + 1] : fromEnvironment;
        return int.TryParse(text, out var port) && port is >= 0 and <= 65535 ? port : 0;
    }

    public static bool ShouldOpenBrowser(string[] args)
    {
        return !args.Contains("--no-browser") && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(NoBrowserEnvironmentVariable));
    }

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var port = new Uri(address).Port;
        var token = app.Services.GetRequiredService<LaunchCredentials>().Token;
        var launchUrl = $"http://127.0.0.1:{port}{LocalAccessMiddleware.LaunchPath}?token={token}";

        Console.WriteLine($"BlazeHarness is running at http://127.0.0.1:{port}");
        Console.WriteLine($"Open this link once to sign in: {launchUrl}");

        if (ShouldOpenBrowser(args))
        {
            TryOpenBrowser(launchUrl);
        }

        await app.WaitForShutdownAsync();
    }

    public static void ConfigureKestrel(WebApplicationBuilder builder, int port)
    {
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine("Could not open a browser automatically; use the link above.");
        }
    }
}
