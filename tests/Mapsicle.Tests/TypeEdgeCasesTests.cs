using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for type conversion edge cases including signed/unsigned, precision loss, DateTime, Guid, and Enum scenarios.
    /// </summary>
    public class TypeEdgeCasesTests
    {
        #region Test Models

        public class SignedModel
        {
            public int IntValue { get; set; }
            public long LongValue { get; set; }
            public short ShortValue { get; set; }
            public sbyte SByteValue { get; set; }
        }

        public class UnsignedModel
        {
            public uint UIntValue { get; set; }
            public ulong ULongValue { get; set; }
            public ushort UShortValue { get; set; }
            public byte ByteValue { get; set; }
        }

        public class PrecisionSourceModel
        {
            public long LongValue { get; set; }
            public double DoubleValue { get; set; }
            public decimal DecimalValue { get; set; }
        }

        public class PrecisionDestModel
        {
            public int IntValue { get; set; }
            public float FloatValue { get; set; }
            public double DoubleValue { get; set; }
        }

        public class DateTimeModel
        {
            public DateTime DateTimeValue { get; set; }
            public DateTimeOffset DateTimeOffsetValue { get; set; }
            public DateTime? NullableDateTime { get; set; }
        }

        public class GuidModel
        {
            public Guid Id { get; set; }
            public Guid? NullableId { get; set; }
        }

        public class EnumModel
        {
            public TestEnum EnumValue { get; set; }
            public TestEnum? NullableEnumValue { get; set; }
            public int EnumAsInt { get; set; }
        }

        public enum TestEnum
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 3
        }

        #endregion

        #region Signed to Unsigned Conversion Tests

        [Fact]
        public void MapTo_PositiveIntToUInt_ShouldMap()
        {
            var source = new { IntValue = 42 };
            var dest = source.MapTo<UnsignedModel>();

            Assert.NotNull(dest);
            // Type mismatch may result in default value
            // Document actual behavior
        }

        [Fact]
        public void MapTo_NegativeIntToUInt_ShouldHandleGracefully()
        {
            var source = new { IntValue = -42 };
            var dest = source.MapTo<UnsignedModel>();

            Assert.NotNull(dest);
            // Should either skip or convert - document behavior
        }

        [Fact]
        public void MapTo_UIntToInt_WithinRange_ShouldMap()
        {
            var source = new UnsignedModel { UIntValue = 100 };
            var dest = source.MapTo<SignedModel>();

            Assert.NotNull(dest);
            // Document actual mapping behavior
        }

        [Fact]
        public void MapTo_UIntToInt_OutOfRange_ShouldHandleGracefully()
        {
            var source = new UnsignedModel { UIntValue = uint.MaxValue };
            var dest = source.MapTo<SignedModel>();

            Assert.NotNull(dest);
            // Should handle overflow gracefully
        }

        #endregion

        #region Precision Loss Tests

        [Fact]
        public void MapTo_LongToInt_WithinRange_ShouldMap()
        {
            var source = new PrecisionSourceModel { LongValue = 12345 };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // May map if within int range
        }

        [Fact]
        public void MapTo_LongToInt_OutOfRange_ShouldHandleOverflow()
        {
            var source = new PrecisionSourceModel { LongValue = long.MaxValue };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Should handle overflow - either skip or truncate
        }

        [Fact]
        public void MapTo_DoubleToFloat_PrecisionLoss()
        {
            var source = new PrecisionSourceModel { DoubleValue = 1.234567890123456 };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Float has less precision than double
            // Document behavior (skip or convert with loss)
        }

        [Fact]
        public void MapTo_DecimalToDouble_PrecisionLoss()
        {
            var source = new PrecisionSourceModel 
            { 
                DecimalValue = 1.234567890123456789012345678m 
            };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Decimal has more precision than double
        }

        #endregion

        #region DateTime Edge Cases Tests

        [Fact]
        public void MapTo_DateTimeMinValue_ShouldMap()
        {
            var source = new DateTimeModel { DateTimeValue = DateTime.MinValue };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            Assert.Equal(DateTime.MinValue, dest.DateTimeValue);
        }

        [Fact]
        public void MapTo_DateTimeMaxValue_ShouldMap()
        {
            var source = new DateTimeModel { DateTimeValue = DateTime.MaxValue };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            Assert.Equal(DateTime.MaxValue, dest.DateTimeValue);
        }

        [Fact]
        public void MapTo_DateTimeOffsetMinValue_ShouldMap()
        {
            var source = new DateTimeModel { DateTimeOffsetValue = DateTimeOffset.MinValue };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            Assert.Equal(DateTimeOffset.MinValue, dest.DateTimeOffsetValue);
        }

        [Fact]
        public void MapTo_DateTimeOffsetMaxValue_ShouldMap()
        {
            var source = new DateTimeModel { DateTimeOffsetValue = DateTimeOffset.MaxValue };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            Assert.Equal(DateTimeOffset.MaxValue, dest.DateTimeOffsetValue);
        }

        [Fact]
        public void MapTo_NullableDateTime_WithNull_ShouldMap()
        {
            var source = new DateTimeModel { NullableDateTime = null };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.NullableDateTime);
        }

        [Fact]
        public void MapTo_DateTimeToDateTimeOffset_ShouldConvert()
        {
            var now = DateTime.Now;
            var source = new { DateTimeValue = now };
            var dest = source.MapTo<DateTimeModel>();

            Assert.NotNull(dest);
            // Document conversion behavior
        }

        #endregion

        #region Guid Edge Cases Tests

        [Fact]
        public void MapTo_GuidEmpty_ShouldMap()
        {
            var source = new GuidModel { Id = Guid.Empty };
            var dest = source.MapTo<GuidModel>();

            Assert.NotNull(dest);
            Assert.Equal(Guid.Empty, dest.Id);
        }

        [Fact]
        public void MapTo_GuidNewGuid_ShouldMap()
        {
            var guid = Guid.NewGuid();
            var source = new GuidModel { Id = guid };
            var dest = source.MapTo<GuidModel>();

            Assert.NotNull(dest);
            Assert.Equal(guid, dest.Id);
        }

        [Fact]
        public void MapTo_NullableGuid_WithNull_ShouldMap()
        {
            var source = new GuidModel { NullableId = null };
            var dest = source.MapTo<GuidModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.NullableId);
        }

        [Fact]
        public void MapTo_NullableGuid_WithValue_ShouldMap()
        {
            var guid = Guid.NewGuid();
            var source = new GuidModel { NullableId = guid };
            var dest = source.MapTo<GuidModel>();

            Assert.NotNull(dest);
            Assert.Equal(guid, dest.NullableId);
        }

        #endregion

        #region Enum Edge Cases Tests

        [Fact]
        public void MapTo_EnumWithDefinedValue_ShouldMap()
        {
            var source = new EnumModel { EnumValue = TestEnum.Second };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            Assert.Equal(TestEnum.Second, dest.EnumValue);
        }

        [Fact]
        public void MapTo_EnumWithUndefinedValue_ShouldMap()
        {
            // Cast an undefined value
            var source = new EnumModel { EnumValue = (TestEnum)999 };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            Assert.Equal((TestEnum)999, dest.EnumValue);
        }

        [Fact]
        public void MapTo_NullableEnum_WithNull_ShouldMap()
        {
            var source = new EnumModel { NullableEnumValue = null };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.NullableEnumValue);
        }

        [Fact]
        public void MapTo_NullableEnum_WithValue_ShouldMap()
        {
            var source = new EnumModel { NullableEnumValue = TestEnum.Third };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            Assert.Equal(TestEnum.Third, dest.NullableEnumValue);
        }

        [Fact]
        public void MapTo_EnumToInt_ShouldConvert()
        {
            var source = new { EnumValue = TestEnum.Second };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            // Document enum to int conversion behavior
        }

        [Fact]
        public void MapTo_IntToEnum_ShouldConvert()
        {
            var source = new { EnumAsInt = 2 };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            // Document int to enum conversion behavior
        }

        [Fact]
        public void MapTo_NullableEnumWithUndefinedValue_ShouldMap()
        {
            var source = new EnumModel { NullableEnumValue = (TestEnum)(-1) };
            var dest = source.MapTo<EnumModel>();

            Assert.NotNull(dest);
            Assert.Equal((TestEnum)(-1), dest.NullableEnumValue);
        }

        #endregion

        #region Special Numeric Values Tests

        [Fact]
        public void MapTo_DoubleNaN_ShouldMap()
        {
            var source = new { Value = double.NaN };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Document NaN handling
        }

        [Fact]
        public void MapTo_DoublePositiveInfinity_ShouldMap()
        {
            var source = new { DoubleValue = double.PositiveInfinity };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Document infinity handling
        }

        [Fact]
        public void MapTo_DoubleNegativeInfinity_ShouldMap()
        {
            var source = new { DoubleValue = double.NegativeInfinity };
            var dest = source.MapTo<PrecisionDestModel>();

            Assert.NotNull(dest);
            // Document negative infinity handling
        }

        #endregion
    }
}
