using BlazeHarness.Core.Config;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// Finds the skills on disk, keeps them up to date, and remembers which ones the user turned off.
/// </summary>
/// <remarks>
/// The folders shared by all chats are scanned once and watched for edits. A chat folder's own
/// <c>.agents/skills</c> is scanned when it is asked for and kept for a few seconds, because there can be many chat folders.
/// </remarks>
public sealed partial class SkillCatalog : IDisposable
{
    private const int DebounceMilliseconds = 300;

    // Short enough that an edit shows up on the next turn, long enough that one turn's several lookups scan once.
    private const int ProjectCacheSeconds = 5;
    private const int MaxCachedProjects = 64;

    private readonly (string Path, SkillSource Source)[] _globalFolders;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly ILogger _logger;
    private readonly bool _watch;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (DateTimeOffset At, SkillScan Scan)> _projects = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly ITimer _debounce;
    private SkillScan[] _global = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillCatalog"/> class.
    /// </summary>
    /// <param name="paths">Where BlazeHarness keeps its files.</param>
    /// <param name="config">The app settings, which hold the list of turned-off skills.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock, used to expire the scans of chat folders.</param>
    /// <param name="watch">Whether to watch the skill folders for edits.</param>
    public SkillCatalog(AppPaths paths, JsonFileStore<AppConfig> config, ILogger<SkillCatalog> logger, TimeProvider time, bool watch = true)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _globalFolders =
        [
            (paths.SkillsDirectory, SkillSource.App),
            (paths.UserAgentsSkillsDirectory, SkillSource.UserAgents),
        ];
        _config = config;
        _logger = logger;
        _watch = watch;
        _time = time;
        _debounce = _time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Refresh();
    }

    /// <summary>
    /// Raised, on any thread, after the skills in a shared folder changed or the list of turned-off skills was saved.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Get whether a skill is on, once a chat's own choices are applied.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="disabledEverywhere">Whether the skill is turned off for all chats.</param>
    /// <param name="overrides">The chat's choices by skill name, or null when it has none.</param>
    /// <returns>True when the skill is on for the chat.</returns>
    public static bool IsEnabled(string name, bool disabledEverywhere, IReadOnlyDictionary<string, bool>? overrides)
    {
        return overrides is not null && overrides.TryGetValue(name, out var chosen) ? chosen : !disabledEverywhere;
    }

    /// <summary>
    /// Read every skill folder again.
    /// </summary>
    public void Refresh()
    {
        lock (_gate)
        {
            // A debounced refresh that was already queued must not rewatch folders after disposal.
            if (_disposed)
            {
                return;
            }

            _global = [.. _globalFolders.Select(folder => SkillScanner.Scan(folder.Path, folder.Source))];
            _projects.Clear();
            AttachWatchers();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get the skills that apply to a chat folder, before any are turned off.
    /// </summary>
    /// <param name="folderPath">The chat's folder, or null for a chat without one.</param>
    /// <returns>The skills to use, the ones passed over, and the problems found.</returns>
    public SkillResolution Resolve(string? folderPath)
    {
        SkillScan[] global;
        lock (_gate)
        {
            if (_watch && _globalFolders.Any(folder => Directory.Exists(folder.Path) && !_watchers.ContainsKey(folder.Path)))
            {
                _global = [.. _globalFolders.Select(folder => SkillScanner.Scan(folder.Path, folder.Source))];
                AttachWatchers();
            }

            global = _global;
        }

        var project = string.IsNullOrWhiteSpace(folderPath) ? SkillScan.Empty : ProjectScan(folderPath);
        return SkillResolution.Of([project, .. global]);
    }

    /// <summary>
    /// Get the skills a chat offers the model.
    /// </summary>
    /// <param name="folderPath">The chat's folder, or null for a chat without one.</param>
    /// <param name="overrides">The chat's choices by skill name, or null when it has none.</param>
    /// <returns>The skills that are on.</returns>
    public SkillSet For(string? folderPath, IReadOnlyDictionary<string, bool>? overrides)
    {
        var disabled = _config.Current.DisabledSkills;
        return new SkillSet(Resolve(folderPath).Skills.Where(skill => IsEnabled(skill.Name, disabled.Contains(skill.Name), overrides)));
    }

    /// <summary>
    /// Get whether a skill is turned off for every chat.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <returns>True when it is off.</returns>
    public bool IsDisabledGlobally(string name) => _config.Current.DisabledSkills.Contains(name);

    /// <summary>
    /// Turn a skill on or off for every chat.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="disabled">True to turn the skill off.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the choice is saved.</returns>
    public async Task SetDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _config.UpdateAsync(
            config =>
            {
                var others = config.DisabledSkills.Where(existing => !string.Equals(existing, name, StringComparison.Ordinal));
                return config with { DisabledSkills = disabled ? [.. others, name] : [.. others] };
            },
            cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        _debounce.Dispose();
    }

    private SkillScan ProjectScan(string folderPath)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_projects.TryGetValue(folderPath, out var cached) && now - cached.At < TimeSpan.FromSeconds(ProjectCacheSeconds))
            {
                return cached.Scan;
            }
        }

        var scan = SkillScanner.Scan(Path.Combine(folderPath, ".agents", "skills"), SkillSource.Project, folderPath);
        lock (_gate)
        {
            if (_projects.Count >= MaxCachedProjects)
            {
                _projects.Clear();
            }

            _projects[folderPath] = (now, scan);
        }

        return scan;
    }

    private void AttachWatchers()
    {
        if (!_watch)
        {
            return;
        }

        foreach (var (path, _) in _globalFolders)
        {
            if (_watchers.ContainsKey(path) || !Directory.Exists(path))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += (_, _) => Schedule();
                watcher.Created += (_, _) => Schedule();
                watcher.Deleted += (_, _) => Schedule();
                watcher.Renamed += (_, _) => Schedule();
                _watchers[path] = watcher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LogWatchFailed(ex, path);
            }
        }
    }

    private void Schedule()
    {
        try
        {
            _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A watcher event can still arrive just after disposal.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not watch the skills folder {Path}.")]
    private partial void LogWatchFailed(Exception ex, string path);
}
