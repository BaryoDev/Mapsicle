using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for attribute-based configuration: [IgnoreMap] and [MapFrom]
    /// </summary>
    public class AttributeTests
    {
        #region Test Models

        public class UserEntity
        {
            public int Id { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        public class UserDto
        {
            public int Id { get; set; }

            [MapFrom("UserName")]
            public string Name { get; set; } = string.Empty;

            [IgnoreMap]
            public string Password { get; set; } = "NOT_MAPPED";

            public string Email { get; set; } = string.Empty;

            [IgnoreMap]
            public string InternalField { get; set; } = "INTERNAL";
        }

        public class ProductEntity
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public decimal UnitPrice { get; set; }
        }

        public class ProductDto
        {
            [MapFrom("ProductId")]
            public int Id { get; set; }

            [MapFrom("ProductName")]
            public string Name { get; set; } = string.Empty;

            [MapFrom("UnitPrice")]
            public string Price { get; set; } = string.Empty; // decimal -> string coercion
        }

        public class AuditEntity
        {
            public int Id { get; set; }
            public string Action { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        public class PublicAuditDto
        {
            public int Id { get; set; }
            public string Action { get; set; } = string.Empty;

            [IgnoreMap]
            public string IpAddress { get; set; } = "HIDDEN";

            [IgnoreMap]
            public DateTime Timestamp { get; set; }
        }

        #endregion

        [Fact]
        public void IgnoreMap_ShouldNotMapMarkedProperty()
        {
            var source = new UserEntity
            {
                Id = 1,
                UserName = "alice",
                Password = "secret123",
                Email = "alice@test.com",
                CreatedAt = DateTime.Now
            };

            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal(1, dest.Id);
            Assert.Equal("NOT_MAPPED", dest.Password); // Should retain default, not mapped
            Assert.Equal("alice@test.com", dest.Email);
            Assert.Equal("INTERNAL", dest.InternalField); // Not on source, stays default
        }

        [Fact]
        public void MapFrom_ShouldMapFromSpecifiedProperty()
        {
            var source = new UserEntity
            {
                Id = 1,
                UserName = "bob",
                Email = "bob@test.com"
            };

            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal("bob", dest.Name); // Mapped from UserName
        }

        [Fact]
        public void MapFrom_WithTypeCoercion_ShouldWork()
        {
            var source = new ProductEntity
            {
                ProductId = 42,
                ProductName = "Widget",
                UnitPrice = 19.99m
            };

            var dest = source.MapTo<ProductDto>();

            Assert.NotNull(dest);
            Assert.Equal(42, dest.Id);
            Assert.Equal("Widget", dest.Name);
            Assert.Equal("19.99", dest.Price); // decimal -> string
        }

        [Fact]
        public void IgnoreMap_ShouldHideSensitiveData()
        {
            var source = new AuditEntity
            {
                Id = 100,
                Action = "UserLogin",
                IpAddress = "192.168.1.100",
                Timestamp = new DateTime(2024, 1, 1)
            };

            var dest = source.MapTo<PublicAuditDto>();

            Assert.NotNull(dest);
            Assert.Equal(100, dest.Id);
            Assert.Equal("UserLogin", dest.Action);
            Assert.Equal("HIDDEN", dest.IpAddress); // Should remain default
            Assert.Equal(default(DateTime), dest.Timestamp); // Should remain default
        }

        [Fact]
        public void MapTo_ExistingObject_ShouldRespectIgnoreMap()
        {
            var source = new UserEntity
            {
                Id = 2,
                UserName = "charlie",
                Password = "newpassword",
                Email = "charlie@test.com"
            };

            var dest = new UserDto
            {
                Id = 1,
                Name = "OldName",
                Password = "OldPassword",
                Email = "old@test.com"
            };

            source.Map(dest);

            Assert.Equal(2, dest.Id);
            Assert.Equal("charlie", dest.Name); // Updated via MapFrom
            Assert.Equal("OldPassword", dest.Password); // IgnoreMap - not updated
            Assert.Equal("charlie@test.com", dest.Email);
        }

        [Fact]
        public void Dictionary_ShouldRespectIgnoreMapOnSource()
        {
            var dto = new UserDto
            {
                Id = 1,
                Name = "Bob",
                Password = "secret",
                Email = "bob@test.com"
            };

            var dict = dto.ToDictionary();

            Assert.True(dict.ContainsKey("Id"));
            Assert.True(dict.ContainsKey("Name"));
            Assert.True(dict.ContainsKey("Email"));
            Assert.False(dict.ContainsKey("Password")); // Should be ignored
            Assert.False(dict.ContainsKey("InternalField")); // Should be ignored
        }
    }
}
