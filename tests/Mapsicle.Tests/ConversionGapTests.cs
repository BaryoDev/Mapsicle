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
    }
}
