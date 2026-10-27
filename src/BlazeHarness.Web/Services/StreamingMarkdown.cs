namespace BlazeHarness.Web.Services;

/// <summary>
/// Renders markdown that is still arriving without re-parsing everything already written.
/// </summary>
/// <remarks>
/// Text up to the last blank line outside a code fence is settled, so it is parsed once and kept. Only the text after
/// it is parsed again on each update. A construct that spans a blank line, such as a loose list, may look slightly
/// different until the message is finished and parsed whole.
/// </remarks>
/// <param name="renderer">The renderer that converts the pieces.</param>
/// <param name="sessionId">The chat the text belongs to, which images that point into its folder are served from, or null for none.</param>
public sealed class StreamingMarkdown(MarkdownRenderer renderer, string? sessionId = null)
{
    private const int MinFenceLength = 3;

    private int _settled;
    private int _scanned;
    private char _fenceMarker;
    private int _fenceLength;

    /// <summary>
    /// Gets the HTML of the settled text, which only changes when another block settles, so a view can leave it out of an update.
    /// </summary>
    public string Settled { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the HTML of the text after the settled part, which is parsed again on each update.
    /// </summary>
    public string Tail { get; private set; } = string.Empty;

    /// <summary>
    /// Render the message text so far into <see cref="Settled"/> and <see cref="Tail"/>.
    /// </summary>
    /// <param name="text">The whole text received so far, which only ever grows.</param>
    public void Render(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length < _scanned)
        {
            Settled = string.Empty;
            _settled = 0;
            _scanned = 0;
            _fenceMarker = '\0';
            _fenceLength = 0;
        }

        var boundary = AdvanceScan(text);
        if (boundary > _settled)
        {
            Settled += renderer.ToHtml(text[_settled..boundary], sessionId);
            _settled = boundary;
        }

        Tail = renderer.ToHtml(text[_settled..], sessionId);
    }

    private int AdvanceScan(string text)
    {
        var boundary = _settled;
        while (true)
        {
            var end = text.IndexOf('\n', _scanned);
            if (end < 0)
            {
                return boundary;
            }

            var line = text.AsSpan(_scanned, end - _scanned).Trim();
            _scanned = end + 1;
            if (TrackFence(line))
            {
                continue;
            }

            if (line.IsEmpty && _fenceMarker == '\0')
            {
                boundary = _scanned;
            }
        }
    }

    // A fence closes only with the same character, at least as long, and nothing after it.
    private bool TrackFence(ReadOnlySpan<char> line)
    {
        if (line.Length < MinFenceLength || (line[0] != '`' && line[0] != '~'))
        {
            return false;
        }

        var marker = line[0];
        var length = line.IndexOfAnyExcept(marker);
        if (length < 0)
        {
            length = line.Length;
        }

        if (length < MinFenceLength)
        {
            return false;
        }

        if (_fenceMarker == '\0')
        {
            _fenceMarker = marker;
            _fenceLength = length;
            return true;
        }

        if (marker == _fenceMarker && length >= _fenceLength && length == line.Length)
        {
            _fenceMarker = '\0';
            _fenceLength = 0;
            return true;
        }

        return false;
    }
}
