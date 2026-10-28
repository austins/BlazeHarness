using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Diagnostics;
using System.Net;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Starts the server on a free loopback port (or the configured address) and opens the browser.
/// </summary>
public static class HostLauncher
{
    /// <summary>
    /// The configuration key (and environment variable) that picks the address to listen on.
    /// </summary>
    public const string HostSettingKey = "BLAZEHARNESS_HOST";

    /// <summary>
    /// The configuration key (and environment variable) that pins the listening port.
    /// </summary>
    public const string PortSettingKey = "BLAZEHARNESS_PORT";

    /// <summary>
    /// The configuration key (and environment variable) that, when set to any value, stops the browser from opening automatically.
    /// </summary>
    public const string NoBrowserSettingKey = "BLAZEHARNESS_NO_BROWSER";

    /// <summary>
    /// Get the address the user asked to listen on.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--host X</c> wins over the setting.</param>
    /// <param name="configuration">The app configuration, which carries <see cref="HostSettingKey"/> (from the environment by default).</param>
    /// <returns>
    /// <see cref="IPAddress.Any"/> for <c>0.0.0.0</c>, otherwise loopback, including when the value is missing or not recognized
    /// (<c>localhost</c> and <c>127.0.0.1</c> are the accepted spellings).
    /// </returns>
    public static IPAddress ConfiguredHost(string[] args, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);

        var index = Array.IndexOf(args, "--host");
        var text = index >= 0 && index + 1 < args.Length ? args[index + 1] : configuration[HostSettingKey];
        return string.Equals(text?.Trim(), "0.0.0.0", StringComparison.Ordinal) ? IPAddress.Any : IPAddress.Loopback;
    }

    /// <summary>
    /// Get the port the user asked for.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--port N</c> wins over the setting.</param>
    /// <param name="configuration">The app configuration, which carries <see cref="PortSettingKey"/> (from the environment by default).</param>
    /// <returns>The requested port, or 0 to let the OS pick a free one when none or an invalid one was given.</returns>
    public static int ConfiguredPort(string[] args, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);

        var index = Array.IndexOf(args, "--port");
        var text = index >= 0 && index + 1 < args.Length ? args[index + 1] : configuration[PortSettingKey];
        return int.TryParse(text, out var port) && port is >= 0 and <= 65535 ? port : 0;
    }

    /// <summary>
    /// Decide whether to open the browser after startup.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--no-browser</c> turns it off.</param>
    /// <param name="configuration">The app configuration, which carries <see cref="NoBrowserSettingKey"/> (from the environment by default).</param>
    /// <returns>False when <c>--no-browser</c> or <see cref="NoBrowserSettingKey"/> is set.</returns>
    public static bool ShouldOpenBrowser(string[] args, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);

        return !args.Contains("--no-browser") && string.IsNullOrEmpty(configuration[NoBrowserSettingKey]);
    }

    /// <summary>
    /// Start the app, print its URL, optionally open the browser, and wait for shutdown.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="args">The command line arguments.</param>
    /// <returns>A task that completes when the app shuts down.</returns>
    public static async Task RunAsync(WebApplication app, string[] args)
    {
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var port = new Uri(address).Port;
        var url = $"http://127.0.0.1:{port}";

        Console.WriteLine($"BlazeHarness is running at {url}");
        if (app.Services.GetRequiredService<AccessPolicy>().AllowAnyHost)
        {
            Console.WriteLine($"Listening on all interfaces over plain HTTP with no sign-in: anyone who can reach port {port} can use it. Use this machine's IP address from other devices, and limit access with a firewall.");
        }

        if (ShouldOpenBrowser(args, app.Configuration))
        {
            TryOpenBrowser(url);
        }

        await app.WaitForShutdownAsync();
    }

    /// <summary>
    /// Make Kestrel listen on one address.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="address">The address to listen on; loopback unless the user opted in to all interfaces.</param>
    /// <param name="port">The port to listen on, or 0 for a free one.</param>
    public static void ConfigureKestrel(WebApplicationBuilder builder, IPAddress address, int port)
    {
        builder.WebHost.ConfigureKestrel(options => options.Listen(address, port));
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
