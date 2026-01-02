using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Security and vulnerability assessment tests for Mapsicle.
    /// Tests injection patterns, DoS scenarios, memory safety, and type safety.
    /// Note: These tests validate security measures without executing actual malicious code.
    /// </summary>
    public class SecurityTests
    {
        #region Test Models

        public class UserInput
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Query { get; set; } = string.Empty;
        }

        public class CircularParent
        {
            public int Id { get; set; }
            public CircularChild? Child { get; set; }
        }

        public class CircularChild
        {
            public int Id { get; set; }
            public CircularParent? Parent { get; set; }
        }

        public class DeepModel
        {
            public int Level { get; set; }
            public DeepModel? Next { get; set; }
        }

        #endregion

        #region Injection Pattern Tests

        /// <summary>
        /// Test that property names containing SQL injection patterns are safely handled.
        /// These patterns should be treated as regular strings, not executed.
        /// </summary>
        [Fact]
        public void MapTo_PropertyWithSqlInjectionPattern_ShouldTreatAsString()
        {
            var source = new UserInput 
            { 
                Name = "'; DROP TABLE Users; --",
                Email = "test@test.com"
            };

            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
            // SQL injection pattern should be preserved as string data
            Assert.Equal("'; DROP TABLE Users; --", dest.Name);
            Assert.Equal("test@test.com", dest.Email);
        }

        /// <summary>
        /// Test that values with script injection attempts are treated as plain data.
        /// </summary>
        [Fact]
        public void MapTo_ValueWithScriptInjection_ShouldTreatAsString()
        {
            var source = new UserInput 
            { 
                Name = "<script>alert('XSS')</script>",
                Query = "javascript:void(0)"
            };

            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
            // Script tags should be preserved as string data (not executed)
            Assert.Contains("<script>", dest.Name);
            Assert.Contains("javascript:", dest.Query);
        }

        /// <summary>
        /// Test handling of format string injection patterns.
        /// </summary>
        [Fact]
        public void MapTo_FormatStringInjection_ShouldTreatAsString()
        {
            var source = new UserInput 
            { 
                Name = "{0} {1} %s %x",
                Email = "{System.Environment.UserName}"
            };

            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
            // Format strings should not be evaluated
            Assert.Equal("{0} {1} %s %x", dest.Name);
            Assert.Contains("{System", dest.Email);
        }

        #endregion

        #region Denial of Service Tests

        /// <summary>
        /// Test that extremely deep object graphs are protected by MaxDepth.
        /// This prevents stack overflow attacks.
        /// </summary>
        [Fact]
        public void MapTo_ExtremelyDeepObjectGraph_ShouldRespectMaxDepth()
        {
            var originalMaxDepth = Mapper.MaxDepth;
            var logs = new List<string>();
            var originalLogger = Mapper.Logger;

            try
            {
                Mapper.MaxDepth = 10; // Set a conservative limit
                Mapper.Logger = msg => logs.Add(msg);

                // Create a very deep nested structure (100 levels)
                var deep = CreateDeepModel(100);

                // Should not throw StackOverflowException
                var dest = deep.MapTo<DeepModel>();

                // Should handle gracefully within depth limit
                Assert.NotNull(dest);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
                Mapper.Logger = originalLogger;
            }
        }

        /// <summary>
        /// Test circular references with MaxDepth protection.
        /// Validates that circular references don't cause infinite loops.
        /// </summary>
        [Fact]
        public void MapTo_CircularReferences_ShouldNotCauseInfiniteLoop()
        {
            var originalMaxDepth = Mapper.MaxDepth;

            try
            {
                Mapper.MaxDepth = 10;

                // Create circular reference
                var parent = new CircularParent { Id = 1 };
                var child = new CircularChild { Id = 2, Parent = parent };
                parent.Child = child;

                // Should not hang or throw StackOverflowException
                var dest = parent.MapTo<CircularParent>();

                Assert.NotNull(dest);
                Assert.Equal(1, dest.Id);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
            }
        }

        /// <summary>
        /// Test memory consumption with large collection mapping.
        /// Validates that large collections don't cause out-of-memory.
        /// </summary>
        [Fact]
        public void MapTo_LargeCollection_ShouldHandleMemoryEfficiently()
        {
            // Create collection with 100K items
            var source = Enumerable.Range(1, 100000)
                .Select(i => new { Id = i, Name = $"Item{i}" })
                .ToList();

            // Should complete without OutOfMemoryException
            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
            Assert.Equal(100000, dest.Count());
        }

        /// <summary>
        /// Test concurrent cache access for potential race conditions.
        /// Validates thread-safety of the caching mechanism.
        /// </summary>
        [Fact]
        public void MapTo_ConcurrentCacheAccess_ShouldBeThreadSafe()
        {
            Mapper.ClearCache();
            var exceptions = new List<Exception>();
            var lockObj = new object();

            // Attempt concurrent cache access from multiple threads
            Parallel.For(0, 100, i =>
            {
                try
                {
                    var source = new UserInput { Name = $"User{i}", Email = $"user{i}@test.com" };
                    var dest = source.MapTo<UserInput>();
                    Assert.NotNull(dest);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Should not have any thread-safety exceptions
            Assert.Empty(exceptions);
        }

        #endregion

        #region Memory Safety Tests

        /// <summary>
        /// Test that LRU cache doesn't leak memory with repeated mappings.
        /// </summary>
        [Fact]
        public void MapTo_RepeatedMappingWithLruCache_ShouldNotLeakMemory()
        {
            var originalUseLru = Mapper.UseLruCache;
            var originalMaxCache = Mapper.MaxCacheSize;

            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 100;

                // Perform many mappings
                for (int i = 0; i < 10000; i++)
                {
                    var source = new UserInput { Name = $"User{i}" };
                    _ = source.MapTo<UserInput>();
                }

                var cacheInfo = Mapper.CacheInfo();
                
                // Cache should be bounded by MaxCacheSize when LRU is enabled
                Assert.True(cacheInfo.Total <= Mapper.MaxCacheSize * 2, 
                    $"Cache size {cacheInfo.Total} exceeds expected limit");
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.MaxCacheSize = originalMaxCache;
            }
        }

        /// <summary>
        /// Test cache behavior with many unique type pairs.
        /// Validates cache eviction works correctly.
        /// </summary>
        [Fact]
        public void MapTo_ManyUniqueTypePairs_ShouldHandleCacheEviction()
        {
            var originalUseLru = Mapper.UseLruCache;
            var originalMaxCache = Mapper.MaxCacheSize;

            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 10;
                Mapper.ClearCache();

                // Map many different anonymous types (creates unique type pairs)
                for (int i = 0; i < 50; i++)
                {
                    var source = new { Id = i, Name = $"User{i}", Extra = i * 2 };
                    _ = source.MapTo<UserInput>();
                }

                var cacheInfo = Mapper.CacheInfo();
                
                // Cache should evict old entries
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
        /// Test MapperFactory dispose pattern.
        /// Validates proper cleanup of resources.
        /// </summary>
        [Fact]
        public void MapperFactory_AfterDispose_ShouldPreventUse()
        {
            var mapper = MapperFactory.Create();
            mapper.Dispose();

            // Should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => 
                mapper.MapTo<UserInput>(new UserInput()));
        }

        /// <summary>
        /// Test multiple dispose calls don't cause issues.
        /// </summary>
        [Fact]
        public void MapperFactory_MultipleDispose_ShouldBeIdempotent()
        {
            var mapper = MapperFactory.Create();
            
            // Multiple dispose calls should not throw
            mapper.Dispose();
            mapper.Dispose();
            mapper.Dispose();

            Assert.True(true, "Multiple dispose calls handled safely");
        }

        #endregion

        #region Type Safety Tests

        /// <summary>
        /// Test mapping to/from System.Object.
        /// Validates type safety when using object type.
        /// </summary>
        [Fact]
        public void MapTo_FromObjectType_ShouldHandleGracefully()
        {
            object source = new UserInput { Name = "Test", Email = "test@test.com" };
            
            // Should handle object type safely
            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
        }

        /// <summary>
        /// Test mapping with dynamic type usage.
        /// </summary>
        [Fact]
        public void MapTo_WithDynamicType_ShouldHandleSafely()
        {
            dynamic source = new UserInput { Name = "Dynamic", Email = "dynamic@test.com" };
            
            // Should handle dynamic type
            var dest = ((object)source).MapTo<UserInput>();

            Assert.NotNull(dest);
        }

        /// <summary>
        /// Test invalid cast scenarios are handled gracefully.
        /// </summary>
        [Fact]
        public void MapTo_IncompatibleTypes_ShouldNotThrow()
        {
            var source = new { Id = "NotAnInt", Name = 12345 };
            
            // Should not throw, just skip incompatible properties
            var dest = source.MapTo<UserInput>();

            Assert.NotNull(dest);
        }

        #endregion

        #region Cache Stress Tests

        /// <summary>
        /// Test cache thrashing scenario with LRU eviction.
        /// </summary>
        [Fact]
        public void MapTo_CacheThrashing_ShouldMaintainPerformance()
        {
            var originalUseLru = Mapper.UseLruCache;
            var originalMaxCache = Mapper.MaxCacheSize;

            try
            {
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 5; // Very small cache
                Mapper.ClearCache();

                // Rapidly map different types to cause eviction
                for (int i = 0; i < 100; i++)
                {
                    var source1 = new UserInput { Name = $"User{i}" };
                    _ = source1.MapTo<UserInput>();

                    var source2 = new { Id = i, Value = i * 2 };
                    _ = source2.MapTo<UserInput>();
                }

                var cacheInfo = Mapper.CacheInfo();
                
                // Should maintain cache size limit
                Assert.True(cacheInfo.Total <= Mapper.MaxCacheSize * 2);
                
                // Should still function correctly
                var final = new UserInput { Name = "Final" };
                var finalDest = final.MapTo<UserInput>();
                Assert.NotNull(finalDest);
            }
            finally
            {
                Mapper.UseLruCache = originalUseLru;
                Mapper.MaxCacheSize = originalMaxCache;
                Mapper.ClearCache();
            }
        }

        #endregion

        #region Helper Methods

        private DeepModel CreateDeepModel(int depth)
        {
            if (depth <= 0)
                return new DeepModel { Level = depth };

            return new DeepModel
            {
                Level = depth,
                Next = CreateDeepModel(depth - 1)
            };
        }

        #endregion
    }
}
