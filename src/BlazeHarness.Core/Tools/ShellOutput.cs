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

    private readonly Lock _gate = new();
    private readonly StringBuilder _head = new();
    private readonly StringBuilder _tail = new();
    private long _omitted;

    public void AppendLine(string line)
    {
        lock (_gate)
        {
            var text = line + "\n";
            if (_head.Length < HeadLimit)
            {
                var room = HeadLimit - _head.Length;
                if (text.Length <= room)
                {
                    _head.Append(text);
                    return;
                }

                _head.Append(text, 0, room);
                text = text[room..];
            }

            _tail.Append(text);
            if (_tail.Length > TailLimit)
            {
                var excess = _tail.Length - TailLimit;
                _tail.Remove(0, excess);
                _omitted += excess;
            }
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            return _omitted == 0
                ? _head.ToString() + _tail
                : string.Create(CultureInfo.InvariantCulture, $"{_head}\n[{_omitted} characters left out]\n{_tail}");
        }
    }
}
