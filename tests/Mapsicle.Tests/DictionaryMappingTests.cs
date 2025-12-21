using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for dictionary mapping features.
    /// </summary>
    public class DictionaryMappingTests
    {
        #region Test Models

        public class SimpleEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class EntityWithIgnore
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            [IgnoreMap]
            public string Secret { get; set; } = string.Empty;
        }

        public class EntityWithMapFrom
        {
            public int Id { get; set; }

            [MapFrom("FullName")]
            public string Name { get; set; } = string.Empty;
        }

        #endregion

        [Fact]
        public void ToDictionary_SimpleObject_ShouldConvertAllProperties()
        {
            var entity = new SimpleEntity
            {
                Id = 1,
                Name = "Test",
                Price = 19.99m,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1)
            };

            var dict = entity.ToDictionary();

            Assert.Equal(5, dict.Count);
            Assert.Equal(1, dict["Id"]);
            Assert.Equal("Test", dict["Name"]);
            Assert.Equal(19.99m, dict["Price"]);
            Assert.True((bool)dict["IsActive"]!);
            Assert.Equal(new DateTime(2024, 1, 1), dict["CreatedAt"]);
        }

        [Fact]
        public void ToDictionary_Null_ShouldReturnEmptyDictionary()
        {
            SimpleEntity? entity = null;

            var dict = entity.ToDictionary();

            Assert.NotNull(dict);
            Assert.Empty(dict);
        }

        [Fact]
        public void ToDictionary_ShouldRespectIgnoreMap()
        {
            var entity = new EntityWithIgnore
            {
                Id = 1,
                Name = "Visible",
                Secret = "Hidden"
            };

            var dict = entity.ToDictionary();

            Assert.Equal(2, dict.Count);
            Assert.True(dict.ContainsKey("Id"));
            Assert.True(dict.ContainsKey("Name"));
            Assert.False(dict.ContainsKey("Secret"));
        }

        [Fact]
        public void DictionaryMapTo_ShouldMapToObject()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 42 },
                { "Name", "FromDict" },
                { "Price", 29.99m },
                { "IsActive", true },
                { "CreatedAt", new DateTime(2024, 6, 15) }
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(42, entity.Id);
            Assert.Equal("FromDict", entity.Name);
            Assert.Equal(29.99m, entity.Price);
            Assert.True(entity.IsActive);
            Assert.Equal(new DateTime(2024, 6, 15), entity.CreatedAt);
        }

        [Fact]
        public void DictionaryMapTo_CaseInsensitive_ShouldWork()
        {
            var dict = new Dictionary<string, object?>
            {
                { "id", 1 },
                { "NAME", "CaseTest" },
                { "PRICE", 9.99m }
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(1, entity.Id);
            Assert.Equal("CaseTest", entity.Name);
            Assert.Equal(9.99m, entity.Price);
        }

        [Fact]
        public void DictionaryMapTo_TypeConversion_IntToDecimal()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Price", 100 } // int instead of decimal
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(100m, entity.Price);
        }

        [Fact]
        public void DictionaryMapTo_StringToInt_ShouldConvert()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", "123" } // string instead of int
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(123, entity.Id);
        }

        [Fact]
        public void DictionaryMapTo_Null_ShouldReturnDefault()
        {
            Dictionary<string, object?>? dict = null;

            var entity = dict.MapTo<SimpleEntity>();

            Assert.Null(entity);
        }

        [Fact]
        public void DictionaryMapTo_ShouldRespectMapFrom()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "FullName", "MappedName" } // Uses MapFrom attribute
            };

            var entity = dict.MapTo<EntityWithMapFrom>();

            Assert.NotNull(entity);
            Assert.Equal(1, entity.Id);
            Assert.Equal("MappedName", entity.Name);
        }

        [Fact]
        public void DictionaryMapTo_ExtraKeys_ShouldBeIgnored()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Name", "Test" },
                { "ExtraKey", "ExtraValue" }, // Not on entity
                { "AnotherExtra", 12345 }
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(1, entity.Id);
            Assert.Equal("Test", entity.Name);
        }

        [Fact]
        public void DictionaryMapTo_MissingKeys_ShouldUseDefaults()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 5 }
                // Name, Price, etc. missing
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(5, entity.Id);
            Assert.Equal(string.Empty, entity.Name); // Default
            Assert.Equal(0m, entity.Price); // Default
            Assert.False(entity.IsActive); // Default
        }

        [Fact]
        public void DictionaryMapTo_NullValues_ShouldBeHandled()
        {
            var dict = new Dictionary<string, object?>
            {
                { "Id", 1 },
                { "Name", null }
            };

            var entity = dict.MapTo<SimpleEntity>();

            Assert.NotNull(entity);
            Assert.Equal(1, entity.Id);
            Assert.Equal(string.Empty, entity.Name); // Default because null
        }

        [Fact]
        public void RoundTrip_ObjectToDictToObject_ShouldPreserveData()
        {
            var original = new SimpleEntity
            {
                Id = 999,
                Name = "RoundTrip",
                Price = 123.45m,
                IsActive = true,
                CreatedAt = new DateTime(2024, 12, 25)
            };

            var dict = original.ToDictionary();
            var restored = dict.MapTo<SimpleEntity>();

            Assert.NotNull(restored);
            Assert.Equal(original.Id, restored.Id);
            Assert.Equal(original.Name, restored.Name);
            Assert.Equal(original.Price, restored.Price);
            Assert.Equal(original.IsActive, restored.IsActive);
            Assert.Equal(original.CreatedAt, restored.CreatedAt);
        }
    }
}
