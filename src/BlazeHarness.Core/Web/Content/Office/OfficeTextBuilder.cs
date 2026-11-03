using System.Text;

namespace BlazeHarness.Core.Web.Content.Office;

/// <summary>
/// Collects the Markdown blocks of one Office file and stops taking more once it is long enough.
/// </summary>
internal sealed class OfficeTextBuilder
{
    internal const int MaxCharacters = 2_000_000;
    private const int MaxHeadings = 80;

    private readonly StringBuilder _text = new();
    private readonly List<string> _headings = [];
    private bool _lastWasListItem;

    /// <summary>
    /// Gets a value indicating whether the text has reached its size limit, so the readers should stop.
    /// </summary>
    public bool Full => _text.Length >= MaxCharacters;

    /// <summary>
    /// Gets the headings added so far, each as a Markdown heading line.
    /// </summary>
    public IReadOnlyList<string> Headings => _headings;

    /// <summary>
    /// Gets a value indicating whether the text has anything besides whitespace and heading markers.
    /// </summary>
    public bool HasText => _text.ToString().Any(character => !char.IsWhiteSpace(character) && character != '#');

    /// <summary>
    /// Add a block, separated from the one before by a blank line. An empty block is ignored.
    /// </summary>
    /// <param name="block">The Markdown of the block.</param>
    public void AddBlock(string block)
    {
        if (block.Length == 0)
        {
            return;
        }

        if (_text.Length > 0)
        {
            _text.Append("\n\n");
        }

        _text.Append(block);
        _lastWasListItem = false;
    }

    /// <summary>
    /// Add a list item, which sits right under the item before it.
    /// </summary>
    /// <param name="item">The Markdown of the item, with its bullet.</param>
    public void AddListItem(string item)
    {
        if (_lastWasListItem)
        {
            _text.Append('\n').Append(item);
            return;
        }

        AddBlock(item);
        _lastWasListItem = true;
    }

    /// <summary>
    /// Add a heading and remember it for the outline.
    /// </summary>
    /// <param name="level">The heading level, from 1 to 6.</param>
    /// <param name="text">The heading text.</param>
    public void AddHeading(int level, string text)
    {
        var line = $"{new string('#', Math.Clamp(level, 1, 6))} {text}";
        AddBlock(line);
        if (_headings.Count < MaxHeadings)
        {
            _headings.Add(line);
        }
    }

    /// <inheritdoc />
    public override string ToString() => _text.ToString().TrimEnd();
}
