using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// A collection has to map through the same configured map one object does.
    /// </summary>
    /// <remarks>
    /// It did not. A collection fell through to the core mapper, so every ForMember, Ignore,
    /// Condition and ResolveUsing on the pair was skipped and the elements came back mapped by
    /// convention. Ignoring a member protected a single object and not a list of them, which is
    /// the failure this file exists to keep fixed.
    /// </remarks>
    public class CollectionMappingTests
    {
        public class CmUser
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
            public string PasswordHash { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public class CmUserDto
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
            public string PasswordHash { get; set; } = "";
            public string Name { get; set; } = "";
        }

        private static List<CmUser> Two() => new()
        {
            new CmUser { Id = 1, Email = "a@b.c", PasswordHash = "SECRET-1", Name = "alice" },
            new CmUser { Id = 2, Email = "d@e.f", PasswordHash = "SECRET-2", Name = "bob" },
        };

        private static IMapper IgnoringPasswordHash() =>
            new MapperConfiguration(c =>
                c.CreateMap<CmUser, CmUserDto>().ForMember(d => d.PasswordHash, o => o.Ignore()))
            .CreateMapper();

        [Fact]
        public void AnIgnoredMemberStaysIgnoredWhenMappingAList()
        {
            var dtos = IgnoringPasswordHash().Map<List<CmUserDto>>(Two());

            Assert.Equal(2, dtos!.Count);
            Assert.All(dtos, d => Assert.True(string.IsNullOrEmpty(d.PasswordHash)));
            Assert.Equal(new[] { "alice", "bob" }, dtos.Select(d => d.Name));
        }

        [Fact]
        public void AnIgnoredMemberStaysIgnoredWhenMappingAnArray()
        {
            var dtos = IgnoringPasswordHash().Map<CmUserDto[]>(Two());

            Assert.Equal(2, dtos!.Length);
            Assert.All(dtos, d => Assert.True(string.IsNullOrEmpty(d.PasswordHash)));
        }

        [Fact]
        public void MapToAListReturnsTheElementsRatherThanAnEmptyList()
        {
            // This returned an empty list. Not null, not a throw, an empty list, from the call
            // shape people arriving from AutoMapper write first.
            var mapper = new MapperConfiguration(c => c.CreateMap<CmUser, CmUserDto>()).CreateMapper();

            var dtos = mapper.Map<List<CmUserDto>>(Two());

            Assert.Equal(2, dtos!.Count);
            Assert.Equal(1, dtos[0].Id);
            Assert.Equal("alice", dtos[0].Name);
        }

        [Fact]
        public void ACustomMappingAppliesToEveryElement()
        {
            var mapper = new MapperConfiguration(c =>
                c.CreateMap<CmUser, CmUserDto>()
                 .ForMember(d => d.Name, o => o.ResolveUsing(s => s.Name.ToUpperInvariant()))).CreateMapper();

            var dtos = mapper.Map<List<CmUserDto>>(Two());

            Assert.Equal(new[] { "ALICE", "BOB" }, dtos!.Select(d => d.Name));
        }

        [Fact]
        public void AConditionIsEvaluatedPerElement()
        {
            var mapper = new MapperConfiguration(c =>
                c.CreateMap<CmUser, CmUserDto>()
                 .ForMember(d => d.Name, o => o.Condition(s => s.Id == 1))).CreateMapper();

            var dtos = mapper.Map<List<CmUserDto>>(Two());

            Assert.Equal("alice", dtos![0].Name);
            Assert.Null(dtos[1].Name);
        }

        [Fact]
        public void HooksRunOncePerElement()
        {
            var seen = new List<int>();
            var mapper = new MapperConfiguration(c =>
                c.CreateMap<CmUser, CmUserDto>().AfterMap((s, d) => seen.Add(s.Id))).CreateMapper();

            mapper.Map<List<CmUserDto>>(Two());

            Assert.Equal(new[] { 1, 2 }, seen);
        }

        [Fact]
        public void TheListAndArrayFormsAgree()
        {
            var mapper = IgnoringPasswordHash();

            var asList = mapper.Map<List<CmUserDto>>(Two());
            var asArray = mapper.Map<CmUserDto[]>(Two());

            Assert.Equal(asList!.Count, asArray!.Length);
            Assert.Equal(asList.Select(d => d.Name), asArray.Select(d => d.Name));
            Assert.Equal(asList.Select(d => d.PasswordHash), asArray.Select(d => d.PasswordHash));
        }

        [Fact]
        public void AnEmptySourceGivesAnEmptyResultRatherThanNull()
        {
            var mapper = IgnoringPasswordHash();

            Assert.Empty(mapper.Map<List<CmUserDto>>(new List<CmUser>())!);
            Assert.Empty(mapper.Map<CmUserDto[]>(new List<CmUser>())!);
        }

        [Fact]
        public void ANullSourceStillYieldsTheDefault()
        {
            Assert.Null(IgnoringPasswordHash().Map<List<CmUserDto>>(null));
        }

        public class CmBase { public string Name { get; set; } = ""; }
        public class CmDerived : CmBase { public string Extra { get; set; } = ""; }
        public class CmBaseDto { public string Name { get; set; } = ""; }

        [Fact]
        public void AListHoldingMoreThanOneRuntimeTypeMapsEachAsWhatItIs()
        {
            // The loop resolves the configuration when it first sees a runtime type and reuses it
            // while that holds. A list that alternates types never gets to keep an answer, and
            // correctness must not depend on it keeping one.
            var mapper = new MapperConfiguration(c =>
            {
                c.CreateMap<CmBase, CmBaseDto>().ForMember(d => d.Name, o => o.ResolveUsing(s => "base:" + s.Name));
                c.CreateMap<CmDerived, CmBaseDto>().ForMember(d => d.Name, o => o.ResolveUsing(s => "derived:" + s.Name));
            }).CreateMapper();

            var source = new List<CmBase>
            {
                new CmBase { Name = "a" },
                new CmDerived { Name = "b" },
                new CmBase { Name = "c" },
                new CmDerived { Name = "d" },
            };

            var dtos = mapper.Map<List<CmBaseDto>>(source);

            Assert.Equal(new[] { "base:a", "derived:b", "base:c", "derived:d" }, dtos!.Select(d => d.Name));
        }

        [Fact]
        public void ANullElementInTheMiddleDoesNotDisturbTheOnesAroundIt()
        {
            var mapper = new MapperConfiguration(c => c.CreateMap<CmUser, CmUserDto>()).CreateMapper();
            var source = new List<CmUser> { new CmUser { Name = "a" }, null!, new CmUser { Name = "c" } };

            var dtos = mapper.Map<List<CmUserDto>>(source);

            Assert.Equal(3, dtos!.Count);
            Assert.Equal("a", dtos[0].Name);
            Assert.Null(dtos[1]);
            Assert.Equal("c", dtos[2].Name);
        }

        [Fact]
        public void ConfiguringAfterTheFirstCollectionMapTakesEffect()
        {
            ITypeMapExpression<CmUser, CmUserDto> expression = null!;
            var mapper = new MapperConfiguration(c => expression = c.CreateMap<CmUser, CmUserDto>()).CreateMapper();

            Assert.Equal("SECRET-1", mapper.Map<List<CmUserDto>>(Two())![0].PasswordHash);

            expression.ForMember(d => d.PasswordHash, o => o.Ignore());

            Assert.True(string.IsNullOrEmpty(mapper.Map<List<CmUserDto>>(Two())![0].PasswordHash));
        }
    }
}
