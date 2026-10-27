using BlazeHarness.Core.Config;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Folders;

/// <summary>
/// The result of opening a folder.
/// </summary>
/// <param name="Folder">The folder, or null when it could not be opened.</param>
/// <param name="Error">Why it could not be opened, or <see cref="FolderError.None"/>.</param>
public sealed record OpenFolderResult(FolderInfo? Folder, FolderError Error);

/// <summary>
/// The folders the user opened, saved in <c>folders.json</c>.
/// </summary>
/// <remarks>
/// A folder is kept under its canonical path, so one folder reached by two routes (a link, a trailing slash,
/// different letter case on Windows and macOS) is one entry. A folder that is later moved or deleted stays listed
/// and reports itself as unavailable, so its sessions are never lost. Members are safe to call from any thread.
/// </remarks>
public sealed class FolderRegistry : IDisposable
{
    private readonly AppPaths _paths;
    private readonly TimeProvider _time;
    private readonly JsonFileStore<FoldersDocument> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderRegistry"/> class and loads <c>folders.json</c>.
    /// </summary>
    /// <param name="paths">Resolves the file and the user's home folder.</param>
    /// <param name="loggers">Creates the logger for read failures.</param>
    /// <param name="time">The clock for timestamps; defaults to the system clock.</param>
    public FolderRegistry(AppPaths paths, ILoggerFactory loggers, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggers);
        _paths = paths;
        _time = time ?? TimeProvider.System;
        _store = new JsonFileStore<FoldersDocument>(paths.FoldersFile, loggers.CreateLogger<JsonFileStore<FoldersDocument>>(), time: _time);
        _store.ExternalChange += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised after a folder is opened, removed or changed, including by an edit to the file.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Get the folders, most recently opened first.
    /// </summary>
    /// <returns>The folders.</returns>
    public IReadOnlyList<FolderInfo> List()
    {
        return [.. _store.Current.Folders.OrderByDescending(f => f.LastOpenedAt).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Find a folder by path.
    /// </summary>
    /// <param name="path">A path to the folder, in any form that names it.</param>
    /// <returns>The folder, or null when it is not in the list.</returns>
    public FolderInfo? Find(string? path)
    {
        var key = FolderPaths.Normalize(path, _paths.UserHome);
        return key is null ? null : _store.Current.Folders.FirstOrDefault(f => FolderPaths.Comparer.Equals(f.Path, key));
    }

    /// <summary>
    /// Check whether a folder still exists on disk.
    /// </summary>
    /// <param name="folder">The folder to check.</param>
    /// <returns>True when its path is still a folder.</returns>
    public static bool IsAvailable(FolderInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return Directory.Exists(folder.Path);
    }

    /// <summary>
    /// Check that a path can be opened as a folder.
    /// </summary>
    /// <param name="path">The path as typed or picked.</param>
    /// <returns>The problem with it, or <see cref="FolderError.None"/>.</returns>
    public FolderError Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FolderError.Empty;
        }

        var full = FolderPaths.ToFullPath(path, _paths.UserHome);
        if (full is null)
        {
            return FolderError.NotAbsolute;
        }

        var error = CheckFullPath(full);
        return error == FolderError.None && IsTooBroad(full) ? FolderError.NotAllowed : error;
    }

    /// <summary>
    /// Open a folder: add it to the list, or mark an already listed folder as just opened.
    /// </summary>
    /// <param name="path">The path as typed or picked. A leading <c>~</c> means the home folder.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The folder, or the reason it could not be opened.</returns>
    public async Task<OpenFolderResult> OpenAsync(string? path, CancellationToken cancellationToken = default)
    {
        var error = Check(path);
        if (error != FolderError.None)
        {
            return new OpenFolderResult(null, error);
        }

        var typed = FolderPaths.ToFullPath(path, _paths.UserHome)!;
        var key = FolderPaths.Normalize(path, _paths.UserHome)!;
        FolderInfo? opened = null;
        await _store.UpdateAsync(
            doc =>
            {
                var now = _time.GetUtcNow();
                var existing = doc.Folders.FirstOrDefault(f => FolderPaths.Comparer.Equals(f.Path, key));
                opened = existing is null
                    ? new FolderInfo { Path = key, Name = FolderPaths.DisplayName(typed), LastOpenedAt = now }
                    : existing with { LastOpenedAt = now };
                return doc with { Folders = [.. doc.Folders.Where(f => f != existing), opened] };
            },
            cancellationToken).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
        return new OpenFolderResult(opened, FolderError.None);
    }

