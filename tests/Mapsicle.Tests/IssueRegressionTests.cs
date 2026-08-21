using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// One test per defect fixed in 1.3.0, named by issue number so a fix can be traced to its proof.
    /// </summary>
    /// <remarks>
    /// Every test here failed before its fix. Each carries the observed wrong behaviour in a comment,
    /// because "it returns 42 now" is far less useful to a reader than "it returned 0, silently".
    ///
    /// Type names are unique to this file on purpose. Compiled mappers are cached in static fields
    /// keyed by (source runtime type, destination type), so a test reusing a pair another test
    /// already mapped would exercise a delegate compiled earlier and prove nothing about the code
    /// under test. That is also why the class joins the StaticMapperTests collection: it touches
    /// static mapper state and must not run beside another class that does.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class IssueRegressionTests
    {
        // ---- Issue 2: a null reference-typed source mapped to a string destination -------------
        // Threw NullReferenceException from inside the compiled delegate, on all three entry points.

        [Fact]
        public void Issue2_UntypedMapTo_NullReferenceProperty_ToString_DoesNotThrow()
        {
            Mapper.ClearCache();

            var dest = new Issue2Source { Website = null }.MapTo<Issue2Dest>();

            Assert.NotNull(dest);
            Assert.Null(dest!.Website);
        }

        [Fact]
        public void Issue2_TypedMapTo_NullReferenceProperty_ToString_DoesNotThrow()
        {
            Mapper.ClearCache();

            var dest = new Issue2Source { Website = null }.MapTo<Issue2Source, Issue2Dest>();

            Assert.NotNull(dest);
            Assert.Null(dest!.Website);
        }

        [Fact]
        public void Issue2_MapperInstance_NullReferenceProperty_ToString_DoesNotThrow()
        {
            using var mapper = MapperFactory.Create();

            var dest = mapper.MapTo<Issue2Dest>(new Issue2Source { Website = null });

            Assert.NotNull(dest);
            Assert.Null(dest!.Website);
        }

        // The control. Without it, a fix that returns null unconditionally passes all three above.
        [Fact]
        public void Issue2_NonNullReferenceProperty_StillConvertsToString()
        {
            Mapper.ClearCache();

            var dest = new Issue2Source { Website = new Uri("https://baryo.dev/") }.MapTo<Issue2Dest>();

            Assert.Equal("https://baryo.dev/", dest!.Website);
        }

        // ---- Issue 5: widening numeric conversions ---------------------------------------------
        // int 42 mapped to a long produced 0. IsAssignableFrom is false for every widening pair, so
        // they fell out of the cascade and the destination kept its default, with nothing logged.

        [Fact]
        public void Issue5_IntToLong_KeepsTheValue()
        {
            Mapper.ClearCache();

            var dest = new Issue5IntSource { Value = 42 }.MapTo<Issue5LongDest>();

            Assert.Equal(42L, dest!.Value);
        }

        [Fact]
        public void Issue5_IntToDecimal_KeepsTheValue()
        {
            Mapper.ClearCache();

            var dest = new Issue5IntSource { Value = 42 }.MapTo<Issue5DecimalDest>();

            Assert.Equal(42m, dest!.Value);
        }

        [Fact]
        public void Issue5_DecimalToDouble_KeepsTheValue()
        {
            Mapper.ClearCache();

            var dest = new Issue5DecimalSource { Value = 42.5m }.MapTo<Issue5DoubleDest>();

            Assert.Equal(42.5, dest!.Value, 10);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void Issue5_IntToLong_AtTheBoundaries(int value)
        {
            Mapper.ClearCache();

            var dest = new Issue5IntSource { Value = value }.MapTo<Issue5LongDest>();

            Assert.Equal((long)value, dest!.Value);
        }

        [Fact]
        public void Issue5_NullableSourceWithValue_Widens()
        {
            Mapper.ClearCache();

            var dest = new Issue5NullableIntSource { Value = 7 }.MapTo<Issue5LongDest>();

            Assert.Equal(7L, dest!.Value);
        }

        [Fact]
        public void Issue5_NullableSourceWithoutValue_LeavesTheDefault()
        {
            Mapper.ClearCache();

            var dest = new Issue5NullableIntSource { Value = null }.MapTo<Issue5LongDest>();

            Assert.Equal(0L, dest!.Value);
        }

        // Narrowing must stay unmapped. This is the guard against a fix that widens the rule until
        // long.MaxValue silently becomes -1 in an int.
        [Fact]
        public void Issue5_NarrowingLongToInt_StaysUnmapped()
        {
            Mapper.ClearCache();

            var dest = new Issue5LongSource { Value = long.MaxValue }.MapTo<Issue5IntDest>();

            Assert.Equal(0, dest!.Value);
        }

        // double to decimal throws OverflowException outside decimal's range, so it is deliberately
        // not in the widening table. A mapper turning a data difference into an exception at map
        // time would be worse than leaving the property alone.
        [Fact]
        public void Issue5_DoubleToDecimal_StaysUnmapped_RatherThanRiskingOverflow()
        {
            Mapper.ClearCache();

            var dest = new Issue5DoubleSource { Value = double.MaxValue }.MapTo<Issue5DecimalDest>();

            Assert.Equal(0m, dest!.Value);
        }

        // ---- Issue 6: a collection whose items have different runtime types ---------------------
        // Threw InvalidCastException: the delegate is compiled for the first item's runtime type and
        // its first instruction is a cast to that type.

        [Fact]
        public void Issue6_MixedRuntimeTypes_MapsEveryItem()
        {
            Mapper.ClearCache();

            var animals = new List<Issue6Animal>
            {
                new Issue6Dog { Id = 1, Name = "Rex" },
                new Issue6Cat { Id = 2, Name = "Tom" },
                new Issue6Dog { Id = 3, Name = "Fido" },
            };

            var dtos = animals.MapTo<Issue6Dto>();

            Assert.Equal(3, dtos.Count);
            Assert.Equal(new[] { "Rex", "Tom", "Fido" }, dtos.Select(d => d.Name));
            Assert.Equal(new[] { 1, 2, 3 }, dtos.Select(d => d.Id));
        }

        [Fact]
        public void Issue6_MapToArray_MixedRuntimeTypes_MapsEveryItem()
        {
            Mapper.ClearCache();

            var animals = new List<Issue6Animal>
            {
                new Issue6Dog { Id = 1, Name = "Rex" },
                new Issue6Cat { Id = 2, Name = "Tom" },
            };

            var dtos = animals.MapToArray<Issue6Dto>();

            Assert.Equal(2, dtos.Length);
            Assert.Equal("Tom", dtos[1].Name);
        }

        [Fact]
        public void Issue6_MapperInstance_MixedRuntimeTypes_MapsEveryItem()
        {
            using var mapper = MapperFactory.Create();

            var animals = new List<Issue6Animal>
            {
                new Issue6Dog { Id = 1, Name = "Rex" },
                new Issue6Cat { Id = 2, Name = "Tom" },
            };

            var dtos = mapper.MapTo<Issue6Dto>(animals);

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Tom", dtos[1].Name);
        }

        // Nulls interleaved with a type change, because the two code paths interact.
        [Fact]
        public void Issue6_MixedRuntimeTypesWithNulls_KeepsPositions()
        {
            Mapper.ClearCache();

            var animals = new List<Issue6Animal?>
            {
                new Issue6Dog { Id = 1, Name = "Rex" },
                null,
                new Issue6Cat { Id = 3, Name = "Tom" },
            };

            var dtos = animals.MapTo<Issue6Dto>();

            Assert.Equal(3, dtos.Count);
            Assert.Null(dtos[1]);
            Assert.Equal("Tom", dtos[2].Name);
        }

        // ---- Issue 3: AssertMappingValid disagreed with the mapper ------------------------------
        // Passed for a destination the mapper never populates, because the validator restated the
        // flattening rule instead of asking for it: Name is a prefix of NameLength and string has a
        // Length property, so the validator saw a flattening the mapper skips.

        [Fact]
        public void Issue3_ValidatorReportsWhatTheMapperActuallySkips()
        {
            Mapper.ClearCache();

            var unmapped = Mapper.GetUnmappedProperties<Issue3Source, Issue3Dest>();

            Assert.Contains(nameof(Issue3Dest.NameLength), unmapped);
        }

        [Fact]
        public void Issue3_AssertMappingValid_ThrowsForThePropertyTheMapperSkips()
        {
            Mapper.ClearCache();

            Assert.Throws<InvalidOperationException>(() => Mapper.AssertMappingValid<Issue3Source, Issue3Dest>());
        }

        // The agreement, stated directly: whatever the validator calls unmapped is exactly what the
        // mapper leaves at its default. This is the property that was violated, so it is asserted
        // rather than implied.
        [Fact]
        public void Issue3_UnmappedReportMatchesWhatTheMapperLeavesAtDefault()
        {
            Mapper.ClearCache();

            var unmapped = Mapper.GetUnmappedProperties<Issue3Source, Issue3Dest>();
            var mapped = new Issue3Source { Name = "abcdef" }.MapTo<Issue3Dest>();

            if (unmapped.Contains(nameof(Issue3Dest.NameLength)))
            {
                Assert.Equal(0, mapped!.NameLength);
            }
            else
            {
                Assert.Equal(6, mapped!.NameLength);
            }
        }

        // Real flattening must still validate and still map, or the fix above is just "report
        // everything as unmapped".
        [Fact]
        public void Issue3_GenuineFlatteningStillValidatesAndMaps()
        {
            Mapper.ClearCache();

            var unmapped = Mapper.GetUnmappedProperties<Issue3FlatSource, Issue3FlatDest>();
            Assert.DoesNotContain(nameof(Issue3FlatDest.AddressCity), unmapped);

            var dest = new Issue3FlatSource { Address = new Issue3Address { City = "Koronadal" } }
                .MapTo<Issue3FlatDest>();

            Assert.Equal("Koronadal", dest!.AddressCity);
        }

        // ---- Issue 7: the strongly-typed cache was invisible to cache management ----------------
        // CacheInfo reported 0 with a compiled mapper cached, ClearCache did not clear it, and
        // MaxCacheSize never bounded it.

        [Fact]
        public void Issue7_CacheInfo_CountsATypedMapper()
        {
            Mapper.ClearCache();
            var before = Mapper.CacheInfo().Total;

            _ = new Issue7Source { Value = 1 }.MapTo<Issue7Source, Issue7Dest>();

            Assert.True(Mapper.CacheInfo().Total > before,
                $"CacheInfo reported {Mapper.CacheInfo().Total} after compiling a typed mapper");
        }

        [Fact]
        public void Issue7_ClearCache_ClearsTheTypedMapper()
        {
            Mapper.ClearCache();
            _ = new Issue7ClearSource { Value = 1 }.MapTo<Issue7ClearSource, Issue7Dest>();
            Assert.True(Mapper.CacheInfo().Total > 0);

            Mapper.ClearCache();

            Assert.Equal(0, Mapper.CacheInfo().Total);
        }

        // Clearing must not break the mapper, only the cache. A fix that nulls the entry without
        // recompiling on next use would return default here.
        [Fact]
        public void Issue7_MappingStillWorksAfterTheTypedCacheIsCleared()
        {
            Mapper.ClearCache();
            _ = new Issue7Source { Value = 1 }.MapTo<Issue7Source, Issue7Dest>();
            Mapper.ClearCache();

            var dest = new Issue7Source { Value = 99 }.MapTo<Issue7Source, Issue7Dest>();

            Assert.Equal(99, dest!.Value);
        }

        // ---- Issue 4: the recursive MapTo overload was chosen by reflection ordering ------------
        // Three public overloads match the old GetMethods().First(...) predicate and GetMethods()
        // does not guarantee order. Nested mapping working was luck, not design.

        [Fact]
        public void Issue4_NestedObjectMapping_ResolvesTheObjectOverload()
        {
            Mapper.ClearCache();

            var dest = new Issue4Source { Inner = new Issue4Inner { Value = 5 } }.MapTo<Issue4Dest>();

            Assert.NotNull(dest!.Inner);
            Assert.Equal(5, dest.Inner!.Value);
        }

        [Fact]
        public void Issue4_NestedObjectMapping_ThroughAMapperInstance()
        {
            using var mapper = MapperFactory.Create();

            var dest = mapper.MapTo<Issue4Dest>(new Issue4Source { Inner = new Issue4Inner { Value = 7 } });

            Assert.NotNull(dest!.Inner);
            Assert.Equal(7, dest.Inner!.Value);
        }

        #region Types, unique to this file so no test reuses another's compiled delegate

        public class Issue2Source { public Uri? Website { get; set; } }
        public class Issue2Dest { public string? Website { get; set; } }

        public class Issue5IntSource { public int Value { get; set; } }
        public class Issue5NullableIntSource { public int? Value { get; set; } }
        public class Issue5LongSource { public long Value { get; set; } }
        public class Issue5DecimalSource { public decimal Value { get; set; } }
        public class Issue5DoubleSource { public double Value { get; set; } }
        public class Issue5LongDest { public long Value { get; set; } }
        public class Issue5IntDest { public int Value { get; set; } }
        public class Issue5DecimalDest { public decimal Value { get; set; } }
        public class Issue5DoubleDest { public double Value { get; set; } }

        public class Issue6Animal { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class Issue6Dog : Issue6Animal { public bool Barks { get; set; } }
        public class Issue6Cat : Issue6Animal { public bool Meows { get; set; } }
        public class Issue6Dto { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class Issue3Source { public string Name { get; set; } = ""; }
        public class Issue3Dest { public int NameLength { get; set; } }
        public class Issue3Address { public string City { get; set; } = ""; }
        public class Issue3FlatSource { public Issue3Address? Address { get; set; } }
        public class Issue3FlatDest { public string AddressCity { get; set; } = ""; }

        public class Issue7Source { public int Value { get; set; } }
        public class Issue7ClearSource { public int Value { get; set; } }
        public class Issue7Dest { public int Value { get; set; } }

        public class Issue4Inner { public int Value { get; set; } }
        public class Issue4Source { public Issue4Inner? Inner { get; set; } }
        public class Issue4Dest { public Issue4Inner? Inner { get; set; } }

        #endregion
    }
}
