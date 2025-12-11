using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    public class EdgeCasesTests
    {
        [Fact]
        public void NullableWrapping_IntToNullableInt_ShouldMap()
        {
            var source = new { Age = 25 };
            var dest = source.MapTo<NullableDto>();
            
            Assert.NotNull(dest);
            Assert.Equal(25, dest.Age);
        }

        [Fact]
        public void NullableUnwrapping_NullableIntToInt_ShouldMap()
        {
            var source = new NullableDto { Age = 25 };
            var dest = source.MapTo<StrictDto>();
            
            Assert.NotNull(dest);
            Assert.Equal(25, dest.Age);
        }

        [Fact]
        public void NullableUnwrapping_NullToInt_ShouldDefault()
        {
            var source = new NullableDto { Age = null };
            var dest = source.MapTo<StrictDto>();
            
            Assert.NotNull(dest);
            Assert.Equal(0, dest.Age);
        }

        [Fact]
        public void PrimitiveList_IntToString_ShouldMap()
        {
            var source = new List<int> { 1, 2, 3 };
            var dest = source.MapTo<string>().ToList();
            
            Assert.Equal(3, dest.Count);
            Assert.Equal("1", dest[0]);
            Assert.Equal("2", dest[1]);
            Assert.Equal("3", dest[2]);
        }

        [Fact]
        public void MapToExisting_SourceToNullable_ShouldMap()
        {
            var source = new { Age = 25 };
            var dest = new NullableDto();
            source.Map(dest);
            
            Assert.NotNull(dest);
            Assert.Equal(25, dest.Age);
        }
        
        [Fact]
        public void PrimitiveList_IntToNullableInt_ShouldMap()
        {
            var source = new List<int> { 1, 2 };
            var result = source.MapTo<int?>();
            var dest = new List<int?>(result);
            
            Assert.Equal(2, dest.Count);
            Assert.Equal(1, dest[0]);
            Assert.Equal(2, dest[1]);
        }

        public class  NullableDto { public int? Age { get; set; } }
        public class  StrictDto { public int Age { get; set; } }
    }
}
