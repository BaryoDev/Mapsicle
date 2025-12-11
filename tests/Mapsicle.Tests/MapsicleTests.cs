using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    public class MapsicleTests
    {
        [Fact]
        public void HappyPath_ExactMatch_ShouldMapCorrectly()
        {
            var source = new User { Id = 1, Name = "Alice" };
            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal(source.Id, dest.Id);
            Assert.Equal(source.Name, dest.Name);
        }

        [Fact]
        public void CaseInsensitivity_ShouldMapCorrectly()
        {
            var source = new { email = "alice@example.com" }; // Anonymous type
            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal("alice@example.com", dest.Email);
        }

        [Fact]
        public void NullHandling_ShouldReturnDefault()
        {
            User source = null!;
            var dest = source.MapTo<UserDto>();
            Assert.Null(dest);
        }

        [Fact]
        public void ListMapping_ShouldMapCollection()
        {
            var source = new List<User>
            {
                new User { Id = 1, Name = "Alice" },
                new User { Id = 2, Name = "Bob" }
            };

            var dest = source.MapTo<UserDto>().ToList();

            Assert.Equal(2, dest.Count);
            Assert.Equal("Alice", dest[0].Name);
            Assert.Equal("Bob", dest[1].Name);
        }

        [Fact]
        public void PartialMatching_ShouldIgnoreExtraProperties()
        {
            var source = new ExtraUser { Id = 1, Name = "Alice", Extra = "Secret" };
            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal("Alice", dest.Name);
            // Dest doesn't have "Extra", should just be ignored
        }
        
        [Fact]
        public void TypeMismatch_ShouldSkipIncompatibleTypes()
        {
            var source = new { Id = "NotAnInteger", Name = "Alice" };
            var dest = source.MapTo<UserDto>();

            Assert.NotNull(dest);
            Assert.Equal("Alice", dest.Name);
            Assert.Equal(0, dest.Id); // Id mismatch String -> Int should be skipped, so default(int)
        }



        [Fact]
        public void NestedMapping_DifferentTypes_ShouldDeepMap()
        {
            var source = new SourceParent { Child = new SourceChild { Name = "Baby" } };
            var dest = source.MapTo<DestParent>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.Child);
            Assert.Equal("Baby", dest.Child.Name);

            // Ensure it's a new instance, not a reference copy (since types differ, ref copy is impossible anyway, but good to check generally)
        }

        [Fact]
        public void MapToExisting_ShouldUpdateTarget()
        {
            var source = new User { Id = 2, Name = "Updated" };
            var target = new UserDto { Id = 1, Name = "Original", Email = "keep@me.com" };

            // Act
            source.Map(target);

            // Assert
            Assert.Equal(2, target.Id);
            Assert.Equal("Updated", target.Name);
            Assert.Equal("keep@me.com", target.Email); // Should not be touched as it doesn't exist on source (or is just skipped if null? logic dictates ignore missing source props)
        }


        [Fact]
        public void Coercion_IntToString_ShouldMap()
        {
            var source = new { Id = 123 };
            var dest = source.MapTo<StringIdDto>();
            
            Assert.NotNull(dest);
            Assert.Equal("123", dest.Id);
        }

        [Fact]
        public void Coercion_EnumToInt_ShouldMap()
        {
            var source = new { Status = UserStatus.Active };
            var dest = source.MapTo<IntStatusDto>();
            
            Assert.NotNull(dest);
            Assert.Equal(1, dest.Status);
        }

        [Fact]
        public void Record_ShouldMapViaConstructor()
        {
            var source = new User { Id = 99, Name = "RecordReader" };
            var dest = source.MapTo<UserRecord>();

            Assert.NotNull(dest);
            Assert.Equal(99, dest.Id);
            Assert.Equal("RecordReader", dest.Name);
        }

        [Fact]
        public void ReferenceCopy_ShouldCopyReference_WhenTypesMatch()
        {
            // Renamed from NestedObject... to be clearer
            var addr = new Address { City = "New York" };
            var source = new UserWithAddress { Address = addr };
            var dest = source.MapTo<UserWithAddressDto>(); 

            Assert.NotNull(dest);
            Assert.Same(source.Address, dest.Address); // Shallow copy verified
            Assert.Equal("New York", dest.Address.City);
        }

        [Fact]
        public void ListMapping_WithNulls_ShouldHandleGracefully()
        {
            var source = new List<User>
            {
                new User { Name = "Alice" },
                null,
                new User { Name = "Bob" }
            };

            var dest = source.MapTo<UserDto>().ToList();
            
            Assert.Equal(3, dest.Count);
            Assert.Equal("Alice", dest[0].Name);
            Assert.Null(dest[1]); // Assuming Select(x => x.MapTo) returns null for null input
            Assert.Equal("Bob", dest[2].Name);
        }

        // Test Helpers
        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class ExtraUser
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Extra { get; set; } = string.Empty;
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class UserAge 
        {
            public int Age { get; set; }
        }

        public class NullableUserDto 
        {
            public int? Age { get; set; }
        }

        public class Address
        {
            public string City { get; set; }
        }

        public class UserWithAddress
        {
            public Address Address { get; set; }
        }

        public class UserWithAddressDto
        {
            public Address Address { get; set; }
        }

        public class SourceChild
        {
            public string Name { get; set; } = string.Empty;
        }

        public class SourceParent
        {
            public SourceChild Child { get; set; }
        }

        public class DestChild
        {
            public string Name { get; set; } = string.Empty;
        }

        public class DestParent
        {
            public DestChild Child { get; set; }
        }
        
        public class StringIdDto
        {
            public string Id { get; set; } = string.Empty;
        }

        public enum UserStatus { Inactive = 0, Active = 1 }

        public class IntStatusDto
        {
            public int Status { get; set; }
        }

        public record UserRecord(int Id, string Name);
    }
}
