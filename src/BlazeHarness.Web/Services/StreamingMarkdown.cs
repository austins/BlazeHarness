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
/// <param name="chatId">The chat the text belongs to, which images that point into its folder are served from, or null for none.</param>
public sealed class StreamingMarkdown(MarkdownRenderer renderer, string? chatId = null)
{
    private const int MinFenceLength = 3;
    private const int MaxFenceIndent = 3;

    private int _settled;
    private int _scanned;
    private char _fenceMarker;
    private int _fenceLength;
    private bool _inList;

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
            _inList = false;
        }

        var boundary = AdvanceScan(text);
        if (boundary > _settled)
        {
            Settled += renderer.ToHtml(text[_settled..boundary], chatId);
            _settled = boundary;
        }

        Tail = renderer.ToHtml(text[_settled..], chatId);
    }

    private int AdvanceScan(string text)
    {
        var boundary = _settled;
        while (true)
        {
            var end = text.IndexOf('\n', _scanned, StringComparison.Ordinal);
            if (end < 0)
            {
                return boundary;
            }

            var rawLine = text.AsSpan(_scanned, end - _scanned).TrimEnd('\r');
            _scanned = end + 1;
            if (TrackFence(rawLine))
            {
                continue;
            }

            if (rawLine.Trim().IsEmpty)
            {
                if (_fenceMarker == '\0')
                {
                    boundary = _scanned;
                }
            }
            else if (_fenceMarker == '\0')
            {
                TrackList(rawLine);
            }
        }
    }

    /// <summary>
    /// Track whether the text is inside a list, where a fence can be indented by the item's content offset.
    /// </summary>
    /// <remarks>An unindented line that doesn't start an item ends the list.</remarks>
    /// <param name="rawLine">A non-blank line outside a code fence, without its line ending.</param>
    private void TrackList(ReadOnlySpan<char> rawLine)
    {
        var line = rawLine.TrimStart(' ');
        if (IsListItem(line))
        {
            _inList = true;
        }
        else if (line.Length == rawLine.Length)
        {
            _inList = false;
        }
    }

    private static bool IsListItem(ReadOnlySpan<char> line)
    {
        var marker = 0;
        if (line.Length > 0 && line[0] is '-' or '*' or '+')
        {
            marker = 1;
        }
        else
        {
            while (marker < line.Length && marker < 9 && char.IsAsciiDigit(line[marker]))
            {
                marker++;
            }

            if (marker == 0 || marker >= line.Length || line[marker] is not ('.' or ')'))
            {
                return false;
            }

            marker++;
        }

        return marker == line.Length || line[marker] is ' ' or '\t';
    }

    /// <summary>
    /// Track whether a line opens or closes a code fence.
    /// </summary>
    /// <remarks>
    /// Follows CommonMark §4.5: at most three leading spaces (any number inside a list, whose items indent their content), backtick info strings may not contain backticks,
    /// and a fence closes only with the same character, at least as long, and only whitespace after it.
    /// </remarks>
    /// <param name="rawLine">The line without its line ending.</param>
    /// <returns>True when the line opens or closes a fence.</returns>
    private bool TrackFence(ReadOnlySpan<char> rawLine)
    {
        var indent = 0;
        while (indent < rawLine.Length && rawLine[indent] == ' ')
        {
            indent++;
        }

        if (indent > MaxFenceIndent && !_inList)
        {
            return false;
        }

        var line = rawLine[indent..];
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

        var rest = line[length..];
        if (_fenceMarker == '\0')
        {
            if (marker == '`' && rest.Contains('`'))
            {
                return false;
            }

            _fenceMarker = marker;
            _fenceLength = length;
            return true;
        }

        if (marker == _fenceMarker && length >= _fenceLength && rest.TrimEnd([' ', '\t']).IsEmpty)
        {
            _fenceMarker = '\0';
            _fenceLength = 0;
            return true;
        }

        return false;
    }
}
