using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Where the runtime cannot build a mapper, the engine says so instead of answering wrongly.
    /// </summary>
    /// <remarks>
    /// Under NativeAOT an undeclared pair came back null from MapTo, wrote nothing through Map and
    /// filled zeros from a dictionary, with no exception and no log line. These run under JIT with the
    /// runtime's answer overridden; tests/Mapsicle.Aot.Smoke runs the same cases as a published binary.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class DynamicCodeGuardTests : IDisposable
    {
        public class DcgSource { public int Id { get; set; } public string? Name { get; set; } }
        public class DcgDest { public int Id { get; set; } public string? Name { get; set; } }

        public DynamicCodeGuardTests()
        {
            Mapper.ResetGeneratedRegistrations();
            DynamicCodeGuard.SupportedOverride = false;
        }

        public void Dispose()
        {
            DynamicCodeGuard.SupportedOverride = null;
            Mapper.ResetGeneratedRegistrations();
        }

        [Fact]
        public void UndeclaredMapToThrows() =>
            Assert.Throws<NotSupportedException>(() => new DcgSource { Id = 1 }.MapTo<DcgDest>());

        [Fact]
        public void UndeclaredTypedMapToThrows() =>
            Assert.Throws<NotSupportedException>(() => new DcgSource { Id = 1 }.MapTo<DcgSource, DcgDest>());

        [Fact]
        public void MapOntoExistingThrows() =>
            Assert.Throws<NotSupportedException>(() => new DcgSource { Id = 1 }.Map(new DcgDest()));

        [Fact]
        public void MapperFactoryThrows()
        {
            using var factory = MapperFactory.Create();
            Assert.Throws<NotSupportedException>(() => factory.MapTo<DcgDest>(new DcgSource { Id = 1 }));
        }

        [Fact]
        public void DictionaryThrows() =>
            Assert.Throws<NotSupportedException>(
                () => new Dictionary<string, object?> { ["Id"] = 1 }.MapTo<DcgDest>());

        [Fact]
        public void TheMessageNamesThePairAndTheAttribute()
        {
            var ex = Assert.Throws<NotSupportedException>(() => new DcgSource().MapTo<DcgDest>());
            Assert.Contains("MapsicleGenerate(typeof(DcgSource), typeof(DcgDest))", ex.Message);
        }

        [Fact]
        public void ACollectionPointsAtTheElementPair()
        {
            var ex = Assert.Throws<NotSupportedException>(
                () => ((object)new List<DcgSource>()).MapTo<List<DcgDest>>());
            Assert.Contains("source.MapTo<DcgDest>()", ex.Message);
        }

        [Fact]
        public void ScalarsStillConvert() =>
            Assert.Equal(5L, ((object)5).MapTo<long>());

        [Fact]
        public void DeclaredPairStillMaps()
        {
            Mapper.RegisterGenerated<DcgSource, DcgDest>(
                s => new DcgDest { Id = s.Id, Name = s.Name }, requiresDepthTracking: false);

            Assert.Equal("a", new DcgSource { Name = "a" }.MapTo<DcgDest>()!.Name);
            Assert.Equal("a", new DcgSource { Name = "a" }.MapTo<DcgSource, DcgDest>()!.Name);
        }

        [Fact]
        public void DeclaredPairStillMapsAsAList()
        {
            Mapper.RegisterGenerated<DcgSource, DcgDest>(
                s => new DcgDest { Id = s.Id, Name = s.Name }, requiresDepthTracking: false);

            var mapped = new List<DcgSource> { new() { Name = "a" }, new() { Name = "b" } }.MapTo<DcgDest>();

            Assert.Equal(new[] { "a", "b" }, new[] { mapped[0].Name, mapped[1].Name });
        }

        [Fact]
        public void WithDynamicCodeNothingChanges()
        {
            DynamicCodeGuard.SupportedOverride = true;
            Assert.Equal(3, new DcgSource { Id = 3 }.MapTo<DcgDest>()!.Id);
        }
    }
}
