namespace BlazeHarness.Core.Agent;

/// <summary>
/// Keeps the last lines a running call printed.
/// </summary>
/// <remarks>
/// Lines may come from two threads at once (a command's output and error streams).
/// </remarks>
internal sealed class OutputTail
{
    /// <summary>
    /// The most lines kept.
    /// </summary>
    internal const int MaxLines = 12;

    /// <summary>
    /// The most characters kept of one line.
    /// </summary>
    internal const int MaxLineCharacters = 400;

    private readonly Lock _gate = new();
    private readonly Queue<string> _lines = new();

    /// <summary>
    /// Gets the kept lines, separated by <c>\n</c>.
    /// </summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return string.Join('\n', _lines);
            }
        }
    }

    /// <summary>
    /// Add a line, dropping the oldest one when there are too many.
    /// </summary>
    /// <remarks>
    /// Only the text after the last carriage return is kept, as a terminal shows a progress line that redraws itself.
    /// </remarks>
    /// <param name="line">The line.</param>
    public void Append(string line)
    {
        var trimmed = line.AsSpan().TrimEnd('\r');
        var text = trimmed[(trimmed.LastIndexOf('\r') + 1)..].ToString();
        if (text.Length > MaxLineCharacters)
        {
            var cut = char.IsHighSurrogate(text[MaxLineCharacters - 1]) ? MaxLineCharacters - 1 : MaxLineCharacters;
            text = string.Concat(text.AsSpan(0, cut), "…");
        }

        lock (_gate)
        {
            _lines.Enqueue(text);
            if (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
        }
    }
}
