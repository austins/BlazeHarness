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
public sealed class StreamingMarkdown(MarkdownRenderer renderer)
{
    private string _settledHtml = string.Empty;
    private int _settled;
    private int _scanned;
    private bool _inFence;

    /// <summary>
    /// Render the message text so far.
    /// </summary>
    /// <param name="text">The whole text received so far, which only ever grows.</param>
    /// <returns>Safe HTML.</returns>
    public string Render(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length < _scanned)
        {
            _settledHtml = string.Empty;
            _settled = 0;
            _scanned = 0;
            _inFence = false;
        }

        var boundary = AdvanceScan(text);
        if (boundary > _settled)
        {
            _settledHtml += renderer.ToHtml(text[_settled..boundary]);
            _settled = boundary;
        }

        return _settledHtml + renderer.ToHtml(text[_settled..]);
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
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                _inFence = !_inFence;
            }
            else if (line.IsEmpty && !_inFence)
            {
                boundary = _scanned;
            }
        }
    }
}
