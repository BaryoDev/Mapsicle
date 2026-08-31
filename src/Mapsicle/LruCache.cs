using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Mapsicle
{
    /// <summary>
    /// Thread-safe bounded cache with lock-free reads and approximate LRU eviction.
    /// Reads mark entries as recently used; eviction uses a second-chance (CLOCK) scan,
    /// so frequently-read entries survive eviction instead of being removed in pure
    /// insertion (FIFO) order.
    /// </summary>
    /// <remarks>
    /// Values are held in a <see cref="Lazy{T}"/> for two reasons, both of which were defects.
    ///
    /// <see cref="ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/> may run
    /// the factory on several threads at once and keep only one result. The count was incremented by
    /// every thread whose factory ran, not by the one whose value was kept, so a racing miss counted
    /// an add two or three times. The count then drifted permanently above the real size and
    /// eviction started trimming the cache below its capacity, which is the opposite of what a
    /// bounded cache is for. Wrapping the value means the wrapper's reference identity says which
    /// thread won, exactly once, with no dependence on whether two values compare equal. Comparing
    /// the values themselves is not a fix: two threads whose factories both return 5 are
    /// indistinguishable that way.
    ///
    /// It also stops the losing threads' factories running at all. Here the factory compiles an
    /// expression tree, so a racing miss used to pay for the same compilation several times over and
    /// throw all but one away.
    /// </remarks>
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _cache;
        private readonly ConcurrentQueue<TKey> _accessOrder = new();
        // Presence of a key marks it as "recently used" for the second-chance eviction scan
        private readonly ConcurrentDictionary<TKey, byte> _recentlyUsed = new();
        private int _approximateCount;
        private readonly object _evictionLock = new();

        public LruCache(int capacity = 1000)
        {
            _capacity = capacity > 0 ? capacity : 1000;
            _cache = new ConcurrentDictionary<TKey, Lazy<TValue>>(Environment.ProcessorCount, _capacity);
        }

        public int Count => _approximateCount;

        /// <summary>
        /// Lock-free read with fallback to factory.
        /// </summary>
        /// <summary>Stores a value, replacing any entry already under this key.</summary>
        /// <remarks>
        /// <c>GetOrAdd</c> keeps the entry that got there first, which is right for a cache of
        /// compiled delegates and wrong for a registration that must supersede one. Under
        /// <c>UseLruCache</c>, a pair mapped before <c>RegisterGenerated</c> kept its compiled
        /// delegate and the generated mapper never applied, for the rest of the process.
        /// </remarks>
        public void Set(TKey key, TValue value)
        {
            var entry = new Lazy<TValue>(() => value, LazyThreadSafetyMode.ExecutionAndPublication);

            if (_cache.TryGetValue(key, out _))
            {
                _cache[key] = entry;
                MarkRecentlyUsed(key);
                return;
            }

            _cache[key] = entry;
            Interlocked.Increment(ref _approximateCount);
            MarkRecentlyUsed(key);
            TryEvict();
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            // OPTIMIZATION: Lock-free read path (hot path)
            if (_cache.TryGetValue(key, out var existing))
            {
                MarkRecentlyUsed(key);
                return existing.Value;
            }

            var mine = new Lazy<TValue>(() => factory(key), LazyThreadSafetyMode.ExecutionAndPublication);
            var stored = _cache.GetOrAdd(key, mine);

            if (ReferenceEquals(stored, mine))
            {
                Interlocked.Increment(ref _approximateCount);
            }

            TValue value;
            try
            {
                value = stored.Value;
            }
            catch
            {
                // A Lazy that faulted caches the exception for good, so leaving it in place would
                // turn one transient failure into a permanently poisoned key.
                //
                // Removed by key AND value together. Removing by key alone would delete whatever
                // holder is current, which after a Clear and a racing re-add is a healthy holder
                // belonging to someone else, and would leave the count describing a cache that no
                // longer matches it. ICollection's Remove is the pair-conditional form and is
                // available on every target framework, unlike the TryRemove(KeyValuePair) overload.
                if (((ICollection<KeyValuePair<TKey, Lazy<TValue>>>)_cache)
                        .Remove(new KeyValuePair<TKey, Lazy<TValue>>(key, stored)))
                {
                    Interlocked.Decrement(ref _approximateCount);
                }
                throw;
            }

            // Track access for eviction ordering (non-blocking)
            _accessOrder.Enqueue(key);

            // Lazy eviction - only when significantly over capacity
            TryEvict();

            return value;
        }

        /// <summary>
        /// Lock-free read.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var holder))
            {
                MarkRecentlyUsed(key);
                value = holder.Value;
                return true;
            }
            value = default!;
            return false;
        }

        private void MarkRecentlyUsed(TKey key)
        {
            // TryAdd only writes when the mark is absent, keeping repeat reads cheap
            _recentlyUsed.TryAdd(key, 0);
        }

        private void TryEvict()
        {
            // Only evict when significantly over capacity (25% overage threshold)
            if (_approximateCount <= _capacity + (_capacity / 4))
                return;

            // Only one thread should evict at a time
            if (!Monitor.TryEnter(_evictionLock))
                return;

            try
            {
                // Second-chance scan: recently-used entries get re-enqueued once instead of
                // evicted, so hot entries survive. Bound the scan so concurrent readers
                // re-marking entries cannot keep this loop alive indefinitely.
                int remainingScans = (_approximateCount * 2) + 8;
                while (_approximateCount > _capacity && remainingScans-- > 0 && _accessOrder.TryDequeue(out var oldKey))
                {
                    if (_recentlyUsed.TryRemove(oldKey, out _) && _cache.ContainsKey(oldKey))
                    {
                        // Recently read - give it a second chance at the back of the queue
                        _accessOrder.Enqueue(oldKey);
                        continue;
                    }

                    if (_cache.TryRemove(oldKey, out _))
                    {
                        Interlocked.Decrement(ref _approximateCount);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_evictionLock);
            }
        }

        public void Clear()
        {
            lock (_evictionLock)
            {
                _cache.Clear();
                _recentlyUsed.Clear();
                while (_accessOrder.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _approximateCount, 0);
            }
        }
    }
}
