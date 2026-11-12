using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Config;

/// <summary>
/// The user's custom instructions, kept as text in <c>instructions.md</c> so they can be edited like any other Markdown file.
/// </summary>
/// <remarks>
/// Saves are atomic, and edits made outside the app are picked up. A file that cannot be read keeps the last good text.
/// </remarks>
public sealed class CustomInstructionsFile : IDisposable
{
    private readonly string _path;
    private readonly WatchedFile _file;
    private string _text;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomInstructionsFile"/> class and reads the file.
    /// </summary>
    /// <param name="path">The path of the file; it does not need to exist.</param>
    /// <param name="logger">The logger for read failures.</param>
    /// <param name="time">Times the delay that merges bursts of file events.</param>
    /// <param name="watch">Whether to watch the file for edits made outside the app.</param>
    public CustomInstructionsFile(string path, ILogger logger, TimeProvider time, bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _path = path;
        _file = new WatchedFile(path, logger, time, watch, watchDelete: true, ReloadUnderLock);
        _text = _file.ReadInitialText() ?? string.Empty;
    }

    /// <summary>
    /// Raised, on any thread, after the text changed because the file was edited outside the app.
    /// </summary>
    public event EventHandler? ExternalChange;

    /// <summary>
    /// Gets the current text, empty when the file does not exist.
    /// </summary>
    public string Text => Volatile.Read(ref _text);

    /// <summary>
    /// Save new text.
    /// </summary>
    /// <param name="text">The new text of the file.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the text is on disk and visible through <see cref="Text"/>.</returns>
    public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _file.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAllOwnerOnlyTextAsync(_path, text, cancellationToken);
            Volatile.Write(ref _text, text);
        }
        finally
        {
            _file.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    /// <summary>
    /// Read the file again after an edit made outside the app, and raise <see cref="ExternalChange"/> when its text differs.
    /// </summary>
    internal void OnExternalChange() => _file.OnExternalChange();

    private Action? ReloadUnderLock()
    {
        // Often the writer still holds the file. Keep the text that was loaded instead of treating it as empty, and try again shortly.
        if (!_file.TryReadCurrentText(out var raw))
        {
            return null;
        }

        var text = raw ?? string.Empty;
        if (text == _text)
        {
            return null;
        }

        Volatile.Write(ref _text, text);
        return () => ExternalChange?.Invoke(this, EventArgs.Empty);
    }
}
