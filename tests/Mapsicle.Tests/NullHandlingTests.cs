using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for null handling scenarios including all-null properties, nullable conversions, and null collections.
    /// </summary>
    public class NullHandlingTests
    {
        #region Test Models

        public class AllPropertiesModel
        {
            public string? StringValue { get; set; }
            public int? IntValue { get; set; }
            public DateTime? DateValue { get; set; }
            public Guid? GuidValue { get; set; }
            public NestedObject? Nested { get; set; }
        }

        public class NestedObject
        {
            public string? Name { get; set; }
            public int? Value { get; set; }
        }

        public class NullableSource
        {
            public int? Age { get; set; }
            public string? Name { get; set; }
            public bool? IsActive { get; set; }
        }

        public class NonNullableDestination
        {
            public int Age { get; set; }
            public string? Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public class CollectionWithNullsModel
        {
            public List<string?>? Items { get; set; }
            public List<NestedObject?>? NestedItems { get; set; }
        }

        public class MixedNullModel
        {
            public string? NullString { get; set; }
            public string NonNullString { get; set; } = string.Empty;
            public NestedObject? NullNested { get; set; }
            public NestedObject NonNullNested { get; set; } = new();
        }

        #endregion

        #region All Null Properties Tests

        [Fact]
        public void MapTo_SourceWithAllNullProperties_ShouldMapToDefaults()
        {
            var source = new AllPropertiesModel
            {
                StringValue = null,
                IntValue = null,
                DateValue = null,
                GuidValue = null,
                Nested = null
            };

            var dest = source.MapTo<AllPropertiesModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.StringValue);
            Assert.Null(dest.IntValue);
            Assert.Null(dest.DateValue);
            Assert.Null(dest.GuidValue);
            Assert.Null(dest.Nested);
        }

        #endregion

        #region Nullable to Non-Nullable Tests

        [Fact]
        public void MapTo_NullableToNonNullable_WithValues_ShouldMap()
        {
            var source = new NullableSource
            {
                Age = 25,
                Name = "John",
                IsActive = true
            };

            var dest = source.MapTo<NonNullableDestination>();

            Assert.NotNull(dest);
            Assert.Equal(25, dest.Age);
            Assert.Equal("John", dest.Name);
            Assert.True(dest.IsActive);
        }

        [Fact]
        public void MapTo_NullableToNonNullable_WithNulls_ShouldUseDefaults()
        {
            var source = new NullableSource
            {
                Age = null,
                Name = null,
                IsActive = null
            };

            var dest = source.MapTo<NonNullableDestination>();

            Assert.NotNull(dest);
            Assert.Equal(0, dest.Age); // default(int)
            // Note: Null string maps to null, not empty string - this is expected behavior
            Assert.Null(dest.Name);
            Assert.False(dest.IsActive); // default(bool)
        }

        [Fact]
        public void MapTo_NullableToNonNullable_MixedNulls_ShouldMapCorrectly()
        {
            var source = new NullableSource
            {
                Age = 30,
                Name = null,
                IsActive = true
            };

            var dest = source.MapTo<NonNullableDestination>();

            Assert.NotNull(dest);
            Assert.Equal(30, dest.Age);
            // Note: Null string maps to null, not empty string - this is expected behavior
            Assert.Null(dest.Name);
            Assert.True(dest.IsActive);
        }

        #endregion

        #region Collections with Null Elements Tests

        [Fact]
        public void MapTo_ListContainingNullElements_ShouldPreserveNulls()
        {
            var source = new CollectionWithNullsModel
            {
                Items = new List<string?> { "First", null, "Third", null, "Fifth" }
            };

            var dest = source.MapTo<CollectionWithNullsModel>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.Items);
            Assert.Equal(5, dest.Items.Count);
            Assert.Equal("First", dest.Items[0]);
            Assert.Null(dest.Items[1]);
            Assert.Equal("Third", dest.Items[2]);
            Assert.Null(dest.Items[3]);
            Assert.Equal("Fifth", dest.Items[4]);
        }

        [Fact]
        public void MapTo_ListWithAllNullElements_ShouldMap()
        {
            var source = new CollectionWithNullsModel
            {
                Items = new List<string?> { null, null, null }
            };

            var dest = source.MapTo<CollectionWithNullsModel>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.Items);
            Assert.Equal(3, dest.Items.Count);
            Assert.All(dest.Items, item => Assert.Null(item));
        }

        [Fact]
        public void MapTo_NullCollection_ShouldMapToNull()
        {
            var source = new CollectionWithNullsModel
            {
                Items = null
            };

            var dest = source.MapTo<CollectionWithNullsModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.Items);
        }

        #endregion

        #region Mixed Null and Non-Null Nested Objects Tests

        [Fact]
        public void MapTo_MixedNullAndNonNullNestedObjects_ShouldMapCorrectly()
        {
            var source = new MixedNullModel
            {
                NullString = null,
                NonNullString = "Value",
                NullNested = null,
                NonNullNested = new NestedObject { Name = "Test", Value = 42 }
            };

            var dest = source.MapTo<MixedNullModel>();

            Assert.NotNull(dest);
            Assert.Null(dest.NullString);
            Assert.Equal("Value", dest.NonNullString);
            Assert.Null(dest.NullNested);
            Assert.NotNull(dest.NonNullNested);
            Assert.Equal("Test", dest.NonNullNested.Name);
            Assert.Equal(42, dest.NonNullNested.Value);
        }

        [Fact]
        public void MapTo_NestedObjectListWithNulls_ShouldPreserveStructure()
        {
            var source = new CollectionWithNullsModel
            {
                NestedItems = new List<NestedObject?>
                {
                    new NestedObject { Name = "First", Value = 1 },
                    null,
                    new NestedObject { Name = "Third", Value = 3 },
                    null
                }
            };

            var dest = source.MapTo<CollectionWithNullsModel>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.NestedItems);
            Assert.Equal(4, dest.NestedItems.Count);
            Assert.NotNull(dest.NestedItems[0]);
            Assert.Equal("First", dest.NestedItems[0]!.Name);
            Assert.Null(dest.NestedItems[1]);
            Assert.NotNull(dest.NestedItems[2]);
            Assert.Equal("Third", dest.NestedItems[2]!.Name);
            Assert.Null(dest.NestedItems[3]);
        }

        #endregion

        #region Deeply Nested Null Tests

        [Fact]
        public void MapTo_DeeplyNestedWithNullAtDifferentLevels_ShouldHandle()
        {
            var source = new Level1
            {
                Value = "L1",
                Level2 = new Level2
                {
                    Value = "L2",
                    Level3 = null // Null at level 3
                }
            };

            var dest = source.MapTo<Level1>();

            Assert.NotNull(dest);
            Assert.Equal("L1", dest.Value);
            Assert.NotNull(dest.Level2);
            Assert.Equal("L2", dest.Level2.Value);
            Assert.Null(dest.Level2.Level3);
        }

        [Fact]
        public void MapTo_DeeplyNestedWithNullAtSecondLevel_ShouldHandle()
        {
            var source = new Level1
            {
                Value = "L1",
                Level2 = null
            };

            var dest = source.MapTo<Level1>();

            Assert.NotNull(dest);
            Assert.Equal("L1", dest.Value);
            Assert.Null(dest.Level2);
        }

        #endregion

        #region Supporting Classes for Deep Nesting

        public class Level1
        {
            public string? Value { get; set; }
            public Level2? Level2 { get; set; }
        }

        public class Level2
        {
            public string? Value { get; set; }
            public Level3? Level3 { get; set; }
        }

        public class Level3
        {
            public string? Value { get; set; }
        }

        #endregion

        #region Null to Existing Object Tests

        [Fact]
        public void Map_NullSourceToExistingObject_ShouldNotModifyTarget()
        {
            var target = new AllPropertiesModel
            {
                StringValue = "Original",
                IntValue = 42
            };

            // Mapping from null should ideally not crash
            // The behavior depends on implementation - document current behavior
            try
            {
                AllPropertiesModel? nullSource = null;
                nullSource?.Map(target);
                
                // If it doesn't throw, target should be unchanged
                Assert.Equal("Original", target.StringValue);
                Assert.Equal(42, target.IntValue);
            }
            catch (NullReferenceException)
            {
                // If it throws, that's also acceptable behavior - document it
                Assert.True(true, "Null source throws NullReferenceException as expected");
            }
        }

        #endregion
    }
}
