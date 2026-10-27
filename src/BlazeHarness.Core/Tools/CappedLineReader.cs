using System.Text;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Reads lines from text without ever holding more than a set number of characters of one line, so a single huge line can't use up memory.
/// </summary>
/// <remarks>
/// Lines end at <c>\n</c>, <c>\r</c> or <c>\r\n</c>, like <see cref="TextReader.ReadLine"/>. The rest of a line past the cap is read and dropped.
/// </remarks>
internal sealed class CappedLineReader
{
    private const int BufferSize = 4096;

    private readonly TextReader _reader;
    private readonly int _maxCharacters;
    private readonly char[] _buffer = new char[BufferSize];
    private readonly StringBuilder _line = new();
    private int _position;
    private int _length;
    private bool _skipLineFeed;
    private bool _sawText;

    /// <summary>
    /// Initializes a new instance of the <see cref="CappedLineReader"/> class.
    /// </summary>
    /// <param name="reader">The text to read.</param>
    /// <param name="maxCharacters">The most characters kept of one line.</param>
    public CappedLineReader(TextReader reader, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        _reader = reader;
        _maxCharacters = maxCharacters;
    }

    /// <summary>
    /// Gets how many characters of the last line that was read did not fit under the cap.
    /// </summary>
    public long DroppedCharacters { get; private set; }

    /// <summary>
    /// Read the next line.
    /// </summary>
    /// <returns>The line without its end, cut at the cap, or null at the end of the text.</returns>
    public string? ReadLine()
    {
        Begin();
        while (true)
        {
            if (_position == _length && !Fill(_reader.Read(_buffer, 0, _buffer.Length)))
            {
                return Finish();
            }

            if (Take())
            {
                return Complete();
            }
        }
    }

    /// <summary>
    /// Read the next line without blocking the thread.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the read.</param>
    /// <returns>The line without its end, cut at the cap, or null at the end of the text.</returns>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        Begin();
        while (true)
        {
            if (_position == _length && !Fill(await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false)))
            {
                return Finish();
            }

            if (Take())
            {
                return Complete();
            }
        }
    }

    private void Begin()
    {
        _line.Clear();
        _sawText = false;
        DroppedCharacters = 0;
    }

    private bool Fill(int count)
    {
        _position = 0;
        _length = count;
        return count > 0;
    }

    private string? Finish() => _sawText ? Complete() : null;

    private string Complete() => _line.ToString();

    // Returns true when the buffer held the end of the line.
    private bool Take()
    {
        if (_skipLineFeed)
        {
            _skipLineFeed = false;
            if (_buffer[_position] == '\n')
            {
                _position++;
                if (_position == _length)
                {
                    return false;
                }
            }
        }

        var chunk = _buffer.AsSpan(_position, _length - _position);
        var end = chunk.IndexOfAny('\r', '\n');
        var text = end < 0 ? chunk : chunk[..end];
        if (end >= 0 || text.Length > 0)
        {
            _sawText = true;
        }

        var room = _maxCharacters - _line.Length;
        _line.Append(text[..Math.Min(room, text.Length)]);
        DroppedCharacters += Math.Max(0, text.Length - room);

        if (end < 0)
        {
            _position = _length;
            return false;
        }

        _skipLineFeed = chunk[end] == '\r';
        _position += end + 1;
        return true;
    }
}
