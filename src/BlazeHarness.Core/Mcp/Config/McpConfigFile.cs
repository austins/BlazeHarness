using BlazeHarness.Core.Config;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Mcp.Config;

/// <summary>
/// The global <c>mcp.json</c> as text, so edits keep the file as the user wrote it.
/// </summary>
/// <remarks>
/// The file is read as text instead of a typed model, because it is shared with other tools
/// and the user edits it directly. Saves are atomic, and edits made outside the app are picked up.
/// </remarks>
public sealed class McpConfigFile : IDisposable
{
    private readonly string _path;
    private readonly WatchedFile _file;
    private Snapshot _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpConfigFile"/> class.
    /// </summary>
    /// <param name="path">The path of the file; it does not need to exist.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">Times the delay that merges bursts of file events.</param>
    /// <param name="watch">Whether to watch the file for edits made outside the app.</param>
    public McpConfigFile(string path, ILogger logger, TimeProvider time, bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _path = path;
        _file = new WatchedFile(path, logger, time, watch, watchDelete: true, ReloadUnderLock);
        _current = Snapshot.Of(_file.ReadInitialText() ?? string.Empty);
    }

    /// <summary>
    /// Raised, on any thread, after the file changed outside the app or was saved.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the path of the file.
    /// </summary>
    public string FilePath => _path;

    /// <summary>
    /// Gets the current text of the file, empty when it does not exist.
    /// </summary>
    public string Text => Volatile.Read(ref _current).Text;

    /// <summary>
    /// Gets the result of parsing <see cref="Text"/>.
    /// </summary>
    public McpParseResult Parsed => Volatile.Read(ref _current).Result;

    /// <summary>
    /// Gets the servers to run: those of the newest text that had no errors.
    /// </summary>
    /// <remarks>
    /// A hand edit that breaks the file shows its errors through <see cref="Parsed"/> but leaves the running servers and their trust alone until it is fixed.
    /// When the file already has errors at startup, the servers that could be read are used.
    /// </remarks>
    public IReadOnlyList<McpServerConfig> Servers => Volatile.Read(ref _current).Servers;

    /// <summary>
    /// Check text as <see cref="SaveAsync"/> would, without saving it.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>The result of parsing the text.</returns>
    public static McpParseResult Check(string text) => McpConfigParser.Parse(text);

    /// <summary>
    /// Save new text, unless it has errors.
    /// </summary>
    /// <param name="text">The new text of the file.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The result of parsing the text; the file was saved only when it has no errors.</returns>
    public async Task<McpParseResult> SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var snapshot = Snapshot.Of(text);
        if (snapshot.Result.Errors.Count > 0)
        {
            return snapshot.Result;
        }

        await _file.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAllOwnerOnlyTextAsync(_path, text, cancellationToken);
            Volatile.Write(ref _current, snapshot);
        }
        finally
        {
            _file.Release();
        }

        _file.SafeInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
        return snapshot.Result;
    }

    /// <summary>
    /// Turn a server on or off for every chat by changing its <c>disabled</c> field.
    /// </summary>
    /// <param name="server">The server name.</param>
    /// <param name="disabled">True to turn the server off.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>True when the file was changed or already had that state; false when the server or the file could not be edited.</returns>
    public Task<bool> SetDisabledAsync(string server, bool disabled, CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            (text, _) => McpConfigEditor.SetDisabled(text, server, disabled) is { } edited ? (edited, true) : (null, false),
            onParseError: _ => false,
            cancellationToken);
    }

    /// <summary>
    /// Keep one tool of a server from the model, or offer it again, by changing the server's <c>excludeTools</c> field.
    /// </summary>
    /// <param name="server">The server name.</param>
    /// <param name="tool">The name of the tool as the server calls it.</param>
    /// <param name="excluded">True to keep the tool from the model.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>True when the file was changed or already had that state; false when the server or the file could not be edited.</returns>
    public Task<bool> SetToolExcludedAsync(string server, string tool, bool excluded, CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            (text, _) => McpConfigEditor.SetToolExcluded(text, server, tool, excluded) is { } edited ? (edited, true) : (null, false),
            onParseError: _ => false,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    /// <summary>
    /// Read the file again after an edit made outside the app, and raise <see cref="Changed"/> when its text differs.
    /// </summary>
    internal void OnExternalChange() => _file.OnExternalChange();

    /// <summary>
    /// Edit the current file text under the file lock and write the result atomically when valid.
    /// </summary>
    /// <typeparam name="TResult">The caller's result type.</typeparam>
    /// <param name="edit">Builds the edited text (or null to write nothing) and result from the current text and parsed servers.</param>
    /// <param name="onParseError">Maps parse errors in the edited text to a result without writing.</param>
    /// <param name="cancellationToken">A token to cancel waiting or writing.</param>
    /// <returns>The outcome of the edit.</returns>
    internal async Task<TResult> MutateAsync<TResult>(
        Func<string, McpParseResult, (string? EditedText, TResult Result)> edit,
        Func<IReadOnlyList<string>, TResult> onParseError,
        CancellationToken cancellationToken = default)
    {
        bool wrote = false;
        TResult outcome;
        await _file.WaitAsync(cancellationToken);
        try
        {
            var current = Volatile.Read(ref _current);
            var (edited, result) = edit(current.Text, current.Result);
            if (edited is null)
            {
                return result;
            }

            var snapshot = Snapshot.Of(edited);
            if (snapshot.Result.Errors.Count > 0)
            {
                return onParseError(snapshot.Result.Errors);
            }

            await AtomicFile.WriteAllOwnerOnlyTextAsync(_path, edited, cancellationToken);
            Volatile.Write(ref _current, snapshot);
            wrote = true;
            outcome = result;
        }
        finally
        {
            _file.Release();
        }

        if (wrote)
        {
            _file.SafeInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
        }

        return outcome;
    }

    private Action? ReloadUnderLock()
    {
        // Often the writer still holds the file. Keep the servers that were loaded instead of treating it as empty, and try again shortly.
        if (!_file.TryReadCurrentText(out var raw))
        {
            return null;
        }

        var text = raw ?? string.Empty;
        if (text == _current.Text)
        {
            return null;
        }

        Volatile.Write(ref _current, Snapshot.Of(text, _current));
        return () => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Snapshot(string Text, McpParseResult Result, IReadOnlyList<McpServerConfig> Servers)
    {
        public static Snapshot Of(string text, Snapshot? previous = null)
        {
            var result = McpConfigParser.Parse(text);
            return new(text, result, result.Errors.Count > 0 && previous is not null ? previous.Servers : result.Servers);
        }
    }
}
