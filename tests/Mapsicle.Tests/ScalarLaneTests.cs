using System;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A value mapped on its own converts the same way on every lane. The untyped MapTo already
    /// asked the shared cascade; the typed MapTo and MapperFactory returned default instead.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class ScalarLaneTests
    {
        public enum SlColour { Red = 1, Green = 2 }

        public ScalarLaneTests() => Mapper.ClearCache();

        [Fact]
        public void TypedWidensIntToLong() => Assert.Equal(5L, 5.MapTo<int, long>());

        [Fact]
        public void TypedMapsEnumToInt() => Assert.Equal(2, SlColour.Green.MapTo<SlColour, int>());

        [Fact]
        public void TypedMapsIntToNullableLong() => Assert.Equal(5L, 5.MapTo<int, long?>());

        [Fact]
        public void FactoryWidensIntToLong()
        {
            using var mapper = MapperFactory.Create();
            Assert.Equal(5L, mapper.MapTo<long>(5));
        }

        [Fact]
        public void FactoryMapsEnumToInt()
        {
            using var mapper = MapperFactory.Create();
            Assert.Equal(2, mapper.MapTo<int>(SlColour.Green));
        }

        [Fact]
        public void Control_UntypedWidensIntToLong() => Assert.Equal(5L, ((object)5).MapTo<long>());
    }
}
