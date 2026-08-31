using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Conversions AutoMapper and Mapperly make that Mapsicle silently did not.
    /// </summary>
    /// <remarks>
    /// Issue #57, found by running twenty scenarios through all three mappers in one process.
    /// Fifteen agreed. These did not, and every one returned a wrong value rather than throwing,
    /// which is the shape that reaches production: a status becomes the enum's zero member, a
    /// timestamp becomes year one, a collection comes back empty.
    ///
    /// The expected values here are what AutoMapper and Mapperly both produced, not what seemed
    /// reasonable.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class ConversionGapTests
    {
        public enum CgColour { None = 0, Red = 1, Blue = 2 }

        public class CgStringSource { public string Colour { get; set; } = "Red"; }
        public class CgEnumDest { public CgColour Colour { get; set; } }

        [Fact]
        public void AStringParsesIntoAnEnum()
        {
            // Returning None means a status of "Shipped" silently becomes the zero member, which is
            // a wrong record rather than an incomplete one.
            Mapper.ClearCache();

            Assert.Equal(CgColour.Red, ((object)new CgStringSource()).MapTo<CgEnumDest>()!.Colour);
        }

        [Fact]
        public void AStringParsesIntoAnEnumIgnoringCase()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgStringSource { Colour = "blue" }).MapTo<CgEnumDest>();

            Assert.Equal(CgColour.Blue, dto!.Colour);
        }

        [Fact]
        public void AStringThatNamesNoMemberYieldsTheDefault()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgStringSource { Colour = "Chartreuse" }).MapTo<CgEnumDest>();

            Assert.Equal(CgColour.None, dto!.Colour);
        }

        [Fact]
        public void ANumericStringParsesIntoAnEnumByValue()
        {
            Mapper.ClearCache();

            Assert.Equal(CgColour.Blue, ((object)new CgStringSource { Colour = "2" }).MapTo<CgEnumDest>()!.Colour);
        }

        [Fact]
        public void ANullStringYieldsTheDefault()
        {
            Mapper.ClearCache();

            Assert.Equal(CgColour.None, ((object)new CgStringSource { Colour = null! }).MapTo<CgEnumDest>()!.Colour);
        }

        public class CgDateSource { public DateTime When { get; set; } = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc); }
        public class CgOffsetDest { public DateTimeOffset When { get; set; } }

        [Fact]
        public void ADateTimeConvertsToADateTimeOffset()
        {
            // It produced DateTimeOffset.MinValue, so the year came back as 1. A timestamp silently
            // becoming year one is the worst of these four.
            Mapper.ClearCache();

            var dto = ((object)new CgDateSource()).MapTo<CgOffsetDest>();

            Assert.Equal(2026, dto!.When.Year);
            Assert.Equal(8, dto.When.Month);
            Assert.Equal(31, dto.When.Day);
        }

        public class CgNullableDateSource { public DateTime? When { get; set; } = new DateTime(2026, 8, 31); }
        public class CgNullableOffsetDest { public DateTimeOffset? When { get; set; } }

        [Fact]
        public void ANullableDateTimeConvertsToANullableOffset()
        {
            Mapper.ClearCache();

            Assert.Equal(2026, ((object)new CgNullableDateSource()).MapTo<CgNullableOffsetDest>()!.When!.Value.Year);
        }

        [Fact]
        public void ANullDateTimeStaysNull()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgNullableDateSource { When = null }).MapTo<CgNullableOffsetDest>();

            Assert.Null(dto!.When);
        }

        public class CgFieldSource { public int Id = 5; public string Name = "field"; }
        public class CgFieldDest { public int Id; public string Name = ""; }

        [Fact]
        public void PublicFieldsAreMapped()
        {
            // Nothing claimed field support and the engine never called GetFields, so this was a
            // deliberate omission rather than a defect. Both competitors map them, and a caller
            // mapping a struct or an interop type hits it with no diagnostic.
            Mapper.ClearCache();

            var dto = ((object)new CgFieldSource()).MapTo<CgFieldDest>();

            Assert.Equal(5, dto!.Id);
            Assert.Equal("field", dto.Name);
        }

        public class CgMixedSource { public int Id { get; set; } = 1; public string Tag = "tag"; }
        public class CgMixedDest { public int Id { get; set; } public string Tag = ""; }

        [Fact]
        public void PropertiesAndFieldsMapTogether()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgMixedSource()).MapTo<CgMixedDest>();

            Assert.Equal(1, dto!.Id);
            Assert.Equal("tag", dto.Tag);
        }

        public class CgReadOnlySource { public List<int> Values { get; set; } = new() { 1, 2 }; }
        public class CgReadOnlyDest { public List<int> Values { get; } = new(); }

        [Fact]
        public void AGetterOnlyCollectionIsFilledInPlace()
        {
            // The standard read model shape for a collection you do not want replaced, and how EF
            // Core entities are usually written. AutoMapper adds into the existing instance.
            Mapper.ClearCache();

            var dto = ((object)new CgReadOnlySource()).MapTo<CgReadOnlyDest>();

            Assert.Equal(2, dto!.Values.Count);
            Assert.Equal(new[] { 1, 2 }, dto.Values);
        }

        public class CgReadOnlyComplexSource { public List<CgItem> Items { get; set; } = new() { new() }; }
        public class CgItem { public string Sku { get; set; } = "s"; }
        public class CgItemDto { public string Sku { get; set; } = ""; }
        public class CgReadOnlyComplexDest { public List<CgItemDto> Items { get; } = new(); }

        [Fact]
        public void AGetterOnlyCollectionOfMappedItemsIsFilled()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgReadOnlyComplexSource()).MapTo<CgReadOnlyComplexDest>();

            Assert.Single(dto!.Items);
            Assert.Equal("s", dto.Items[0].Sku);
        }

        [Fact]
        public void AGetterOnlyCollectionAlreadyHoldingItemsIsNotDuplicated()
        {
            // Mapping twice into the same destination must not append twice. Nothing tested this
            // because nothing filled it at all before.
            Mapper.ClearCache();
            var dest = new CgReadOnlyDest();

            ((object)new CgReadOnlySource()).Map(dest);
            ((object)new CgReadOnlySource()).Map(dest);

            Assert.Equal(2, dest.Values.Count);
        }
        // ---- enum into a different enum type ----------------------------------------------------

        public enum CgSrcColour { Unset = 0, Teal = 1, Amber = 7 }
        public enum CgDstColour { Unset = 0, Amber = 2, Teal = 5 }

        public class CgEnumToEnumSource { public CgSrcColour Colour { get; set; } public CgSrcColour? Maybe { get; set; } }
        public class CgEnumToEnumDest { public CgDstColour Colour { get; set; } public CgDstColour? Maybe { get; set; } }

        public enum CgAligned { Unset = 0, Teal = 1, Amber = 7 }
        public class CgAlignedDest { public CgAligned Colour { get; set; } }

        [Fact]
        public void AnEnumMapsIntoADifferentEnumType()
        {
            // Found by mapping the same order through all three in mapsicle_samples: the source
            // Channel was Mobile and Mapsicle returned Web, the zero member, because the cascade had
            // no enum into enum rule at all and the member fell out of it entirely.
            Mapper.ClearCache();

            var dto = ((object)new CgEnumToEnumSource { Colour = CgSrcColour.Amber }).MapTo<CgEnumToEnumDest>();

            Assert.Equal(CgDstColour.Amber, dto!.Colour);
        }

        [Fact]
        public void AnEnumIsMatchedByNameNotByValue()
        {
            // The two reference mappers disagree here, so this is a choice rather than a copy.
            // AutoMapper 15.1.3 matches by name: Amber(7) becomes Amber(2). Mapperly 4.1.1 matches
            // by value and emits raw 7, which names no member of the destination at all.
            //
            // By name, because the cascade already reads and writes enums by name everywhere else:
            // an enum into a string is ToString, and a string into an enum is a case insensitive
            // Enum.TryParse. Matching by value would mean Amber round tripping through a string
            // arrives as Amber while the direct map arrives as 7, and a mapper that disagrees with
            // itself depending on the route is worse than one that picks the less common rule.
            Mapper.ClearCache();

            var dto = ((object)new CgEnumToEnumSource { Colour = CgSrcColour.Teal }).MapTo<CgEnumToEnumDest>();

            Assert.Equal(CgDstColour.Teal, dto!.Colour);
            Assert.Equal(5, (int)dto.Colour);
        }

        [Fact]
        public void AnEnumWhoseNameIsAbsentYieldsTheDestinationDefault()
        {
            // Never a value the destination enum does not define. An undefined member reaches a
            // switch that has no case for it and a database column that rejects it, and it does so
            // far from the mapping that produced it.
            Mapper.ClearCache();

            var dto = ((object)new CgAlignedSource { Colour = CgOrphan.Missing }).MapTo<CgAlignedDest>();

            Assert.Equal(CgAligned.Unset, dto!.Colour);
        }

        public enum CgOrphan { Missing = 3 }
        public class CgAlignedSource { public CgOrphan Colour { get; set; } }

        [Fact]
        public void ANullableEnumMapsIntoADifferentNullableEnum()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgEnumToEnumSource { Maybe = CgSrcColour.Amber }).MapTo<CgEnumToEnumDest>();

            Assert.Equal(CgDstColour.Amber, dto!.Maybe);
        }

        [Fact]
        public void ANullNullableEnumStaysNull()
        {
            Mapper.ClearCache();

            var dto = ((object)new CgEnumToEnumSource { Maybe = null }).MapTo<CgEnumToEnumDest>();

            Assert.Null(dto!.Maybe);
        }

        // ---- a getter-only collection the engine cannot fill -------------------------------------

        public class CgComputedSource { public List<int> Tags { get; set; } = new() { 1, 2 }; }

        public class CgComputedDest
        {
            public IEnumerable<int> Tags => Compute();

            private static IEnumerable<int> Compute() { yield return 9; }
        }

        public class CgBackedDest
        {
            private readonly List<int> _tags = new();

            public IEnumerable<int> Tags => _tags;
        }

        [Fact]
        public void AGetterOnlyCollectionThatCannotBeFilledIsSkippedRatherThanThrown()
        {
            // The eligibility test can only see the declared type, and a member declared
            // IEnumerable<T> can return anything. A computed getter returns an iterator, which is
            // not an ICollection<T>, and the hard cast threw InvalidCastException from inside the
            // compiled delegate. Section 6 says a value of the wrong shape is dropped, never thrown.
            Mapper.ClearCache();

            var dto = ((object)new CgComputedSource()).MapTo<CgComputedDest>();

            Assert.NotNull(dto);
            Assert.Equal(new[] { 9 }, dto!.Tags);
        }

        [Fact]
        public void AGetterOnlyCollectionThatCanBeFilledStillIs()
        {
            // The positive control. Making the cast safe must not turn the working case into a skip.
            Mapper.ClearCache();

            var dto = ((object)new CgComputedSource()).MapTo<CgBackedDest>();

            Assert.Equal(new[] { 1, 2 }, dto!.Tags);
        }

    }
}
