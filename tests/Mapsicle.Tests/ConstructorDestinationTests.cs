using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A destination built through a parameterized constructor still has members left to map.
    /// </summary>
    /// <remarks>
    /// The constructor parameters were matched and filled and the mapping stopped there, so every
    /// other writable member kept its initialiser. Nothing was raised. That is the shape of most
    /// immutable DTOs and every positional record, so the result was a partially populated object
    /// that looked mapped.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class ConstructorDestinationTests
    {
        public class CdSource
        {
            public int Id { get; set; } = 7;
            public string Name { get; set; } = "name";
            public string Note { get; set; } = "note";
        }

        public class CdDest
        {
            public CdDest(int id) { Id = id; }
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Note { get; set; } = "";
        }

        public class CdNoMatch
        {
            public CdNoMatch(int somethingElse) { Unmatched = somethingElse; }
            public int Unmatched { get; set; }
            public string Name { get; set; } = "";
        }

        public class CdReadOnly
        {
            public CdReadOnly(int id) { Id = id; }
            public int Id { get; }
            public string Name { get; set; } = "";
        }

        [Fact]
        public void TheUntypedEntryPointFillsTheMembersTheConstructorDidNot()
        {
            Mapper.ClearCache();
            var dto = ((object)new CdSource()).MapTo<CdDest>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
            Assert.Equal("note", dto.Note);
        }

        [Fact]
        public void TheTypedEntryPointFillsThemToo()
        {
            Mapper.ClearCache();
            var dto = new CdSource().MapTo<CdSource, CdDest>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
            Assert.Equal("note", dto.Note);
        }

        [Fact]
        public void TheInstanceMapperFillsThemToo()
        {
            Mapper.ClearCache();
            var dto = MapperFactory.Create().MapTo<CdDest>(new CdSource());

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
            Assert.Equal("note", dto.Note);
        }

        [Fact]
        public void TheCollectionEntryPointFillsThemToo()
        {
            Mapper.ClearCache();
            var dtos = ((IEnumerable)new List<CdSource> { new() }).MapTo<CdDest>();

            Assert.Single(dtos);
            Assert.Equal(7, dtos[0].Id);
            Assert.Equal("name", dtos[0].Name);
            Assert.Equal("note", dtos[0].Note);
        }

        [Fact]
        public void AConstructorParameterMatchingNothingStillLeavesTheRestMapped()
        {
            Mapper.ClearCache();
            var dto = ((object)new CdSource()).MapTo<CdNoMatch>();

            Assert.Equal(0, dto!.Unmatched);
            Assert.Equal("name", dto.Name);
        }

        [Fact]
        public void AGetOnlyMemberSetByTheConstructorIsNotDisturbed()
        {
            // Id has no setter, so it can only come from the constructor. Binding it in a member
            // initialiser would not compile the expression tree at all.
            Mapper.ClearCache();
            var dto = ((object)new CdSource()).MapTo<CdReadOnly>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
        }

        [Fact]
        public void APositionalRecordGetsItsNonPositionalMembersToo()
        {
            Mapper.ClearCache();
            var dto = ((object)new CdSource()).MapTo<CdRecord>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("note", dto.Note);
        }

        public record CdRecord(int Id)
        {
            public string Note { get; set; } = "";
        }

        [Fact]
        public void AParameterlessDestinationIsUnchangedByAnyOfThis()
        {
            // The positive control. The common path must not move.
            Mapper.ClearCache();
            var dto = ((object)new CdSource()).MapTo<CdPlain>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
            Assert.Equal("note", dto.Note);
        }

        public class CdPlain
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Note { get; set; } = "";
        }
    }
}
