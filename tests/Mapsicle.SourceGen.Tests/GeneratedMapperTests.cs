using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Xunit;

// The declarations under test. The generator reads these at build time and emits a mapper for each,
// registered from a module initializer before any test runs.
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.GenUser), typeof(Mapsicle.SourceGen.Tests.GenUserDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.GenOrder), typeof(Mapsicle.SourceGen.Tests.GenOrderDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.GenKeywords), typeof(Mapsicle.SourceGen.Tests.GenKeywordsDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.GenObsolete), typeof(Mapsicle.SourceGen.Tests.GenObsoleteDto))]

namespace Mapsicle.SourceGen.Tests
{
    public class GenUser
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class GenUserDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class GenOrder { public int Number { get; set; } public decimal Total { get; set; } }
    public class GenOrderDto { public int Number { get; set; } public decimal Total { get; set; } }

    // Members named after C# keywords. Emitting the bare name produces source that does not compile,
    // which breaks the consumer's build inside a file they did not write.
    public class GenKeywords { public int @class { get; set; } public string @event { get; set; } = ""; }
    public class GenKeywordsDto { public int @class { get; set; } public string @event { get; set; } = ""; }

    // A member obsolete as an error. Generated code cannot reference it and pragma cannot suppress
    // an error, so the pair has to be refused and left to the engine.
    public class GenObsolete
    {
        public int Fine { get; set; }
        [Obsolete("gone", true)] public int Gone { get; set; }
    }

    public class GenObsoleteDto { public int Fine { get; set; } public int Gone { get; set; } }

    /// <summary>
    /// The generator's output, exercised the way a consumer would reach it.
    /// </summary>
    /// <remarks>
    /// This project references <c>Mapsicle.SourceGen</c> as an analyzer, which is how it is
    /// installed, so the generator runs over this file and the assertions below run against code it
    /// actually emitted. Asserting on generated source as a string would prove the generator wrote
    /// what was expected and nothing about whether it compiles or maps correctly.
    ///
    /// Every test asserts through a public entry point rather than the registration, because the
    /// point of the seam is that a generated pair is reached by every door.
    /// </remarks>
    public class GeneratedMapperTests
    {
        private static GenUser Sample() => new()
        {
            Id = 7,
            FirstName = "Ada",
            LastName = "Lovelace",
            IsActive = true,
        };

        [Fact]
        public void TheGeneratorActuallyEmittedCode()
        {
            // Every other test in this file passes with the analyzer reference removed, because the
            // runtime engine produces the same answer by design: behaviour is identical and only
            // the cost differs. That makes them worthless as evidence the generator ran. This one
            // looks for the type the generator emits, which exists only if it did.
            var emitted = typeof(GeneratedMapperTests).Assembly
                .GetType("Mapsicle.Generated.MapsicleGeneratedMappers", throwOnError: false);

            Assert.True(emitted is not null,
                "Mapsicle.Generated.MapsicleGeneratedMappers is missing, so the generator produced nothing. " +
                "Every mapping test in this file would still pass, because the engine maps these pairs anyway.");
        }

        [Fact]
        public void TheGeneratedMapperIsTheOneBeingInvoked()
        {
            // The registration outlives a cache clear, so this holds even after the engine is asked
            // to throw away everything it compiled. Without the generator there is nothing to
            // survive and the pair would map through a freshly compiled delegate instead.
            Mapper.ClearCache();

            var registered = typeof(Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) as System.Collections.ICollection;

            Assert.True(registered is { Count: >= 2 },
                $"expected at least the two declared pairs to be registered, found {registered?.Count ?? 0}");
        }

        [Fact]
        public void AGeneratedPairMapsThroughTheTypedDoor()
        {
            var dto = Sample().MapTo<GenUser, GenUserDto>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("Ada", dto.FirstName);
            Assert.Equal("Lovelace", dto.LastName);
            Assert.True(dto.IsActive);
        }

        [Fact]
        public void AGeneratedPairMapsThroughTheUntypedDoor()
        {
            var dto = ((object)Sample()).MapTo<GenUserDto>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("Ada", dto.FirstName);
        }

        [Fact]
        public void AGeneratedPairMapsThroughACollection()
        {
            var source = new List<GenUser> { Sample(), Sample() };

            var dtos = ((IEnumerable)source).MapTo<GenUserDto>();

            Assert.Equal(2, dtos.Count);
            Assert.All(dtos, d => Assert.Equal("Ada", d.FirstName));
        }

        [Fact]
        public void ASecondDeclaredPairIsGeneratedToo()
        {
            var dto = ((object)new GenOrder { Number = 42, Total = 19.99m }).MapTo<GenOrderDto>();

            Assert.Equal(42, dto!.Number);
            Assert.Equal(19.99m, dto.Total);
        }

        [Fact]
        public void AnUndeclaredPairStillMapsThroughTheEngine()
        {
            // The generator is opt in per pair. Everything it was not asked about has to keep
            // working exactly as before, or the package is not additive.
            var dto = ((object)Sample()).MapTo<GenUserPartialDto>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("Ada", dto.FirstName);
        }

        public class GenUserPartialDto { public int Id { get; set; } public string FirstName { get; set; } = ""; }

        [Fact]
        public void MembersNamedAfterKeywordsAreEscaped()
        {
            // If this file compiles at all, the generator escaped them. The assertion is here so the
            // reason the type exists is written down rather than implied by the build passing.
            var dto = new GenKeywords { @class = 3, @event = "e" }.MapTo<GenKeywordsDto>();

            Assert.Equal(3, dto!.@class);
            Assert.Equal("e", dto.@event);
        }

        [Fact]
        public void APairTouchingAnObsoleteAsErrorMemberIsRefusedAndStillMaps()
        {
            // Refused, because generated code referencing it would not compile and the warning
            // cannot be suppressed. The engine maps it by reflection, which does not care.
            var dto = ((object)new GenObsolete { Fine = 1 }).MapTo<GenObsoleteDto>();

            Assert.Equal(1, dto!.Fine);
        }
    }
}
