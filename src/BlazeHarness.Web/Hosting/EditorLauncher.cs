using BlazeHarness.Core.Config;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools.Shell;
using System.ComponentModel;
using System.Diagnostics;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Opens a file of a folder in a text editor on the machine running the app.
/// </summary>
/// <remarks>
/// The editor is started directly, never through a shell or the file's default handler, so opening a script or shortcut the agent wrote never runs it.
/// The command comes from <see cref="AppConfig.EditorCommand"/>, which sits in the app's home folder where the agent can't write.
/// </remarks>
/// <param name="config">The app settings holding the editor command.</param>
/// <param name="logger">The logger.</param>
/// <param name="permissions">Supplies the protected folders that may not be opened, or null in tests without one.</param>
/// <param name="time">Times looking up the system's text editor; defaults to the system clock.</param>
internal sealed partial class EditorLauncher(
    JsonFileStore<AppConfig> config,
    ILogger<EditorLauncher> logger,
    PermissionDefaults? permissions = null,
    TimeProvider? time = null)
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Open a file in the configured editor, or the system's text editor when none is set.
    /// </summary>
    /// <param name="root">The folder the file belongs to.</param>
    /// <param name="relativePath">The file's path relative to <paramref name="root"/>.</param>
    /// <param name="cancellationToken">A token to cancel finding the system's editor.</param>
    /// <returns>Whether the editor started.</returns>
    public async Task<EditorOpenResult> OpenAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        if (Resolve(root, relativePath, permissions?.Sensitive()) is not { } path)
        {
            return EditorOpenResult.NotFound;
        }

        var configured = EditorCommandLine.Split(config.Current.EditorCommand);
        var command = configured.Count > 0 ? [.. configured, path] : await SystemCommandAsync(path, _time, cancellationToken);
        if (command is null)
        {
            return EditorOpenResult.NoEditor;
        }

        var startInfo = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path),
        };
        foreach (var argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            Process.StartAndForget(startInfo);
            return EditorOpenResult.Opened;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            LogStartFailed(logger, ex, configured.Count > 0);
            return configured.Count > 0 ? EditorOpenResult.Failed : EditorOpenResult.NoEditor;
        }
    }

    /// <summary>
    /// Get the full path of a file when it is inside the folder, follows no link outside it, is not protected, and exists.
    /// </summary>
    /// <param name="root">The folder.</param>
    /// <param name="relativePath">The file's path relative to <paramref name="root"/>.</param>
    /// <param name="sensitive">Protected folders that must not be opened, or null.</param>
    /// <returns>The full path, or null.</returns>
    internal static string? Resolve(string root, string relativePath, SensitivePaths? sensitive = null)
    {
        return FolderPaths.ResolveFileInside(root, relativePath, out var real) is { } path && sensitive?.IsProtected(real) != true
            ? path
            : null;
    }

    /// <summary>
    /// Find the command of the system's text editor.
    /// </summary>
    private static async Task<IReadOnlyList<string>?> SystemCommandAsync(string path, TimeProvider time, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return ["notepad.exe", path];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["open", "-t", path];
        }

        if (await DefaultTextAppAsync(time, cancellationToken) is not { } id)
        {
            return null;
        }

        foreach (var directory in ApplicationDirectories())
        {
            var file = Path.Combine(directory, id);
            if (File.Exists(file))
            {
                return DesktopEntry.CommandFor(await File.ReadAllTextAsync(file, cancellationToken), path);
            }
        }

        return null;
    }

    /// <summary>
    /// Ask <c>xdg-mime</c> for the desktop entry of the app that opens plain text.
    /// </summary>
    /// <returns>The desktop entry's file name, such as <c>org.kde.kate.desktop</c>, or null.</returns>
    private static async Task<string?> DefaultTextAppAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        if (ShellInfo.FindOnPath("xdg-mime", Environment.GetEnvironmentVariable("PATH")) is not { } xdgMime)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(xdgMime)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("query");
        startInfo.ArgumentList.Add("default");
        startInfo.ArgumentList.Add("text/plain");
        using var clockTimeout = new CancellationTokenSource(QueryTimeout, time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clockTimeout.Token);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            try
            {
                var (output, _) = await process.ReadAllTextAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var id = output.Trim();
                return process.ExitCode == 0 && id.EndsWith(".desktop", StringComparison.Ordinal) && Path.GetFileName(id) == id ? id : null;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>
    /// List the folders desktop entries live in, most specific first, as the XDG Base Directory Specification orders them.
    /// </summary>
    private static IEnumerable<string> ApplicationDirectories()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrEmpty(dataDirs))
        {
            dataDirs = "/usr/local/share:/usr/share";
        }

        return dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Prepend(dataHome)
            .Select(directory => Path.Combine(directory, "applications"));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start the text editor (configured: {Configured})")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, bool configured);
}
