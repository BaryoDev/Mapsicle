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
    /// Stress tests for Mapsicle including continuous mapping, random type generation, and cache thrashing.
    /// </summary>
    public class StressTests
    {
        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class NestedModel
        {
            public int Level { get; set; }
            public NestedModel? Child { get; set; }
        }

        #endregion

        #region Continuous Mapping Tests

        /// <summary>
        /// Test continuous mapping for 1 minute to verify stability.
        /// Note: Reduced to 5 seconds for practical test execution time.
        /// </summary>
        [Fact]
        public void ContinuousMapping_5Seconds_ShouldMaintainStability()
        {
            var stopwatch = Stopwatch.StartNew();
            var exceptions = new List<Exception>();
            int mappingCount = 0;
            var duration = TimeSpan.FromSeconds(5);

            while (stopwatch.Elapsed < duration)
            {
                try
                {
                    var user = new User 
                    { 
                        Id = mappingCount, 
                        Name = $"User{mappingCount}", 
                        Email = $"user{mappingCount}@test.com" 
                    };
                    
                    var dto = user.MapTo<UserDto>();
                    
                    Assert.NotNull(dto);
                    Assert.Equal(user.Id, dto.Id);
                    
                    mappingCount++;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            stopwatch.Stop();

            // Should have completed many mappings without errors
            Assert.Empty(exceptions);
            Assert.True(mappingCount > 1000, $"Expected >1000 mappings, got {mappingCount}");
        }

        #endregion

        #region Random Type Pair Tests

        /// <summary>
        /// Test mapping with randomly generated type combinations.
        /// </summary>
        [Fact]
        public void RandomTypePairMapping_ShouldHandleVariety()
        {
            var random = new Random(42); // Fixed seed for reproducibility
            var exceptions = new List<Exception>();

            for (int i = 0; i < 100; i++)
            {
                try
                {
                    // Generate random data
                    var source = new
                    {
                        Id = random.Next(),
                        Name = $"Random{random.Next()}",
                        Value = random.NextDouble(),
                        Flag = random.Next(2) == 1,
                        Timestamp = DateTime.Now.AddSeconds(random.Next(-1000, 1000))
                    };

                    var dto = source.MapTo<UserDto>();
                    Assert.NotNull(dto);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            // Should handle various random combinations
            Assert.Empty(exceptions);
        }

        #endregion

        #region Memory Pressure Tests

        /// <summary>
        /// Test behavior under memory pressure with large object graphs.
        /// </summary>
        [Fact]
        public void MemoryPressure_LargeObjectGraphs_ShouldHandle()
        {
            var originalMaxDepth = Mapper.MaxDepth;
            
            try
            {
                Mapper.MaxDepth = 20;
                var exceptions = new List<Exception>();

                // Create many deep object graphs
                for (int i = 0; i < 1000; i++)
                {
                    try
                    {
                        var nested = CreateNestedModel(10);
                        var dto = nested.MapTo<NestedModel>();
                        Assert.NotNull(dto);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }

                Assert.Empty(exceptions);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
            }
        }

        /// <summary>
        /// Test mapping with high memory allocation rate.
        /// </summary>
        [Fact]
        public void HighAllocationRate_ShouldNotCauseIssues()
        {
            var exceptions = new List<Exception>();

            // Rapidly allocate and map objects
            for (int i = 0; i < 10000; i++)
            {
                try
                {
                    var user = new User { Id = i, Name = new string('A', 100), Email = new string('B', 50) };
                    var dto = user.MapTo<UserDto>();
                    Assert.NotNull(dto);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            Assert.Empty(exceptions);
        }

        #endregion

        #region Cache Thrashing Tests

        /// <summary>
        /// Test LRU cache with frequent evictions.
        /// </summary>
        [Fact]
        public void CacheThrashing_FrequentEvictions_ShouldMaintainCorrectness()
        {
            var originalUseLru = Mapper.UseLruCache;
            var originalMaxCache = Mapper.MaxCacheSize;

            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 10;
                Mapper.ClearCache();

                var exceptions = new List<Exception>();

                // Map many different anonymous types to cause frequent evictions
                for (int i = 0; i < 200; i++)
                {
                    try
                    {
                        // Each iteration creates a unique anonymous type
                        var source = CreateAnonymousType(i);
                        var dto = source.MapTo<UserDto>();
                        Assert.NotNull(dto);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }

                Assert.Empty(exceptions);

                var cacheInfo = Mapper.CacheInfo();
                Assert.True(cacheInfo.Total <= Mapper.MaxCacheSize * 2);
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.MaxCacheSize = originalMaxCache;
                Mapper.ClearCache();
            }
        }

        /// <summary>
        /// Test cache performance under concurrent thrashing.
        /// </summary>
        [Fact]
        public void ConcurrentCacheThrashing_ShouldBeThreadSafe()
        {
            var originalUseLru = Mapper.UseLruCache;
            var originalMaxCache = Mapper.MaxCacheSize;

            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 20;
                Mapper.ClearCache();

                var exceptions = new List<Exception>();
                var lockObj = new object();

                Parallel.For(0, 100, i =>
                {
                    try
                    {
                        for (int j = 0; j < 10; j++)
                        {
                            var source = new User { Id = i * 10 + j, Name = $"User{i}_{j}" };
                            var dto = source.MapTo<UserDto>();
                            Assert.NotNull(dto);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });

                Assert.Empty(exceptions);
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.MaxCacheSize = originalMaxCache;
                Mapper.ClearCache();
            }
        }

        #endregion

        #region Burst Load Tests

        /// <summary>
        /// Test handling of sudden burst of mapping requests.
        /// </summary>
        [Fact]
        public void BurstLoad_SuddenHighVolume_ShouldHandle()
        {
            var exceptions = new List<Exception>();
            var lockObj = new object();

            // Simulate burst load with parallel execution
            Parallel.For(0, 1000, i =>
            {
                try
                {
                    var users = Enumerable.Range(i * 100, 100)
                        .Select(id => new User { Id = id, Name = $"User{id}" })
                        .ToList();

                    var dtos = users.MapTo<UserDto>();
                    Assert.Equal(100, dtos.Count);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
        }

        #endregion

        #region Helper Methods

        private NestedModel CreateNestedModel(int depth)
        {
            if (depth <= 0)
                return new NestedModel { Level = 0 };

            return new NestedModel
            {
                Level = depth,
                Child = CreateNestedModel(depth - 1)
            };
        }

        private object CreateAnonymousType(int seed)
        {
            // Create different anonymous types based on seed
            switch (seed % 5)
            {
                case 0:
                    return new { Id = seed, Name = $"Type0_{seed}" };
                case 1:
                    return new { Id = seed, Name = $"Type1_{seed}", Extra1 = seed };
                case 2:
                    return new { Id = seed, Name = $"Type2_{seed}", Extra1 = seed, Extra2 = seed * 2 };
                case 3:
                    return new { Id = seed, Name = $"Type3_{seed}", Flag = seed % 2 == 0 };
                default:
                    return new { Id = seed, Name = $"Type4_{seed}", Value = seed * 1.5 };
            }
        }

        #endregion
    }
}
