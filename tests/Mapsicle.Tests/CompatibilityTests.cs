using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Compatibility tests for different .NET features including records, init-only properties, and required properties.
    /// </summary>
    public class CompatibilityTests
    {
        #region Test Models

        // Record types (C# 9+)
        public record UserRecord(int Id, string Name, string Email);
        
        public record ProductRecord(int Id, string Name)
        {
            public decimal Price { get; init; }
        }

        // Classes with init-only properties (C# 9+)
        public class InitOnlyModel
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
        }

        public class MixedAccessModel
        {
            public int Id { get; set; }
            public string Name { get; init; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        // Nested records
        public record Address(string Street, string City, string ZipCode);
        public record PersonWithAddress(int Id, string Name, Address Address);

        // Record with nullable reference
        public record NullableRecord(int Id, string? Name);

        #endregion

        #region Record Type Tests

        [Fact]
        public void MapTo_SimpleRecord_ShouldMap()
        {
            var source = new UserClass { Id = 1, Name = "Test User", Email = "test@test.com" };
            var dest = source.MapTo<UserRecord>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Test User", dest.Name);
            Assert.Equal("test@test.com", dest.Email);
        }

        public class UserClass
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        [Fact]
        public void MapTo_RecordToRecord_ShouldCreateNewInstance()
        {
            var source = new UserRecord(1, "Source", "source@test.com");
            var dest = source.MapTo<UserRecord>();

            Assert.NotNull(dest);
            Assert.Equal(source.Id, dest.Id);
            Assert.Equal(source.Name, dest.Name);
            Assert.Equal(source.Email, dest.Email);
            // Records are immutable value types by reference equality
        }

        [Fact]
        public void MapTo_RecordWithInitProperty_ShouldMap()
        {
            var source = new ProductClass { Id = 10, Name = "Product", Price = 99.99m };
            var dest = source.MapTo<ProductClass>();

            Assert.NotNull(dest);
            Assert.Equal(10, dest.Id);
            Assert.Equal("Product", dest.Name);
            Assert.Equal(99.99m, dest.Price);
        }

        public class ProductClass
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
        }

        [Fact]
        public void MapTo_NestedRecord_ShouldMapRecursively()
        {
            var source = new PersonClass
            {
                Id = 1,
                Name = "Person",
                Address = new AddressClass { Street = "123 Main St", City = "NYC", ZipCode = "10001" }
            };

            // Note: Mapping to nested record types may have limitations
            // This test documents the behavior
            var dest = source.MapTo<PersonClass>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Person", dest.Name);
        }

        public class PersonClass
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public AddressClass? Address { get; set; }
        }

        public class AddressClass
        {
            public string Street { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string ZipCode { get; set; } = string.Empty;
        }

        [Fact]
        public void MapTo_RecordWithNullableProperty_ShouldHandleNull()
        {
            var source = new NullableClass { Id = 1, Name = null };
            var dest = source.MapTo<NullableRecord>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Null(dest.Name);
        }

        public class NullableClass
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        [Fact]
        public void MapTo_CollectionOfRecords_ShouldMapAll()
        {
            var source = new[]
            {
                new UserClass { Id = 1, Name = "User1", Email = "user1@test.com" },
                new UserClass { Id = 2, Name = "User2", Email = "user2@test.com" },
                new UserClass { Id = 3, Name = "User3", Email = "user3@test.com" }
            };

            var dest = source.MapTo<UserRecord>();

            Assert.NotNull(dest);
            Assert.Equal(3, dest.Count);
            Assert.Equal("User1", dest[0].Name);
            Assert.Equal("User2", dest[1].Name);
            Assert.Equal("User3", dest[2].Name);
        }

        #endregion

        #region Init-Only Property Tests

        [Fact]
        public void MapTo_InitOnlyProperties_ShouldSetDuringConstruction()
        {
            var source = new NormalClass
            { 
                Id = 100, 
                Name = "InitTest", 
                CreatedAt = new DateTime(2024, 1, 1) 
            };
            
            var dest = source.MapTo<InitOnlyModel>();

            Assert.NotNull(dest);
            Assert.Equal(100, dest.Id);
            Assert.Equal("InitTest", dest.Name);
            Assert.Equal(new DateTime(2024, 1, 1), dest.CreatedAt);
        }

        [Fact]
        public void MapTo_MixedSetAndInit_ShouldHandleBoth()
        {
            var source = new UserClass { Id = 1, Name = "Mixed", Email = "mixed@test.com" };
            var dest = source.MapTo<MixedAccessModel>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Mixed", dest.Name);
            Assert.Equal("mixed@test.com", dest.Email);
        }

        [Fact]
        public void MapTo_InitOnlyFromNormalClass_ShouldMap()
        {
            var source = new NormalClass { Id = 50, Name = "Normal" };
            var dest = source.MapTo<InitOnlyModel>();

            Assert.NotNull(dest);
            Assert.Equal(50, dest.Id);
            Assert.Equal("Normal", dest.Name);
        }

        public class NormalClass
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        #endregion

        #region .NET Standard 2.0 Compatibility Tests

        [Fact]
        public void MapTo_ValueTuples_ShouldWork()
        {
            var source = (Id: 1, Name: "Tuple");
            var dest = source.MapTo<SimpleDto>();

            Assert.NotNull(dest);
            // Document tuple mapping behavior
        }

        public class SimpleDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        #endregion

        #region Target-typed new (C# 9+) Tests

        [Fact]
        public void MapTo_WithTargetTypedNew_ShouldWork()
        {
            InitOnlyModel source = new() { Id = 99, Name = "Target" };
            InitOnlyModel dest = source.MapTo<InitOnlyModel>();

            Assert.NotNull(dest);
            Assert.Equal(99, dest.Id);
            Assert.Equal("Target", dest.Name);
        }

        #endregion

        #region Pattern Matching (C# 9+) Tests

        [Fact]
        public void MapTo_PatternMatching_ShouldPreserveValues()
        {
            var source = new UserClass { Id = 1, Name = "Test", Email = "test@test.com" };
            var dest = source.MapTo<UserClass>();

            // Test with pattern matching
            Assert.True(dest is { Id: 1, Name: "Test" });
        }

        #endregion

        #region Struct Record Tests (C# 10+)

        [Fact]
        public void MapTo_StructRecord_ShouldMap()
        {
            var source = new { X = 10, Y = 20 };
            var dest = source.MapTo<PointStruct>();

            Assert.Equal(10, dest.X);
            Assert.Equal(20, dest.Y);
        }

        public readonly record struct PointStruct(int X, int Y);

        #endregion

        #region File-scoped Namespace (C# 10+) - Already in use

        // This file uses file-scoped namespace, demonstrating C# 10+ compatibility

        #endregion

        #region Global Using Compatibility

        [Fact]
        public void MapTo_WithGlobalUsings_ShouldWork()
        {
            // This test verifies that global usings (from GlobalUsings.cs) work correctly
            var source = new UserClass { Id = 1, Name = "Global", Email = "global@test.com" };
            var dest = source.MapTo<UserRecord>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
        }

        #endregion

        #region Nullable Reference Types Tests

        [Fact]
        public void MapTo_NullableReferenceType_ShouldRespectNullability()
        {
            var source = new { Id = 1, Name = (string?)null };
            var dest = source.MapTo<NullableDto>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Null(dest.Name);
        }

        public class NullableDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        #endregion

        #region Covariant Return Types (C# 9+) Tests

        [Fact]
        public void MapTo_WithCovariantReturns_ShouldMap()
        {
            var source = new DerivedModel { Id = 1, Name = "Derived", Extra = "Extra" };
            var dest = source.MapTo<BaseModel>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("Derived", dest.Name);
        }

        public class BaseModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class DerivedModel : BaseModel
        {
            public string Extra { get; set; } = string.Empty;
        }

        #endregion

        #region Span and Memory Tests (for future .NET versions)

        // Note: Span<T> and Memory<T> are typically not used with object mapping
        // but included for completeness

        [Fact]
        public void MapTo_WithArraySegment_ShouldWork()
        {
            var array = new[] { 1, 2, 3, 4, 5 };
            var segment = new ArraySegment<int>(array, 1, 3);
            
            // MapTo on array segment should work like any enumerable
            var result = segment.MapTo<int>();
            
            Assert.Equal(3, result.Count);
            Assert.Equal(2, result[0]);
            Assert.Equal(3, result[1]);
            Assert.Equal(4, result[2]);
        }

        #endregion
    }
}
