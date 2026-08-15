using System;
using System.Linq;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// Regression tests for fixed bugs:
    /// 1. Profile ReverseMap used to register a duplicate, unconfigured reverse map that
    ///    failed AssertConfigurationIsValid even when the reverse side was fully configured.
    /// 2. The in-place Map(source, destination) overload used to skip BeforeMap/AfterMap hooks.
    /// </summary>
    public class BugfixRegressionTests
    {
        #region Test Models

        public class Person
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Secret { get; set; } = string.Empty;
        }

        public class PersonDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Extra { get; set; } = string.Empty;
        }

        public class PersonProfile : MapsicleProfile
        {
            protected override void Configure()
            {
                CreateMap<Person, PersonDto>()
                    .ForMember(d => d.Extra, opt => opt.Ignore())
                    .ReverseMap()
                    .ForMember(p => p.Secret, opt => opt.Ignore());
            }
        }

        #endregion

        #region Profile ReverseMap

        [Fact]
        public void Profile_ReverseMap_ShouldNotRegisterDuplicateReverseMap()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<PersonProfile>());

            var reverseMaps = config.GetAllTypeMaps()
                .Where(m => m.SourceType == typeof(PersonDto) && m.DestinationType == typeof(Person))
                .ToList();

            Assert.Single(reverseMaps);
        }

        [Fact]
        public void Profile_ReverseMap_WithIgnoreOnReverse_AssertConfigurationIsValid_ShouldPass()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<PersonProfile>());

            // Used to throw "Unmapped member 'Secret'" because the reverse Ignore()
            // landed on a second map while an empty shadow reverse map was validated.
            config.AssertConfigurationIsValid();
        }

        [Fact]
        public void Profile_ReverseMap_ConfigurationOnReverse_ShouldApply()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<PersonProfile>());
            var mapper = config.CreateMapper();

            var dto = new PersonDto { Id = 7, Name = "Alice", Extra = "x" };
            var person = mapper.Map<PersonDto, Person>(dto);

            Assert.NotNull(person);
            Assert.Equal(7, person!.Id);
            Assert.Equal("Alice", person.Name);
            Assert.Null(person.Secret); // ignored on reverse — library semantics force default
        }

        #endregion

        #region In-place Map hooks

        [Fact]
        public void Map_ToExistingDestination_ShouldInvokeBeforeMapAndAfterMap()
        {
            bool beforeCalled = false;
            bool afterCalled = false;

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonDto>()
                    .ForMember(d => d.Extra, opt => opt.Ignore())
                    .BeforeMap((src, dest) => beforeCalled = true)
                    .AfterMap((src, dest) => dest.Extra = "stamped");
            });
            var mapper = config.CreateMapper();

            var source = new Person { Id = 1, Name = "Bob" };
            var destination = new PersonDto { Id = 99, Name = "Old", Extra = "old" };

            var result = mapper.Map(source, destination);
            afterCalled = result.Extra == "stamped";

            Assert.True(beforeCalled, "BeforeMap should run for in-place Map(source, destination)");
            Assert.True(afterCalled, "AfterMap should run for in-place Map(source, destination)");
            Assert.Equal(1, result.Id);
            Assert.Equal("Bob", result.Name);
            Assert.Same(destination, result);
        }

        #endregion
    }
}
