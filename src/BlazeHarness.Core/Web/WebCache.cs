namespace BlazeHarness.Core.Web;

/// <summary>
/// Keeps the pages a chat fetched in memory, so reading the next piece of a long page doesn't download it again.
/// </summary>
/// <remarks>
/// Each chat has its own small, least-recently-used set that expires after a while. Nothing is written to disk, and a chat's pages are
/// dropped when it ends.
/// </remarks>
internal sealed class WebCache
{
    internal const int MaxPagesPerChat = 8;
    internal const int MaxCharactersPerChat = 2_000_000;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<Entry>> _chats = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCache"/> class.
    /// </summary>
    /// <param name="time">Tells when a page has expired.</param>
    public WebCache(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Get a page a chat fetched earlier.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="address">The address that was asked for.</param>
    /// <returns>The page, or null when it was not fetched or has expired.</returns>
    public CachedPage? Find(string chatId, Uri address)
    {
        var key = UrlCanonicalizer.Canonical(address);
        lock (_lock)
        {
            if (!_chats.TryGetValue(chatId, out var entries))
            {
                return null;
            }

            entries.RemoveAll(entry => _time.GetUtcNow() - entry.At > Lifetime);
            var index = entries.FindIndex(entry => entry.Key == key);
            if (index < 0)
            {
                return null;
            }

            var found = entries[index];
            entries.RemoveAt(index);
            entries.Add(found);
            return found.Page;
        }
    }

    /// <summary>
    /// Keep a page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="address">The address that was asked for.</param>
    /// <param name="page">The page.</param>
    public void Add(string chatId, Uri address, CachedPage page)
    {
        var key = UrlCanonicalizer.Canonical(address);
        lock (_lock)
        {
            if (!_chats.TryGetValue(chatId, out var entries))
            {
                entries = [];
                _chats[chatId] = entries;
            }

            // Pages of other chats that have expired are dropped too, so a chat that is no longer used doesn't hold memory.
            foreach (var other in _chats.Values)
            {
                other.RemoveAll(entry => _time.GetUtcNow() - entry.At > Lifetime);
            }

            entries.RemoveAll(entry => entry.Key == key);
            entries.Add(new Entry(key, page, _time.GetUtcNow()));
            var total = entries.Sum(entry => entry.Page.Document.Text.Length);
            while (entries.Count > MaxPagesPerChat || (entries.Count > 1 && total > MaxCharactersPerChat))
            {
                total -= entries[0].Page.Document.Text.Length;
                entries.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Drop everything a chat fetched.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    public void Forget(string chatId)
    {
        lock (_lock)
        {
            _chats.Remove(chatId);
        }
    }

    private sealed record Entry(string Key, CachedPage Page, DateTimeOffset At);
}
