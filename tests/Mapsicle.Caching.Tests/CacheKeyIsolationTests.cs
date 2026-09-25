using System.Threading.Tasks;
using Mapsicle;
using Mapsicle.Caching;
using Mapsicle.Fluent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mapsicle.Caching.Tests
{
    public class CkiSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class CkiDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    public class CkiFieldSrc { public int Id { get; set; } public string Tag = ""; }
    public class CkiFieldDto { public int Id { get; set; } public string Tag { get; set; } = ""; }

    public class CkiNode { public int Id { get; set; } public CkiNode? Next { get; set; } }
    public class CkiNodeDto { public int Id { get; set; } }

    public class CkiWireA { public int Id { get; set; } }
    public class CkiWireB { public int Id { get; set; } public string Name { get; set; } = ""; }

    [Collection("StaticMapperTests")]
    public class CacheKeyIsolationTests
    {
        public CacheKeyIsolationTests() => Mapper.ClearCache();

        private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

        private static IDistributedCache NewDistributedCache() =>
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        [Fact]
        public void MapToCachedAuto_EntryPlantedUnderTheSameKey_IsNotReturned()
        {
            // Stands in for a hash collision: something else already sits under this source's key.
            var cache = NewCache();
            var src = new CkiSrc { Id = 1, Name = "mine" };
            cache.Set(CachingExtensions.GenerateCacheKey(src, typeof(CkiDto)), new CkiDto { Id = 99, Name = "theirs" });

            var dto = src.MapToCachedAuto<CkiDto>(cache);

            Assert.Equal("mine", dto!.Name);
        }

        [Fact]
        public void Control_MapToCachedAuto_EqualSourcesStillHit()
        {
            var cache = NewCache();
            var first = new CkiSrc { Id = 2, Name = "same" }.MapToCachedAuto<CkiDto>(cache);
            var second = new CkiSrc { Id = 2, Name = "same" }.MapToCachedAuto<CkiDto>(cache);

            Assert.Same(first, second);
        }

        [Fact]
        public void MapToCachedAuto_SourcesDifferingOnlyInPublicField_GetTheirOwnResult()
        {
            var cache = NewCache();
            var first = new CkiFieldSrc { Id = 1, Tag = "alice" }.MapToCachedAuto<CkiFieldDto>(cache);
            var second = new CkiFieldSrc { Id = 1, Tag = "bob" }.MapToCachedAuto<CkiFieldDto>(cache);

            Assert.Equal("alice", first!.Tag);
            Assert.Equal("bob", second!.Tag);
        }

        [Fact]
        public void MapToCachedAuto_SourceDeeperThanJsonMaxDepth_Maps()
        {
            var head = new CkiNode { Id = 0 };
            var node = head;
            for (var i = 1; i < 100; i++)
            {
                node.Next = new CkiNode { Id = i };
                node = node.Next;
            }

            Assert.Equal(0, head.MapToCachedAuto<CkiNodeDto>(NewCache())!.Id);
        }

        [Fact]
        public void CachedMappers_WithDifferentConfigurations_SharingACache_GetTheirOwnResults()
        {
            var cache = NewCache();
            var upper = new CachedMapper(
                new MapperConfiguration(cfg => cfg.CreateMap<CkiSrc, CkiDto>()
                    .ForMember(d => d.Name, opt => opt.MapFrom(s => s.Name.ToUpperInvariant()))).CreateMapper(),
                cache);
            var plain = new CachedMapper(new MapperConfiguration(_ => { }).CreateMapper(), cache);
            var src = new CkiSrc { Id = 1, Name = "abc" };

            Assert.Equal("ABC", upper.Map<CkiDto>(src)!.Name);
            Assert.Equal("abc", plain.Map<CkiDto>(src)!.Name);
        }

        [Fact]
        public async Task MapToCachedAsync_EntryWrittenForAnotherDestination_IsNotReturned()
        {
            var cache = NewDistributedCache();
            var src = new CkiSrc { Id = 5, Name = "five" };

            await src.MapToCachedAsync<CkiWireA>(cache, "cki-shared");
            var b = await src.MapToCachedAsync<CkiWireB>(cache, "cki-shared");

            Assert.Equal("five", b!.Name);
        }

        [Fact]
        public async Task Control_MapToCachedAsync_RoundTrippableValueIsServedFromTheCache()
        {
            var cache = NewDistributedCache();

            await new CkiSrc { Id = 6, Name = "stored" }.MapToCachedAsync<CkiDto>(cache, "cki-hit");
            var hit = await new CkiSrc { Id = 6, Name = "fresh" }.MapToCachedAsync<CkiDto>(cache, "cki-hit");

            Assert.Equal("stored", hit!.Name);
        }
    }
}
