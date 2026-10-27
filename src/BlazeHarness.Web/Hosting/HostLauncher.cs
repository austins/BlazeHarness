using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Diagnostics;
using System.Net;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Starts the server on a free loopback port and opens the browser with the one-time launch URL.
/// </summary>
public static class HostLauncher
{
    /// <summary>
    /// The environment variable that pins the listening port.
    /// </summary>
    public const string PortEnvironmentVariable = "BLAZEHARNESS_PORT";

    /// <summary>
    /// The environment variable that, when set to any value, stops the browser from opening automatically.
    /// </summary>
    public const string NoBrowserEnvironmentVariable = "BLAZEHARNESS_NO_BROWSER";

    /// <summary>
    /// Get the port the user asked for.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--port N</c> wins over the environment variable.</param>
    /// <returns>The requested port, or 0 to let the OS pick a free one when none or an invalid one was given.</returns>
    public static int ConfiguredPort(string[] args) => ConfiguredPort(args, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Get the port the user asked for, reading the environment through the given function.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--port N</c> wins over the environment variable.</param>
    /// <param name="getEnvironmentVariable">Returns the value of an environment variable, or null when it is not set.</param>
    /// <returns>The requested port, or 0 to let the OS pick a free one when none or an invalid one was given.</returns>
    public static int ConfiguredPort(string[] args, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var index = Array.IndexOf(args, "--port");
        var text = index >= 0 && index + 1 < args.Length ? args[index + 1] : getEnvironmentVariable(PortEnvironmentVariable);
        return int.TryParse(text, out var port) && port is >= 0 and <= 65535 ? port : 0;
    }

    /// <summary>
    /// Decide whether to open the browser after startup.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--no-browser</c> turns it off.</param>
    /// <returns>False when <c>--no-browser</c> or <see cref="NoBrowserEnvironmentVariable"/> is set.</returns>
    public static bool ShouldOpenBrowser(string[] args) => ShouldOpenBrowser(args, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Decide whether to open the browser after startup, reading the environment through the given function.
    /// </summary>
    /// <param name="args">The command line arguments; <c>--no-browser</c> turns it off.</param>
    /// <param name="getEnvironmentVariable">Returns the value of an environment variable, or null when it is not set.</param>
    /// <returns>False when <c>--no-browser</c> or <see cref="NoBrowserEnvironmentVariable"/> is set.</returns>
    public static bool ShouldOpenBrowser(string[] args, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        return !args.Contains("--no-browser") && string.IsNullOrEmpty(getEnvironmentVariable(NoBrowserEnvironmentVariable));
    }

    /// <summary>
    /// Start the app, print the one-time launch link, optionally open the browser, and wait for shutdown.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="args">The command line arguments.</param>
    /// <returns>A task that completes when the app shuts down.</returns>
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

    /// <summary>
    /// Make Kestrel listen on the loopback interface only.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="port">The port to listen on, or 0 for a free one.</param>
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
