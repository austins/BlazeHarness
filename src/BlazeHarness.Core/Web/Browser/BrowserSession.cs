using PuppeteerSharp;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// One chat's page in the browser, with the addresses it may navigate to and the problems it has run into.
/// </summary>
internal sealed class BrowserSession
{
    private const int MaxProblems = 20;
    private const int MaxProblemChars = 300;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];
    private DateTimeOffset _lastUsed;
    private bool _closed;

    public BrowserSession(IBrowserContext context, IPage page, DateTimeOffset now)
    {
        Context = context;
        Page = page;
        _lastUsed = now;
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    public DateTimeOffset LastUsed
    {
        get
        {
            lock (_gate)
            {
                return _lastUsed;
            }
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    public void MarkClosed()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }

    public void Touch(DateTimeOffset now)
    {
        lock (_gate)
        {
            _lastUsed = now;
        }
    }

    public void Approve(string origin)
    {
        lock (_gate)
        {
            _approved.Add(origin);
        }
    }

    public bool IsApproved(string origin)
    {
        lock (_gate)
        {
            return _approved.Contains(origin);
        }
    }

    public void AddProblem(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        if (line.Length > MaxProblemChars)
        {
            line = line[..MaxProblemChars] + "…";
        }

        lock (_gate)
        {
            if (_problems.Count < MaxProblems && !_problems.Contains(line))
            {
                _problems.Add(line);
            }
        }
    }

    public IReadOnlyList<string> TakeProblems()
    {
        lock (_gate)
        {
            var taken = _problems.ToList();
            _problems.Clear();
            return taken;
        }
    }
}
