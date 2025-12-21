using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Mapsicle
{
    /// <summary>
    /// Thread-safe LRU cache with bounded capacity and lock-free reads.
    /// Uses ConcurrentDictionary for lock-free reads, with lazy LRU tracking.
    /// </summary>
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, TValue> _cache;
        private readonly ConcurrentQueue<TKey> _accessOrder = new();
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
                // Skip LRU update for reads - lazy tracking
                return existing;
            }

            // Cache miss - create value
            var value = _cache.GetOrAdd(key, k =>
            {
                Interlocked.Increment(ref _approximateCount);
                return factory(k);
            });

            // Track access for LRU (non-blocking)
            _accessOrder.Enqueue(key);

            // Lazy eviction - only when significantly over capacity
            if (_approximateCount > _capacity + (_capacity / 4))
            {
                TryEvict();
            }

            return value;
        }

        /// <summary>
        /// Lock-free read.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            // Completely lock-free
            return _cache.TryGetValue(key, out value!);
        }

        private void TryEvict()
        {
            // Only one thread should evict at a time
            if (!Monitor.TryEnter(_evictionLock))
                return;

            try
            {
                // Evict oldest entries
                while (_approximateCount > _capacity && _accessOrder.TryDequeue(out var oldKey))
                {
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
                while (_accessOrder.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _approximateCount, 0);
            }
        }
    }
}
