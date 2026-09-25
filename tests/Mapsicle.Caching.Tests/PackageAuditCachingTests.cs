using System;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public class PaCacheSrc { public int Id { get; set; } public string Name { get; set; } = ""; }

    public class PaCacheNsA { public class PaCacheDto { public int Id { get; set; } } }
    public class PaCacheNsB { public class PaCacheDto { public int Id { get; set; } public string Name { get; set; } = ""; } }

    public class PaCacheNode { public int Id { get; set; } public PaCacheNode? Next { get; set; } }
    public class PaCacheNodeDto { public int Id { get; set; } }

    public class PaCacheSecretSrc
    {
        public int Id { get; set; }
        [JsonIgnore] public string Payload { get; set; } = "";
    }
    public class PaCacheSecretDto { public int Id { get; set; } public string Payload { get; set; } = ""; }

    public class PaCacheWire { public int Id { get; set; } public string Internal { get; set; } = ""; }
    public class PaCacheWireDto { public int Id { get; set; } [JsonIgnore] public string Internal { get; set; } = ""; }

    [Collection("StaticMapperTests")]
    public class PackageAuditCachingTests
    {
        public PackageAuditCachingTests() => Mapper.ClearCache();

        private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

        [Fact]
        public void Control_SameNamedDestinationsHaveDifferentKeysWhenNamesDiffer()
        {
            var src = new PaCacheSrc { Id = 1, Name = "n" };
            Assert.NotEqual(
                CachingExtensions.GenerateCacheKey(src, typeof(PaCacheNodeDto)),
                CachingExtensions.GenerateCacheKey(src, typeof(PaCacheNsA.PaCacheDto)));
        }

        [Fact]
        public void GenerateCacheKey_DistinguishesDestinationsWithTheSameShortName()
        {
            var src = new PaCacheSrc { Id = 1, Name = "n" };
            Assert.NotEqual(
                CachingExtensions.GenerateCacheKey(src, typeof(PaCacheNsA.PaCacheDto)),
                CachingExtensions.GenerateCacheKey(src, typeof(PaCacheNsB.PaCacheDto)));
        }

        [Fact]
        public void MapToCachedAuto_SecondDestinationWithSameShortName_DoesNotThrow()
        {
            var cache = NewCache();
            var src = new PaCacheSrc { Id = 1, Name = "n" };
            var a = src.MapToCachedAuto<PaCacheNsA.PaCacheDto>(cache);
            Assert.Equal(1, a!.Id);

            var b = src.MapToCachedAuto<PaCacheNsB.PaCacheDto>(cache);
            Assert.Equal("n", b!.Name);
        }

        [Fact]
        public void Control_CyclicSource_MapsWithoutCache()
        {
            var node = new PaCacheNode { Id = 1 };
            node.Next = node;
            Assert.Equal(1, node.MapTo<PaCacheNodeDto>()!.Id);
        }

        [Fact]
        public void CachedMapper_CyclicSource_Maps()
        {
            var node = new PaCacheNode { Id = 1 };
            node.Next = node;
            var mapper = new CachedMapper(new MapperConfiguration(cfg => { }).CreateMapper(), NewCache());
            Assert.Equal(1, mapper.Map<PaCacheNodeDto>(node)!.Id);
        }

        [Fact]
        public void MapToCachedAuto_SourcesDifferingOnlyInJsonIgnoredMember_GetTheirOwnResult()
        {
            var cache = NewCache();
            var first = new PaCacheSecretSrc { Id = 1, Payload = "alice" }.MapToCachedAuto<PaCacheSecretDto>(cache);
            var second = new PaCacheSecretSrc { Id = 1, Payload = "bob" }.MapToCachedAuto<PaCacheSecretDto>(cache);
            Assert.Equal("alice", first!.Payload);
            Assert.Equal("bob", second!.Payload);
        }

        [Fact]
        public void Control_CachedMapper_WorksWithUnboundedCache()
        {
            var mapper = new CachedMapper(new MapperConfiguration(cfg => { }).CreateMapper(), NewCache());
            Assert.Equal(3, mapper.Map<PaCacheNodeDto>(new PaCacheNode { Id = 3 })!.Id);
        }

        [Fact]
        public void CachedMapper_WorksWithSizeLimitedCache()
        {
            var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
            var mapper = new CachedMapper(new MapperConfiguration(cfg => { }).CreateMapper(), cache);
            Assert.Equal(3, mapper.Map<PaCacheNodeDto>(new PaCacheNode { Id = 3 })!.Id);
        }

        [Fact]
        public async Task MapToCachedAsync_HitReturnsSameValuesAsMiss()
        {
            IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var src = new PaCacheWire { Id = 7, Internal = "x" };

            var miss = await src.MapToCachedAsync<PaCacheWireDto>(cache, "pa-wire-7");
            var hit = await src.MapToCachedAsync<PaCacheWireDto>(cache, "pa-wire-7");

            Assert.Equal("x", miss!.Internal);
            Assert.Equal(miss.Internal, hit!.Internal);
        }
    }
}
