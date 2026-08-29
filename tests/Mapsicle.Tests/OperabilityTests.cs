using System;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// The numbers and knobs an operator reads or turns, and whether they mean anything.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class OperabilityTests
    {
        // ---- Cache statistics -------------------------------------------------------------------
        // Hits, Misses and HitRatio were written only by a method nothing ever called, so they
        // reported zero under any load while looking like live instrumentation.

        [Fact]
        public void CacheStatistics_CountHitsAndMisses_UnderTheBoundedCache()
        {
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = true;
            try
            {
                Mapper.ClearCache();

                var source = new StatsSource { Value = 1 };

                // First map of the pair is a miss, every later one is a hit.
                for (int i = 0; i < 25; i++)
                {
                    ((object)source).MapTo<StatsDest>();
                }

                var info = Mapper.CacheInfo();

                Assert.Equal(1, info.Misses);
                Assert.Equal(24, info.Hits);
                Assert.Equal(24d / 25d, info.HitRatio, 6);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }

        [Fact]
        public void CacheStatistics_AreResetByClearCache()
        {
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = true;
            try
            {
                var source = new StatsResetSource { Value = 1 };
                ((object)source).MapTo<StatsResetDest>();
                ((object)source).MapTo<StatsResetDest>();

                Assert.True(Mapper.CacheInfo().Hits > 0);

                Mapper.ClearCache();

                Assert.Equal(0, Mapper.CacheInfo().Hits);
                Assert.Equal(0, Mapper.CacheInfo().Misses);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }

        [Fact]
        public void CacheStatistics_StayZero_OnTheUnboundedCache_AsDocumented()
        {
            // Not an oversight. The unbounded cache has no capacity to tune a hit ratio against, and
            // an atomic increment on every call of the default warm path costs more than the number
            // is worth. The XML docs on Hits and Misses say so; this pins that they still agree.
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = false;
            try
            {
                Mapper.ClearCache();

                var source = new StatsUnboundedSource { Value = 1 };
                for (int i = 0; i < 10; i++)
                {
                    ((object)source).MapTo<StatsUnboundedDest>();
                }

                var info = Mapper.CacheInfo();

                Assert.Equal(0, info.Hits);
                Assert.Equal(0, info.Misses);

                // The positive control: the cache itself is demonstrably working even though the
                // counters are deliberately idle.
                Assert.True(info.MapToEntries > 0);
            }
            finally
            {
                Mapper.ClearCache();
            }
        }

        // ---- MaxDepth of zero -------------------------------------------------------------------

        [Fact]
        public void MapperOptions_MaxDepthOfZero_IsRejected_NotAccepted()
        {
            // Zero disabled the mapper outright: the first depth check failed before any property
            // was read, so every map returned the destination default, silently.
            var options = new MapperOptions { MaxDepth = 0 };

            Assert.Equal(32, options.MaxDepth);

            using var mapper = MapperFactory.Create(options);
            var result = mapper.MapTo<DepthDest>(new DepthSource { Value = 5 });

            Assert.NotNull(result);
            Assert.Equal(5, result!.Value);
        }

        [Fact]
        public void MapperOptions_NegativeMaxDepth_IsRejected()
        {
            Assert.Equal(32, new MapperOptions { MaxDepth = -1 }.MaxDepth);
        }

        [Fact]
        public void MapperOptions_APositiveMaxDepth_IsKept()
        {
            // Positive control. Refusing every value would satisfy the two tests above while
            // breaking the setting for everyone who uses it correctly.
            Assert.Equal(4, new MapperOptions { MaxDepth = 4 }.MaxDepth);
        }

        [Fact]
        public void MapperOptions_ALowMaxDepth_StillStopsRecursion()
        {
            // The other half of the control: the guard must not have quietly disabled depth limits.
            using var mapper = MapperFactory.Create(new MapperOptions { MaxDepth = 2 });

            var root = new DepthNode { Name = "0" };
            var current = root;
            for (int i = 1; i <= 6; i++)
            {
                current.Child = new DepthNode { Name = i.ToString() };
                current = current.Child;
            }

            var result = mapper.MapTo<DepthNodeDto>(root);

            Assert.NotNull(result);
            // Two levels of depth means the graph is not walked all the way down.
            Assert.Null(result!.Child?.Child?.Child?.Child);
        }

        // ---- Types, unique to this file ----------------------------------------------------------

        public class StatsSource { public int Value { get; set; } }
        public class StatsDest { public int Value { get; set; } }
        public class StatsResetSource { public int Value { get; set; } }
        public class StatsResetDest { public int Value { get; set; } }
        public class StatsUnboundedSource { public int Value { get; set; } }
        public class StatsUnboundedDest { public int Value { get; set; } }
        public class DepthSource { public int Value { get; set; } }
        public class DepthDest { public int Value { get; set; } }

        public class DepthNode { public string? Name { get; set; } public DepthNode? Child { get; set; } }
        public class DepthNodeDto { public string? Name { get; set; } public DepthNodeDto? Child { get; set; } }
    }
}
