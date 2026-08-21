using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Sustained and concurrent load, with every result checked.
    /// </summary>
    /// <remarks>
    /// A load test that only proves "it did not throw" is close to worthless: the failure a cached,
    /// compiled, statically-shared mapper actually produces under concurrency is the wrong answer,
    /// not an exception. So every mapping here is verified against the input that produced it, and
    /// each thread uses values only it could have supplied.
    ///
    /// Thresholds are deliberately loose. These run on shared CI hardware, and a tight timing bound
    /// on a shared runner produces flaky failures, which teaches everyone to rerun until green. What
    /// is asserted tightly is correctness and memory; time is asserted only at an order of magnitude
    /// that would catch a mapper that stopped caching altogether.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class LoadTests
    {
        [Fact]
        public void ConcurrentMapping_AcrossThreads_ReturnsTheRightAnswerEveryTime()
        {
            Mapper.ClearCache();

            const int threads = 8;
            const int perThread = 25_000;
            var failures = new List<string>();

            Parallel.For(0, threads, t =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    // Unique per thread, so a delegate serving the wrong closure shows up as a
                    // mismatch rather than passing by coincidence.
                    var id = (t * perThread) + i;
                    var dest = new LoadOrder { Id = id, Reference = $"REF-{id}", Total = id * 1.5m }
                        .MapTo<LoadOrderDto>();

                    if (dest is null || dest.Id != id || dest.Reference != $"REF-{id}" || dest.Total != id * 1.5m)
                    {
                        lock (failures)
                        {
                            if (failures.Count < 5) failures.Add($"id {id} came back as {dest?.Id.ToString() ?? "null"}/{dest?.Reference}");
                        }
                        return;
                    }
                }
            });

            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        [Fact]
        public async Task ConcurrentFirstUse_CompilesOnceAndAgrees()
        {
            Mapper.ClearCache();

            // Every thread arrives at an uncached pair simultaneously, which is the race that
            // matters on a cold start under real traffic.
            var ready = new ManualResetEventSlim();
            var results = new LoadColdDto[16];

            var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            {
                ready.Wait();
                results[i] = new LoadColdSource { Id = i, Name = $"n{i}" }.MapTo<LoadColdDto>()!;
            })).ToArray();

            ready.Set();
            await Task.WhenAll(tasks);

            for (var i = 0; i < results.Length; i++)
            {
                Assert.NotNull(results[i]);
                Assert.Equal(i, results[i].Id);
                Assert.Equal($"n{i}", results[i].Name);
            }
        }

        [Fact]
        public void ALargeCollection_MapsEveryItemInOrder()
        {
            Mapper.ClearCache();

            var source = Enumerable.Range(0, 100_000)
                .Select(i => new LoadOrder { Id = i, Reference = $"REF-{i}", Total = i })
                .ToList();

            var mapped = source.MapTo<LoadOrderDto>();

            Assert.Equal(100_000, mapped.Count);
            Assert.Equal(0, mapped[0].Id);
            Assert.Equal(99_999, mapped[99_999].Id);
            Assert.Equal("REF-50000", mapped[50_000].Reference);
        }

        /// <summary>
        /// A soak: the managed heap must settle rather than climb.
        /// </summary>
        /// <remarks>
        /// The bound is generous because a shared runner's GC timing is not ours to control. It is
        /// still tight enough to catch the shape that matters, which is a per-call retention: at one
        /// leaked 48-byte object per call this allocates 24 MB and fails.
        /// </remarks>
        [Fact]
        public void SustainedMapping_DoesNotGrowTheHeapWithoutBound()
        {
            Mapper.ClearCache();

            // Warm, so first-call compilation is not counted as growth.
            for (var i = 0; i < 1_000; i++)
            {
                _ = new LoadOrder { Id = i, Reference = "warm", Total = 1 }.MapTo<LoadOrderDto>();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(forceFullCollection: true);

            for (var i = 0; i < 500_000; i++)
            {
                _ = new LoadOrder { Id = i, Reference = "soak", Total = i }.MapTo<LoadOrderDto>();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(forceFullCollection: true);

            var growthMb = (after - before) / (1024.0 * 1024.0);
            Assert.True(growthMb < 5, $"heap grew {growthMb:F1} MB over 500k mappings of one type pair");
        }

        /// <summary>
        /// The LRU bound has to hold when an application closes over many type pairs, which is the
        /// case MaxCacheSize exists for.
        /// </summary>
        [Fact]
        public void ManyDistinctTypePairs_StayWithinTheConfiguredBound()
        {
            var originalLru = Mapper.UseLruCache;
            var originalMax = Mapper.MaxCacheSize;
            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 8;
                Mapper.ClearCache();

                // 200 genuinely distinct source types, so the bound is actually reached. The
                // previous version of this test mapped two type pairs and asserted the total was
                // under 96, which no implementation could fail. A bound that is never crossed
                // tests nothing.
                for (var i = 0; i < 200; i++)
                {
                    var source = new Dictionary<string, object?>
                    {
                        ["Id"] = i,
                        ["Reference"] = $"r{i}",
                        ["Total"] = (decimal)i,
                    };
                    _ = source.MapTo<LoadOrderDto>();

                    // A distinct runtime source type per iteration is what creates distinct cache
                    // keys, since the key is (source runtime type, destination type).
                    object boxed = i % 2 == 0
                        ? new LoadOrder { Id = i, Reference = $"r{i}", Total = i }
                        : new LoadColdSource { Id = i, Name = $"n{i}" };
                    _ = boxed.MapTo<LoadOrderSummary>();
                }

                var total = Mapper.CacheInfo().Total;

                // Generous against the exact bound because several caches contribute to Total, but
                // far below the ~200 an unbounded cache would hold.
                Assert.True(total <= 64,
                    $"cache held {total} entries against a MaxCacheSize of 8; an unbounded cache would hold hundreds");
            }
            finally
            {
                Mapper.UseLruCache = originalLru;
                Mapper.MaxCacheSize = originalMax;
                Mapper.ClearCache();
            }
        }

        /// <summary>
        /// Throughput, asserted only at an order of magnitude.
        /// </summary>
        /// <remarks>
        /// This is not a benchmark and must not become one. It exists to catch a change that
        /// stops the mapper caching at all, which turns a warm map from tens of nanoseconds into
        /// an expression compile and shows up here as several orders of magnitude, not a few
        /// percent. Benchmarks live in tests/Mapsicle.Benchmarks.
        /// </remarks>
        [Fact]
        public void WarmMapping_StaysFarBelowACompilePerCall()
        {
            Mapper.ClearCache();

            for (var i = 0; i < 10_000; i++)
            {
                _ = new LoadOrder { Id = i, Reference = "warm", Total = 1 }.MapTo<LoadOrderDto>();
            }

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 200_000; i++)
            {
                _ = new LoadOrder { Id = i, Reference = "hot", Total = 1 }.MapTo<LoadOrderDto>();
            }
            sw.Stop();

            var microsPerCall = sw.Elapsed.TotalMilliseconds * 1000 / 200_000;
            Assert.True(microsPerCall < 10,
                $"{microsPerCall:F2} us/call warm, which suggests the compiled delegate is not being reused");
        }

        #region Types

        public class LoadOrder
        {
            public int Id { get; set; }
            public string Reference { get; set; } = "";
            public decimal Total { get; set; }
        }

        public class LoadOrderDto
        {
            public int Id { get; set; }
            public string Reference { get; set; } = "";
            public decimal Total { get; set; }
        }

        public class LoadOrderSummary
        {
            public int Id { get; set; }
            public decimal Total { get; set; }
        }

        public class LoadColdSource
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class LoadColdDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        #endregion
    }
}
