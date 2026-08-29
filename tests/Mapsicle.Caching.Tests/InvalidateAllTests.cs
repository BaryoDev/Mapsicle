using System;
using Mapsicle.Caching;
using Mapsicle.Fluent;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Mapsicle.Caching.Tests
{
    /// <summary>
    /// <see cref="CachedMapper.InvalidateAll"/> has to actually invalidate.
    /// </summary>
    /// <remarks>
    /// It was an empty method with a comment saying memory caches cannot be cleared, so a caller
    /// who invoked it kept receiving stale mappings until they expired with nothing to say the call
    /// had done nothing. A public method that documents a behaviour it does not perform is worse
    /// than an absent one, because the caller has no reason to look further.
    /// </remarks>
    public class InvalidateAllTests
    {
        private static CachedMapper NewMapper(IMemoryCache cache) =>
            new CachedMapper(new MapperConfiguration(_ => { }).CreateMapper(), cache);

        [Fact]
        public void InvalidateAll_DropsPreviouslyCachedMappings()
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var mapper = NewMapper(cache);

            var source = new Person { Id = 1, Name = "before" };
            var first = mapper.Map<PersonDto>(source);
            Assert.Equal("before", first!.Name);

            // The cache key is derived from the serialised source, so a changed source would miss
            // the cache on its own. Mutating the destination shape is not possible either, so the
            // stale read is demonstrated below with the same source object.
            mapper.InvalidateAll();

            var afterInvalidate = mapper.Map<PersonDto>(source);
            Assert.Equal("before", afterInvalidate!.Name);
            Assert.NotSame(first, afterInvalidate);
        }

        [Fact]
        public void WithoutInvalidateAll_TheSameSourceComesBackFromTheCache()
        {
            // The positive control. If mapping always produced a fresh instance, the test above
            // would pass whether InvalidateAll did anything or not.
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var mapper = NewMapper(cache);

            var source = new Person { Id = 2, Name = "cached" };

            var first = mapper.Map<PersonDto>(source);
            var second = mapper.Map<PersonDto>(source);

            Assert.Same(first, second);
        }

        [Fact]
        public void InvalidateAll_LeavesEntriesThisMapperDidNotCreate()
        {
            // The cache is normally resolved from the container and shared across the application,
            // so this must not behave like MemoryCache.Clear() and evict unrelated components.
            using var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("someone-elses-entry", "keep me");

            var mapper = NewMapper(cache);
            mapper.Map<PersonDto>(new Person { Id = 3, Name = "x" });

            mapper.InvalidateAll();

            Assert.True(cache.TryGetValue("someone-elses-entry", out var survived));
            Assert.Equal("keep me", survived);
        }

        [Fact]
        public void InvalidateAll_OnAnEmptyCache_DoesNotThrow()
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            NewMapper(cache).InvalidateAll();
        }

        [Fact]
        public void InvalidateAll_IsSafeToCallTwice()
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var mapper = NewMapper(cache);
            mapper.Map<PersonDto>(new Person { Id = 4, Name = "y" });

            mapper.InvalidateAll();
            mapper.InvalidateAll();
        }

        public class Person { public int Id { get; set; } public string? Name { get; set; } }
        public class PersonDto { public int Id { get; set; } public string? Name { get; set; } }
    }
}
