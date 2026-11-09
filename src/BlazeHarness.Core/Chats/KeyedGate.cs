namespace BlazeHarness.Core.Chats;

/// <summary>
/// One async lock per key, so work on one key never waits for work on another.
/// </summary>
/// <remarks>A key's lock exists only while someone holds or waits for it, so the set doesn't grow with every key ever used.</remarks>
internal sealed class KeyedGate
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Wait for the lock of a key.
    /// </summary>
    /// <param name="key">The key to lock.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>A lease that releases the lock when disposed.</returns>
    public async Task<Lease> EnterAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.References == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    /// A held lock of one key.
    /// </summary>
    internal sealed class Lease : IDisposable
    {
        private readonly KeyedGate _owner;
        private readonly string _key;
        private Entry? _entry;

        /// <summary>
        /// Initializes a new instance of the <see cref="Lease"/> class.
        /// </summary>
        /// <param name="owner">The gate the lock belongs to.</param>
        /// <param name="key">The locked key.</param>
        /// <param name="entry">The key's lock, already entered.</param>
        internal Lease(KeyedGate owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        /// <summary>
        /// Release the lock.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _entry, null) is { } entry)
            {
                entry.Semaphore.Release();
                _owner.Leave(_key, entry);
            }
        }
    }

    /// <summary>
    /// The lock of one key and its reference count.
    /// </summary>
    internal sealed class Entry
    {
        /// <summary>
        /// Gets the lock.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>
        /// Gets or sets how many leases hold or wait for the lock; changed only under the gate's lock. The lock is removed when this reaches zero.
        /// </summary>
        public int References { get; set; }
    }
}
