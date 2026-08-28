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
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, TValue> _cache;
        private readonly ConcurrentQueue<TKey> _accessOrder = new();
        // Presence of a key marks it as "recently used" for the second-chance eviction scan
        private readonly ConcurrentDictionary<TKey, byte> _recentlyUsed = new();
        private int _approximateCount;
        private readonly object _evictionLock = new();

        public LruCache(int capacity = 1000)
        {
            _capacity = capacity > 0 ? capacity : 1000;
            _cache = new ConcurrentDictionary<TKey, TValue>(Environment.ProcessorCount, _capacity);
        }

        public int Count => _approximateCount;

        /// <summary>
        /// Lock-free read with fallback to factory.
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            // OPTIMIZATION: Lock-free read path (hot path)
            if (_cache.TryGetValue(key, out var existing))
            {
                MarkRecentlyUsed(key);
                return existing;
            }

            // Cache miss - create value
            TValue valueCheck = default!;
            bool added = false;
            var value = _cache.GetOrAdd(key, k =>
            {
                added = true;
                valueCheck = factory(k);
                return valueCheck;
            });

            if (added && EqualityComparer<TValue>.Default.Equals(value, valueCheck))
            {
                Interlocked.Increment(ref _approximateCount);
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
            if (_cache.TryGetValue(key, out value!))
            {
                MarkRecentlyUsed(key);
                return true;
            }
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
