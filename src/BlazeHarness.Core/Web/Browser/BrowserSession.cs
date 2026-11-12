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
    private readonly Dictionary<ITarget, ICDPSession> _workers = [];
    private DateTimeOffset _lastUsed;
    private bool _closed;
    private Uri? _titleUrl;
    private string _title = string.Empty;
    private DateTimeOffset _titleRead;

    public BrowserSession(IBrowserContext context, IPage page, ICDPSession client, ICDPSession browserClient, DateTimeOffset now)
    {
        Context = context;
        Page = page;
        Client = client;
        BrowserClient = browserClient;
        _lastUsed = now;
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    /// <summary>
    /// Gets the protocol session of the page, used for the screencast and for finding elements.
    /// </summary>
    public ICDPSession Client { get; }

    /// <summary>
    /// Gets the protocol session of the whole browser, which has to stay open for the page's download setting to hold.
    /// </summary>
    public ICDPSession BrowserClient { get; }

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

    /// <summary>
    /// Get the origins this chat has opened with a navigation.
    /// </summary>
    /// <returns>A copy of the approved origins.</returns>
    public IReadOnlyList<string> ApprovedOrigins()
    {
        lock (_gate)
        {
            return [.. _approved];
        }
    }

    /// <summary>
    /// Gets the title read for an address if it is still fresh enough to show again.
    /// </summary>
    /// <param name="url">The address of the page.</param>
    /// <param name="now">The current time.</param>
    /// <param name="maxAge">How long a title stays fresh.</param>
    /// <returns>The title, or null when it has to be read again.</returns>
    public string? FreshTitle(Uri url, DateTimeOffset now, TimeSpan maxAge)
    {
        lock (_gate)
        {
            return _titleUrl == url && now - _titleRead < maxAge ? _title : null;
        }
    }

    public void RememberTitle(Uri url, string title, DateTimeOffset now)
    {
        lock (_gate)
        {
            _titleUrl = url;
            _title = title;
            _titleRead = now;
        }
    }

    public void AddProblem(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        if (line.Length > MaxProblemChars)
        {
            line = TextClip.Start(line, MaxProblemChars) + "…";
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

    /// <summary>
    /// Remember the session that intercepts a worker's requests, so it can be detached when the worker or page ends.
    /// </summary>
    /// <param name="target">The worker.</param>
    /// <param name="client">The session that intercepts its requests.</param>
    /// <returns>False when the page has closed, so the caller detaches the session itself.</returns>
    public bool TrackWorker(ITarget target, ICDPSession client)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            _workers[target] = client;
            return true;
        }
    }

    /// <summary>
    /// Stop tracking an ended worker.
    /// </summary>
    /// <param name="target">The worker.</param>
    /// <returns>The session that intercepted its requests, or null when there was none.</returns>
    public ICDPSession? ForgetWorker(ITarget target)
    {
        lock (_gate)
        {
            return _workers.Remove(target, out var client) ? client : null;
        }
    }

    public IReadOnlyList<ICDPSession> TakeWorkerClients()
    {
        lock (_gate)
        {
            var taken = _workers.Values.ToList();
            _workers.Clear();
            return taken;
        }
    }
}
