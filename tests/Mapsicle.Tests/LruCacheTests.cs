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

            // Exactly 10, not a range. The count used to be incremented by every thread whose
            // factory ran rather than by the one whose value was kept, so a racing miss counted the
            // same add several times and the count drifted permanently above the real size. A
            // bounded cache that believes it holds more than it does starts evicting below its own
            // capacity.
            Assert.Equal(10, cache.Count);

            // The factory runs once per key now, so the losing threads no longer pay to compile a
            // value that is thrown away. Ten keys, ten calls, however many threads raced.
            Assert.Equal(10, callCount);
        }

        [Fact]
        public void ConcurrentGetOrAdd_UnderHeavyContention_CountsEachKeyExactlyOnce()
        {
            // The stress form of the test above. A single Parallel.For over ten keys can finish
            // before any real race happens, which would let the drift through unnoticed; this holds
            // many threads on a barrier so they collide on the same key at the same moment.
            const int keys = 200;
            const int threads = 16;

            var cache = new LruCache<int, string>(capacity: 10_000);
            var factoryCalls = 0;
            using var start = new ManualResetEventSlim(false);

            var workers = new List<Task>();
            for (int t = 0; t < threads; t++)
            {
                workers.Add(Task.Run(() =>
                {
                    start.Wait();
                    for (int k = 0; k < keys; k++)
                    {
                        cache.GetOrAdd(k, key =>
                        {
                            Interlocked.Increment(ref factoryCalls);
                            return "value" + key;
                        });
                    }
                }));
            }

            start.Set();
            Task.WaitAll(workers.ToArray());

            Assert.Equal(keys, cache.Count);
            Assert.Equal(keys, factoryCalls);

            for (int k = 0; k < keys; k++)
            {
                Assert.True(cache.TryGetValue(k, out var value));
                Assert.Equal("value" + k, value);
            }
        }

        [Fact]
        public void AFaultingFactory_DoesNotPoisonTheKey()
        {
            // A Lazy that faulted caches its exception for good, so a transient failure would
            // otherwise make the key permanently unusable and leave the count counting it.
            var cache = new LruCache<string, string>(10);
            var attempts = 0;

            Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd("key", _ =>
            {
                attempts++;
                throw new InvalidOperationException("transient");
            }));

            Assert.Equal(0, cache.Count);

            var recovered = cache.GetOrAdd("key", _ =>
            {
                attempts++;
                return "second attempt";
            });

            Assert.Equal("second attempt", recovered);
            Assert.Equal(2, attempts);
            Assert.Equal(1, cache.Count);
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