    /// <summary>
    /// Record that a folder was just used, which moves it to the top of the recent list. Does nothing for an unlisted folder.
    /// </summary>
    /// <param name="path">The path of the folder.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the change is saved.</returns>
    public async Task TouchAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Find(path) is not { } folder)
        {
            return;
        }

        await _store.UpdateAsync(
            doc => doc with
            {
                Folders = [.. doc.Folders.Select(f => FolderPaths.Comparer.Equals(f.Path, folder.Path) ? f with { LastOpenedAt = _time.GetUtcNow() } : f)],
            },
            cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Remove a folder from the list. The folder on disk is never touched.
    /// </summary>
    /// <param name="path">The path of the folder.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>True when the folder was listed and is now removed.</returns>
    public async Task<bool> RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        var removed = false;
        var key = FolderPaths.Normalize(path, _paths.UserHome);
        if (key is null)
        {
            return false;
        }

        await _store.UpdateAsync(
            doc =>
            {
                var kept = doc.Folders.Where(f => !FolderPaths.Comparer.Equals(f.Path, key)).ToList();
                removed = kept.Count != doc.Folders.Count;
                return removed ? doc with { Folders = kept } : doc;
            },
            cancellationToken).ConfigureAwait(false);

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>
    /// Replace a listed folder with the place it moved to. If the new folder is already listed, the two entries become one.
    /// </summary>
    /// <param name="oldPath">The path of the listed folder, which may no longer exist.</param>
    /// <param name="newPath">The path as typed or picked. A leading <c>~</c> means the home folder.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The folder now listed in its place, or the reason the new path could not be used.</returns>
    public async Task<OpenFolderResult> RelocateAsync(string oldPath, string? newPath, CancellationToken cancellationToken = default)
    {
        var old = Find(oldPath);
        if (old is null)
        {
            return await OpenAsync(newPath, cancellationToken).ConfigureAwait(false);
        }

        var error = Check(newPath);
        if (error != FolderError.None)
        {
            return new OpenFolderResult(null, error);
        }

        var typed = FolderPaths.ToFullPath(newPath, _paths.UserHome)!;
        var key = FolderPaths.Normalize(newPath, _paths.UserHome)!;
        FolderInfo? moved = null;
        await _store.UpdateAsync(
            doc =>
            {
                moved = new FolderInfo { Path = key, Name = FolderPaths.DisplayName(typed), LastOpenedAt = _time.GetUtcNow() };
                var others = doc.Folders.Where(f => !FolderPaths.Comparer.Equals(f.Path, old.Path) && !FolderPaths.Comparer.Equals(f.Path, key));
                return doc with { Folders = [.. others, moved] };
            },
            cancellationToken).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
        return new OpenFolderResult(moved, FolderError.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _store.Dispose();
    }

    // Working in the root or home would let every tool reach everything the user owns, and the app's own folder holds provider keys.
    private bool IsTooBroad(string full)
    {
        var real = WorkspacePath.CanonicalRoot(full);
        return FolderPaths.Comparer.Equals(real, Path.GetPathRoot(real))
            || FolderPaths.Comparer.Equals(real, WorkspacePath.CanonicalRoot(_paths.UserHome))
            || WorkspacePath.IsInside(WorkspacePath.CanonicalRoot(_paths.Home), real);
    }

    private static FolderError CheckFullPath(string full)
    {
        if (!Directory.Exists(full))
        {
            return File.Exists(full) ? FolderError.NotAFolder : FolderError.NotFound;
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(full).FirstOrDefault();
            return FolderError.None;
        }
        catch (UnauthorizedAccessException)
        {
            return FolderError.AccessDenied;
        }
        catch (IOException)
        {
            return FolderError.NotFound;
        }
    }
}
