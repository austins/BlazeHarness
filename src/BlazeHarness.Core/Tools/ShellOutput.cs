using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Collects the lines a command prints, keeping the start and the end when there is too much.
/// </summary>
internal sealed class ShellOutput
{
    internal const int HeadLimit = 10_000;
    internal const int TailLimit = 19_000;

    /// <summary>
    /// The most characters kept of one line, so a command that prints a huge line without a break can't use up memory.
    /// </summary>
    internal const int MaxLineCharacters = TailLimit;

    private readonly Lock _gate = new();
    private readonly StringBuilder _head = new();

    // Whole lines, so dropping the oldest text never copies the rest; only the first line can be partly dropped.
    private readonly Queue<string> _tail = new();
    private int _tailLength;
    private int _firstLineOffset;
    private long _omitted;

    public void AppendLine(string line)
    {
        lock (_gate)
        {
            var room = HeadLimit - _head.Length;
            if (room > line.Length)
            {
                _head.Append(line).Append('\n');
                return;
            }

            var rest = line + "\n";
            if (room > 0)
            {
                _head.Append(rest, 0, room);
                rest = rest[room..];
            }

            _tail.Enqueue(rest);
            _tailLength += rest.Length;
            DropOldest();
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            var text = new StringBuilder(_head.Length + _tailLength + 64);
            text.Append(_head);
            if (_omitted > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n[{_omitted} characters left out]\n");
            }

            var offset = _firstLineOffset;
            foreach (var line in _tail)
            {
                text.Append(line, offset, line.Length - offset);
                offset = 0;
            }

            return text.ToString();
        }
    }

    private void DropOldest()
    {
        while (_tailLength > TailLimit)
        {
            var excess = _tailLength - TailLimit;
            var first = _tail.Peek();
            var available = first.Length - _firstLineOffset;
            if (available <= excess)
            {
                _tail.Dequeue();
                _firstLineOffset = 0;
                excess = available;
            }
            else
            {
                _firstLineOffset += excess;
            }

            _tailLength -= excess;
            _omitted += excess;
        }
    }
}
