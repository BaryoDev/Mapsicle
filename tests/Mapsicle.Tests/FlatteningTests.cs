using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Tests for the flattening feature: Source.Child.Property -> DestChildProperty
    /// </summary>
    public class FlatteningTests
    {
        #region Test Models

        public class Address
        {
            public string City { get; set; } = string.Empty;
            public string Country { get; set; } = string.Empty;
            public string? ZipCode { get; set; }
        }

        public class Person
        {
            public string Name { get; set; } = string.Empty;
            public Address? Address { get; set; }
        }

        public class PersonFlatDto
        {
            public string Name { get; set; } = string.Empty;
            public string AddressCity { get; set; } = string.Empty;
            public string AddressCountry { get; set; } = string.Empty;
            public string? AddressZipCode { get; set; }
        }

        public class Company
        {
            public string Name { get; set; } = string.Empty;
            public Person? Owner { get; set; }
            public Address? Headquarters { get; set; }
        }

        public class CompanyFlatDto
        {
            public string Name { get; set; } = string.Empty;
            public string? OwnerName { get; set; }
            public string? HeadquartersCity { get; set; }
            public string? HeadquartersCountry { get; set; }
        }

        #endregion

        [Fact]
        public void Flattening_SingleLevel_ShouldMapNestedToFlat()
        {
            var source = new Person
            {
                Name = "Alice",
                Address = new Address { City = "Manila", Country = "Philippines" }
            };

            var dest = source.MapTo<PersonFlatDto>();

            Assert.NotNull(dest);
            Assert.Equal("Alice", dest.Name);
            Assert.Equal("Manila", dest.AddressCity);
            Assert.Equal("Philippines", dest.AddressCountry);
        }

        [Fact]
        public void Flattening_NullNestedObject_ShouldNotCrash()
        {
            var source = new Person { Name = "Bob", Address = null };

            var dest = source.MapTo<PersonFlatDto>();

            Assert.NotNull(dest);
            Assert.Equal("Bob", dest.Name);
            // When parent is null, flattened properties get default values
            Assert.True(string.IsNullOrEmpty(dest.AddressCity));
        }

        [Fact]
        public void Flattening_MultipleNestedObjects_ShouldMapAll()
        {
            var source = new Company
            {
                Name = "TechCorp",
                Owner = new Person { Name = "CEO", Address = new Address { City = "Makati" } },
                Headquarters = new Address { City = "BGC", Country = "PH" }
            };

            var dest = source.MapTo<CompanyFlatDto>();

            Assert.NotNull(dest);
            Assert.Equal("TechCorp", dest.Name);
            Assert.Equal("CEO", dest.OwnerName);
            Assert.Equal("BGC", dest.HeadquartersCity);
            Assert.Equal("PH", dest.HeadquartersCountry);
        }

        [Fact]
        public void Flattening_PartialNested_ShouldHandleGracefully()
        {
            var source = new Company
            {
                Name = "StartupInc",
                Owner = null,
                Headquarters = new Address { City = "QC", Country = "Philippines" }
            };

            var dest = source.MapTo<CompanyFlatDto>();

            Assert.NotNull(dest);
            Assert.Equal("StartupInc", dest.Name);
            Assert.Null(dest.OwnerName); // Null parent
            Assert.Equal("QC", dest.HeadquartersCity);
        }

        [Fact]
        public void Flattening_NullableProperty_ShouldMap()
        {
            var source = new Person
            {
                Name = "Charlie",
                Address = new Address { City = "Cebu", Country = "PH", ZipCode = "6000" }
            };

            var dest = source.MapTo<PersonFlatDto>();

            Assert.Equal("6000", dest.AddressZipCode);
        }
    }
}
