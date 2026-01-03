using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for boundary conditions including empty strings, extreme values, and collection edge cases.
    /// </summary>
    public class BoundaryConditionsTests
    {
        #region Test Models

        public class StringModel
        {
            public string Value { get; set; } = string.Empty;
            public string? NullableValue { get; set; }
        }

        public class NumericModel
        {
            public int IntValue { get; set; }
            public long LongValue { get; set; }
            public double DoubleValue { get; set; }
            public float FloatValue { get; set; }
            public decimal DecimalValue { get; set; }
        }

        public class CollectionModel
        {
            public List<int> Items { get; set; } = new();
        }

        public class NestedModel
        {
            public int Level { get; set; }
            public NestedModel? Child { get; set; }
        }

        #endregion

        #region Empty and Whitespace String Tests

        [Fact]
        public void MapTo_EmptyString_ShouldMap()
        {
            var source = new StringModel { Value = string.Empty };
            var dest = source.MapTo<StringModel>();

            Assert.NotNull(dest);
            Assert.Equal(string.Empty, dest.Value);
        }

        [Fact]
        public void MapTo_WhitespaceOnlyString_ShouldMap()
        {
            var source = new StringModel { Value = "   \t\n  " };
            var dest = source.MapTo<StringModel>();

            Assert.NotNull(dest);
            Assert.Equal("   \t\n  ", dest.Value);
        }

        [Fact]
        public void MapTo_NullString_ShouldMapToNull()
        {
            var source = new StringModel { NullableValue = null };
            var dest = source.MapTo<StringModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.NullableValue);
        }

        #endregion

        #region Very Long String Tests

        [Fact]
        public void MapTo_VeryLongString_1MB_ShouldMap()
        {
            // Create a 1MB+ string
            var longString = new string('A', 1024 * 1024);
            var source = new StringModel { Value = longString };
            
            var dest = source.MapTo<StringModel>();

            Assert.NotNull(dest);
            Assert.Equal(longString.Length, dest.Value.Length);
            Assert.Equal(longString, dest.Value);
        }

        [Fact]
        public void MapTo_VeryLongString_10MB_ShouldMap()
        {
            // Create a 10MB string
            var longString = new string('B', 10 * 1024 * 1024);
            var source = new StringModel { Value = longString };
            
            var dest = source.MapTo<StringModel>();

            Assert.NotNull(dest);
            Assert.Equal(longString.Length, dest.Value.Length);
        }

        #endregion

        #region Extreme Numeric Value Tests

        [Fact]
        public void MapTo_IntMaxValue_ShouldMap()
        {
            var source = new NumericModel { IntValue = int.MaxValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(int.MaxValue, dest.IntValue);
        }

        [Fact]
        public void MapTo_IntMinValue_ShouldMap()
        {
            var source = new NumericModel { IntValue = int.MinValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(int.MinValue, dest.IntValue);
        }

        [Fact]
        public void MapTo_LongMaxValue_ShouldMap()
        {
            var source = new NumericModel { LongValue = long.MaxValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(long.MaxValue, dest.LongValue);
        }

        [Fact]
        public void MapTo_LongMinValue_ShouldMap()
        {
            var source = new NumericModel { LongValue = long.MinValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(long.MinValue, dest.LongValue);
        }

        [Fact]
        public void MapTo_DoubleEpsilon_ShouldMap()
        {
            var source = new NumericModel { DoubleValue = double.Epsilon };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(double.Epsilon, dest.DoubleValue);
        }

        [Fact]
        public void MapTo_DoubleMaxValue_ShouldMap()
        {
            var source = new NumericModel { DoubleValue = double.MaxValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(double.MaxValue, dest.DoubleValue);
        }

        [Fact]
        public void MapTo_DoubleMinValue_ShouldMap()
        {
            var source = new NumericModel { DoubleValue = double.MinValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(double.MinValue, dest.DoubleValue);
        }

        [Fact]
        public void MapTo_FloatMaxValue_ShouldMap()
        {
            var source = new NumericModel { FloatValue = float.MaxValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(float.MaxValue, dest.FloatValue);
        }

        [Fact]
        public void MapTo_DecimalMaxValue_ShouldMap()
        {
            var source = new NumericModel { DecimalValue = decimal.MaxValue };
            var dest = source.MapTo<NumericModel>();

            Assert.NotNull(dest);
            Assert.Equal(decimal.MaxValue, dest.DecimalValue);
        }

        #endregion

        #region Empty and Single Item Collection Tests

        [Fact]
        public void MapTo_EmptyList_ShouldMapToEmptyList()
        {
            var source = new CollectionModel { Items = new List<int>() };
            var dest = source.MapTo<CollectionModel>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.Items);
            Assert.Empty(dest.Items);
        }

        [Fact]
        public void MapTo_SingleItemList_ShouldMap()
        {
            var source = new CollectionModel { Items = new List<int> { 42 } };
            var dest = source.MapTo<CollectionModel>();

            Assert.NotNull(dest);
            Assert.Single(dest.Items);
            Assert.Equal(42, dest.Items[0]);
        }

        [Fact]
        public void MapTo_EmptyArray_ShouldMapToEmptyList()
        {
            var source = Array.Empty<int>();
            var dest = source.MapTo<int>();

            Assert.NotNull(dest);
            Assert.Empty(dest);
        }

        #endregion

        #region Nested Object Depth Tests

        [Fact]
        public void MapTo_NestedObjectsMaxDepth32_ShouldMap()
        {
            // Save original max depth
            var originalMaxDepth = Mapper.MaxDepth;
            
            try
            {
                Mapper.MaxDepth = 32;
                
                // Create 32 levels of nesting
                var source = CreateNestedModel(32);
                
                var dest = source.MapTo<NestedModel>();

                Assert.NotNull(dest);
                Assert.Equal(1, dest.Level);
                
                // Verify first few levels
                var current = dest;
                for (int i = 1; i <= 10 && current != null; i++)
                {
                    Assert.Equal(i, current.Level);
                    current = current.Child;
                }
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
            }
        }

        [Fact]
        public void MapTo_NestedObjectsDepthExceedsMaxDepth_ShouldHandleGracefully()
        {
            var originalMaxDepth = Mapper.MaxDepth;
            var logs = new List<string>();
            var originalLogger = Mapper.Logger;
            
            try
            {
                Mapper.MaxDepth = 5;
                Mapper.Logger = msg => logs.Add(msg);
                
                // Create more than 5 levels
                var source = CreateNestedModel(10);
                
                var dest = source.MapTo<NestedModel>();

                // Should not crash, should handle gracefully
                Assert.NotNull(dest);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
                Mapper.Logger = originalLogger;
            }
        }

        #endregion

        #region Helper Methods

        private NestedModel CreateNestedModel(int depth)
        {
            if (depth <= 0)
                return new NestedModel { Level = depth };

            return new NestedModel
            {
                Level = 1,
                Child = CreateNestedModelRecursive(2, depth)
            };
        }

        private NestedModel? CreateNestedModelRecursive(int currentLevel, int maxLevel)
        {
            if (currentLevel > maxLevel)
                return null;

            return new NestedModel
            {
                Level = currentLevel,
                Child = CreateNestedModelRecursive(currentLevel + 1, maxLevel)
            };
        }

        #endregion
    }
}
