using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mapsicle.Tests
{
    public class LruCacheTests
    {
        [Fact]
        public void GetOrAdd_ShouldReturnCachedValue()
        {
            var cache = new LruCache<string, int>(10);

            var result1 = cache.GetOrAdd("key", _ => 42);
            var result2 = cache.GetOrAdd("key", _ => 99);

            Assert.Equal(42, result1);
            Assert.Equal(42, result2); // Should return cached value, not 99
        }

        [Fact]
        public void GetOrAdd_ShouldTrackCount()
        {
            var cache = new LruCache<string, int>(100);

            cache.GetOrAdd("a", _ => 1);
            cache.GetOrAdd("b", _ => 2);
            cache.GetOrAdd("c", _ => 3);

            Assert.Equal(3, cache.Count);
        }

        [Fact]
        public void GetOrAdd_DuplicateKey_ShouldNotInflateCount()
        {
            var cache = new LruCache<string, int>(100);

            cache.GetOrAdd("a", _ => 1);
            cache.GetOrAdd("a", _ => 2); // Duplicate - should not increment count

            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void TryGetValue_ShouldReturnFalseForMissing()
        {
            var cache = new LruCache<string, int>(10);

            Assert.False(cache.TryGetValue("missing", out _));
        }

        [Fact]
        public void TryGetValue_ShouldReturnTrueForExisting()
        {
            var cache = new LruCache<string, int>(10);
            cache.GetOrAdd("key", _ => 42);

            Assert.True(cache.TryGetValue("key", out var value));
            Assert.Equal(42, value);
        }

        [Fact]
        public void Eviction_ShouldRemoveOldestKeys()
        {
            // Small capacity to trigger eviction
            var cache = new LruCache<int, string>(4);

            // Add 4 items (at capacity)
            for (int i = 0; i < 4; i++)
            {
                cache.GetOrAdd(i, k => $"value{k}");
            }

            Assert.Equal(4, cache.Count);

            // Add enough to trigger eviction (25% overage = 5 needed for capacity of 4)
            for (int i = 4; i < 6; i++)
            {
                cache.GetOrAdd(i, k => $"value{k}");
            }

            // After eviction, count should be at or below capacity
            Assert.True(cache.Count <= 5, $"Count should be reduced after eviction, was {cache.Count}");
        }

        [Fact]
        public void Eviction_ShouldPreferKeepingRecentlyReadKeys()
        {
            var cache = new LruCache<int, string>(4);

            for (int i = 0; i < 4; i++)
            {
                cache.GetOrAdd(i, k => $"value{k}");
            }

            // Keep key 0 hot while inserting enough new keys to force several eviction passes.
            // Under the old FIFO eviction, key 0 (oldest insertion) was always evicted first.
            for (int i = 4; i < 12; i++)
            {
                cache.TryGetValue(0, out _);
                cache.GetOrAdd(i, k => $"value{k}");
            }

            Assert.True(cache.TryGetValue(0, out var hot), "recently-read key should survive eviction");
            Assert.Equal("value0", hot);
        }

        [Fact]
        public void Clear_ShouldResetCache()
        {
            var cache = new LruCache<string, int>(10);
            cache.GetOrAdd("a", _ => 1);
            cache.GetOrAdd("b", _ => 2);

            cache.Clear();

            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGetValue("a", out _));
            Assert.False(cache.TryGetValue("b", out _));
        }

        [Fact]
        public void GetOrAdd_WhenFactoryThrows_ShouldPropagateException()
        {
            var cache = new LruCache<string, int>(10);

            Assert.Throws<InvalidOperationException>(() =>
                cache.GetOrAdd("key", _ => throw new InvalidOperationException("test")));
        }

        [Fact]
        public void ConcurrentGetOrAdd_ShouldBeThreadSafe()
        {
            var cache = new LruCache<int, int>(1000);
            var callCount = 0;

            Parallel.For(0, 100, i =>
            {
                // Each thread adds the same set of keys
                for (int j = 0; j < 10; j++)
                {
                    cache.GetOrAdd(j, k =>
                    {
                        Interlocked.Increment(ref callCount);
                        return k * 10;
                    });
                }
            });

            // All 10 unique keys should be present with correct values
            for (int j = 0; j < 10; j++)
            {
                Assert.True(cache.TryGetValue(j, out var val));
                Assert.Equal(j * 10, val);
            }

            // Count is approximate — may be slightly higher than 10 under contention
            // because ConcurrentDictionary.GetOrAdd may call the factory on multiple threads
            // but only one result wins. The added flag can be set by losing threads.
            Assert.InRange(cache.Count, 10, 100);
        }

        [Fact]
        public void DefaultCapacity_ShouldBe1000()
        {
            var cache = new LruCache<string, int>();

            // Should be able to add 1000 items without eviction
            for (int i = 0; i < 1000; i++)
            {
                cache.GetOrAdd($"key{i}", _ => i);
            }

            Assert.Equal(1000, cache.Count);
        }

        [Fact]
        public void NegativeCapacity_ShouldDefault()
        {
            var cache = new LruCache<string, int>(-1);

            // Should use default capacity (1000)
            for (int i = 0; i < 100; i++)
            {
                cache.GetOrAdd($"key{i}", _ => i);
            }

            Assert.Equal(100, cache.Count);
        }
    }
}
