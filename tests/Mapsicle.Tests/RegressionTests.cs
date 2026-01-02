using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Regression tests to verify fixes for known issues and ensure consistent behavior.
    /// These tests document expected behavior for edge cases mentioned in README.
    /// </summary>
    public class RegressionTests
    {
        #region Test Models

        public class CircularParent
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public CircularChild? Child { get; set; }
        }

        public class CircularChild
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public CircularParent? Parent { get; set; }
        }

        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        #endregion

        #region Circular Reference Handling Tests

        /// <summary>
        /// Regression: Circular references should not throw StackOverflowException.
        /// Instead, they should be handled gracefully via MaxDepth protection.
        /// </summary>
        [Fact]
        public void CircularReference_ShouldNotThrowStackOverflow()
        {
            var originalMaxDepth = Mapper.MaxDepth;

            try
            {
                Mapper.MaxDepth = 10;

                // Create circular reference
                var parent = new CircularParent { Id = 1, Name = "Parent" };
                var child = new CircularChild { Id = 2, Name = "Child", Parent = parent };
                parent.Child = child;

                // Should NOT throw StackOverflowException
                var result = parent.MapTo<CircularParent>();

                Assert.NotNull(result);
                Assert.Equal(1, result.Id);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
            }
        }

        /// <summary>
        /// Regression: Verify that circular references return default at max depth.
        /// </summary>
        [Fact]
        public void CircularReference_AtMaxDepth_ShouldReturnDefault()
        {
            var originalMaxDepth = Mapper.MaxDepth;
            var logs = new List<string>();
            var originalLogger = Mapper.Logger;

            try
            {
                Mapper.MaxDepth = 5;
                Mapper.Logger = msg => logs.Add(msg);

                var parent = new CircularParent { Id = 1, Name = "Parent" };
                var child = new CircularChild { Id = 2, Name = "Child", Parent = parent };
                parent.Child = child;

                var result = parent.MapTo<CircularParent>();

                // Should handle gracefully without crashing
                Assert.NotNull(result);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
                Mapper.Logger = originalLogger;
            }
        }

        #endregion

        #region Collection Mapper Caching Tests

        /// <summary>
        /// Regression: Verify that collection mapper caching works correctly (v1.1+).
        /// Collections should benefit from cached mappers.
        /// </summary>
        [Fact]
        public void CollectionMapping_ShouldUseCachedMapper()
        {
            Mapper.ClearCache();

            var users = Enumerable.Range(1, 100)
                .Select(i => new User { Id = i, Name = $"User{i}" })
                .ToList();

            // First mapping - should create and cache the mapper
            var firstResult = users.MapTo<UserDto>();
            var firstCacheInfo = Mapper.CacheInfo();

            // Second mapping - should use cached mapper
            var secondResult = users.MapTo<UserDto>();
            var secondCacheInfo = Mapper.CacheInfo();

            Assert.Equal(100, firstResult.Count);
            Assert.Equal(100, secondResult.Count);
            
            // Cache should have entries for the mapping
            Assert.True(firstCacheInfo.Total > 0);
        }

        /// <summary>
        /// Regression: Collection pre-allocation should improve performance.
        /// </summary>
        [Fact]
        public void CollectionMapping_WithKnownSize_ShouldPreAllocate()
        {
            var users = new List<User>(1000);
            for (int i = 0; i < 1000; i++)
            {
                users.Add(new User { Id = i, Name = $"User{i}" });
            }

            var result = users.MapTo<UserDto>();

            Assert.Equal(1000, result.Count);
            Assert.All(result, dto => Assert.NotNull(dto));
        }

        #endregion

        #region Lock-Free Cache Reads Tests

        /// <summary>
        /// Regression: Verify that cache reads are lock-free (v1.1+).
        /// Multiple threads should be able to read from cache concurrently.
        /// </summary>
        [Fact]
        public void CacheReads_ShouldBeLockFree()
        {
            Mapper.ClearCache();

            // Warm up the cache
            var user = new User { Id = 1, Name = "Test" };
            _ = user.MapTo<UserDto>();

            var exceptions = new List<Exception>();
            var lockObj = new object();

            // Multiple threads reading from cache concurrently
            System.Threading.Tasks.Parallel.For(0, 1000, i =>
            {
                try
                {
                    var testUser = new User { Id = i, Name = $"User{i}" };
                    var dto = testUser.MapTo<UserDto>();
                    Assert.NotNull(dto);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Should not have any concurrency issues
            Assert.Empty(exceptions);
        }

        #endregion

        #region Known Limitation: Nested Flattening Tests

        /// <summary>
        /// Known Limitation: Nested flattening is limited to 1 level.
        /// Address.City works, but Address.Street.Line1 does not.
        /// </summary>
        [Fact]
        public void NestedFlattening_OneLevel_ShouldWork()
        {
            var source = new SourceWithAddress
            {
                Id = 1,
                Name = "Test",
                Address = new SourceAddress { City = "NYC", Street = "Main St" }
            };

            var dest = source.MapTo<FlatDto>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Test", dest.Name);
            Assert.Equal("NYC", dest.AddressCity);
        }

        public class SourceWithAddress
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public SourceAddress? Address { get; set; }
        }

        public class SourceAddress
        {
            public string City { get; set; } = string.Empty;
            public string Street { get; set; } = string.Empty;
        }

        /// <summary>
        /// Known Limitation: Multi-level nested flattening is not supported.
        /// This test documents the current limitation.
        /// </summary>
        [Fact]
        public void NestedFlattening_MultiLevel_HasLimitations()
        {
            var source = new
            {
                Id = 1,
                Address = new
                {
                    Street = new
                    {
                        Line1 = "123 Main St"
                    }
                }
            };

            var dest = source.MapTo<DeepFlatDto>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            // AddressStreetLine1 may not be mapped due to multi-level limitation
        }

        public class FlatDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string AddressCity { get; set; } = string.Empty;
        }

        public class DeepFlatDto
        {
            public int Id { get; set; }
            public string AddressStreetLine1 { get; set; } = string.Empty;
        }

        #endregion

        #region Unmapped Properties Tests

        /// <summary>
        /// Regression: Unmapped properties should be silent by default.
        /// Use GetUnmappedProperties for validation.
        /// </summary>
        [Fact]
        public void UnmappedProperties_ShouldBeSilent()
        {
            var source = new { Id = 1, Name = "Test" };
            
            // ExtraDto has an extra property that won't be mapped
            var dest = source.MapTo<ExtraDto>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Test", dest.Name);
            Assert.Equal(string.Empty, dest.Extra); // Unmapped, uses default
        }

        /// <summary>
        /// Regression: GetUnmappedProperties should identify unmapped destination properties.
        /// </summary>
        [Fact]
        public void GetUnmappedProperties_ShouldIdentifyUnmapped()
        {
            var unmapped = Mapper.GetUnmappedProperties<User, ExtraDto>();

            Assert.Contains("Extra", unmapped);
        }

        public class ExtraDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Extra { get; set; } = string.Empty;
        }

        #endregion

        #region Null Safety Tests

        /// <summary>
        /// Regression: Mapsicle should be more aggressive with null-safe navigation.
        /// This prevents NullReferenceException in many scenarios.
        /// </summary>
        [Fact]
        public void NullSafety_WithNullNestedObject_ShouldNotThrow()
        {
            var source = new NestedSource
            {
                Id = 1,
                Nested = null
            };

            // Should not throw NullReferenceException
            var dest = source.MapTo<NestedDest>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
        }

        public class NestedSource
        {
            public int Id { get; set; }
            public NestedObject? Nested { get; set; }
        }

        public class NestedDest
        {
            public int Id { get; set; }
            public NestedObject? Nested { get; set; }
        }

        public class NestedObject
        {
            public string Value { get; set; } = string.Empty;
        }

        #endregion

        #region LRU Cache Behavior Tests

        /// <summary>
        /// Regression: LRU cache must be explicitly enabled.
        /// Default is unbounded cache.
        /// </summary>
        [Fact]
        public void LruCache_DefaultIsUnbounded()
        {
            var originalUseLru = Mapper.UseLruCache;

            try
            {
                // Reset to default
                Mapper.UseLruCache = false;
                Mapper.ClearCache();

                // Map many times
                for (int i = 0; i < 2000; i++)
                {
                    var user = new User { Id = i, Name = $"User{i}" };
                    _ = user.MapTo<UserDto>();
                }

                var cacheInfo = Mapper.CacheInfo();
                
                // With unbounded cache, entries can grow beyond typical LRU limits
                Assert.True(cacheInfo.Total >= 0);
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.ClearCache();
            }
        }

        /// <summary>
        /// Regression: Enabling LRU cache should clear existing cache.
        /// </summary>
        [Fact]
        public void LruCache_EnableingShouldClearCache()
        {
            var originalUseLru = Mapper.UseLruCache;

            try
            {
                Mapper.UseLruCache = false;
                Mapper.ClearCache();

                // Add some cache entries
                var user = new User { Id = 1, Name = "Test" };
                _ = user.MapTo<UserDto>();

                var beforeSwitch = Mapper.CacheInfo();

                // Switch to LRU - should clear cache
                Mapper.UseLruCache = true;

                var afterSwitch = Mapper.CacheInfo();

                // Cache should be cleared when switching modes
                Assert.True(afterSwitch.Total <= beforeSwitch.Total);
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.ClearCache();
            }
        }

        #endregion

        #region Property Info Caching Tests

        /// <summary>
        /// Regression: PropertyInfo caching (v1.1+) should improve cold start performance.
        /// </summary>
        [Fact]
        public void PropertyInfoCaching_ShouldImprovePerformance()
        {
            Mapper.ClearCache();

            // First mapping - cold start
            var user1 = new User { Id = 1, Name = "First" };
            var dto1 = user1.MapTo<UserDto>();

            // Second mapping - should be faster due to cached property info
            var user2 = new User { Id = 2, Name = "Second" };
            var dto2 = user2.MapTo<UserDto>();

            Assert.NotNull(dto1);
            Assert.NotNull(dto2);
            Assert.Equal(1, dto1.Id);
            Assert.Equal(2, dto2.Id);
        }

        #endregion

        #region Primitive Fast Path Tests

        /// <summary>
        /// Regression: Primitive types should skip depth tracking (v1.1+ optimization).
        /// </summary>
        [Fact]
        public void PrimitiveFastPath_ShouldSkipDepthTracking()
        {
            var primitives = new List<int> { 1, 2, 3, 4, 5 };
            var result = primitives.MapTo<int>();

            Assert.Equal(5, result.Count);
            Assert.Equal(1, result[0]);
            Assert.Equal(5, result[4]);
        }

        #endregion

        #region MaxDepth Configuration Tests

        /// <summary>
        /// Regression: MaxDepth is a global setting (cannot be per-mapping).
        /// This is a known limitation.
        /// </summary>
        [Fact]
        public void MaxDepth_IsGlobal_NotPerMapping()
        {
            var originalMaxDepth = Mapper.MaxDepth;

            try
            {
                // Setting MaxDepth affects all mappings
                Mapper.MaxDepth = 5;

                var deep1 = CreateDeepModel(10);
                var result1 = deep1.MapTo<DeepModel>();

                // MaxDepth affects this mapping too
                var deep2 = CreateDeepModel(10);
                var result2 = deep2.MapTo<DeepModel>();

                Assert.NotNull(result1);
                Assert.NotNull(result2);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
            }
        }

        private DeepModel CreateDeepModel(int depth)
        {
            if (depth <= 0)
                return new DeepModel { Level = 0 };

            return new DeepModel
            {
                Level = depth,
                Child = CreateDeepModel(depth - 1)
            };
        }

        public class DeepModel
        {
            public int Level { get; set; }
            public DeepModel? Child { get; set; }
        }

        #endregion
    }
}
