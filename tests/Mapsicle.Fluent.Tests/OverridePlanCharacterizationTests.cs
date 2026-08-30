using System;
using System.Linq;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// Pins what the override pass does, member by member, before it is compiled rather than
    /// reflected.
    /// </summary>
    /// <remarks>
    /// The override pass reads its answer out of three case-insensitive dictionaries on every call
    /// and writes each member with <c>PropertyInfo.SetValue</c>. Both are worth removing, and both
    /// are only safe to remove if the behaviour they implement is written down first. The awkward
    /// one is the last test here: <c>CreateMap</c> hands back a live expression, so configuration
    /// can legitimately change after a map has already run, and any cache has to notice.
    /// </remarks>
    public class OverridePlanCharacterizationTests
    {
        public class OpSource
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Secret { get; set; } = "";
            public int Count { get; set; }
            public OpInner Inner { get; set; } = new();
        }

        public class OpInner { public string City { get; set; } = ""; }

        public class OpDest
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Secret { get; set; } = "";
            public int Count { get; set; }
            public string InnerCity { get; set; } = "";
        }

        private static OpSource Sample() => new()
        {
            Id = 7,
            Name = "name",
            Secret = "secret",
            Count = 3,
            Inner = new OpInner { City = "Cebu" },
        };

        [Fact]
        public void ConventionMapsEveryMatchingMemberWhenNothingIsConfigured()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<OpSource, OpDest>()).CreateMapper();

            var dto = mapper.Map<OpDest>(Sample());

            Assert.Equal(7, dto!.Id);
            Assert.Equal("name", dto.Name);
            Assert.Equal("secret", dto.Secret);
            Assert.Equal(3, dto.Count);
        }

        [Fact]
        public void AnIgnoredMemberIsSetToItsDefaultEvenThoughConventionWouldHaveMappedIt()
        {
            // Not merely "left alone". The override pass actively writes the default over whatever
            // the convention pass put there, and a reference member becomes null rather than "".
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>().ForMember(d => d.Secret, o => o.Ignore())).CreateMapper();

            var dto = mapper.Map<OpDest>(Sample());

            Assert.Null(dto!.Secret);
            Assert.Equal("name", dto.Name);
        }

        [Fact]
        public void AnIgnoredValueTypeMemberBecomesZeroRatherThanTheMappedValue()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>().ForMember(d => d.Count, o => o.Ignore())).CreateMapper();

            Assert.Equal(0, mapper.Map<OpDest>(Sample())!.Count);
        }

        [Fact]
        public void AFalseConditionWritesTheDefaultOverTheConventionMappedValue()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>().ForMember(d => d.Name, o => o.Condition(s => false))).CreateMapper();

            Assert.Null(mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void ATrueConditionLeavesTheConventionMappedValueInPlace()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>().ForMember(d => d.Name, o => o.Condition(s => true))).CreateMapper();

            Assert.Equal("name", mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void ResolveUsingSuppliesTheValue()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Name, o => o.ResolveUsing(s => s.Name.ToUpperInvariant()))).CreateMapper();

            Assert.Equal("NAME", mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void MapFromReachesThroughANestedMember()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.InnerCity, o => o.MapFrom(s => s.Inner.City))).CreateMapper();

            Assert.Equal("Cebu", mapper.Map<OpDest>(Sample())!.InnerCity);
        }

        [Fact]
        public void AConditionAndACustomMappingTogether_TheConditionDecides()
        {
            var onlyOdd = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Name, o => { o.ResolveUsing(s => "resolved"); o.Condition(s => s.Id % 2 == 1); }))
                .CreateMapper();

            Assert.Equal("resolved", onlyOdd.Map<OpDest>(Sample())!.Name);

            var even = Sample();
            even.Id = 8;
            Assert.Null(onlyOdd.Map<OpDest>(even)!.Name);
        }

        [Fact]
        public void ACustomMappingOnAValueTypeMemberStillArrivesUnboxedAndCorrect()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Count, o => o.ResolveUsing(s => s.Count * 10))).CreateMapper();

            Assert.Equal(30, mapper.Map<OpDest>(Sample())!.Count);
        }

        [Fact]
        public void BeforeMapRunsOnTheEmptyDestinationAndAfterMapRunsOnTheFilledOne()
        {
            var order = "";
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .BeforeMap((s, d) => order += d.Name == "" ? "before(empty)," : "before(filled),")
                   .AfterMap((s, d) => order += d.Name == "name" ? "after(filled)" : "after(empty)")).CreateMapper();

            mapper.Map<OpDest>(Sample());

            Assert.Equal("before(empty),after(filled)", order);
        }

        [Fact]
        public void AfterMapSeesTheOverridesAlreadyApplied()
        {
            string? seen = null;
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Name, o => o.ResolveUsing(s => "resolved"))
                   .AfterMap((s, d) => seen = d.Name)).CreateMapper();

            mapper.Map<OpDest>(Sample());

            Assert.Equal("resolved", seen);
        }

        [Fact]
        public void MappingIsStableAcrossRepeatedCalls()
        {
            // The plan is resolved once and reused, so the second call must not differ from the
            // first. A cache that captured a per-call value would show up here.
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Secret, o => o.Ignore())
                   .ForMember(d => d.Name, o => o.ResolveUsing(s => s.Name.ToUpperInvariant()))).CreateMapper();

            for (var i = 0; i < 50; i++)
            {
                var dto = mapper.Map<OpDest>(Sample());
                Assert.Null(dto!.Secret);
                Assert.Equal("NAME", dto.Name);
                Assert.Equal(7, dto.Id);
            }
        }

        [Fact]
        public void ConfiguringAMemberAfterTheFirstMapTakesEffectOnTheNext()
        {
            // CreateMap hands back a live expression, so this is legal, and the current
            // implementation supports it by rebuilding its answer on every single call. Anything
            // that caches that answer has to notice the change instead. Without this test a cache
            // keyed only on the type pair passes the whole suite.
            OpDest? first = null;
            ITypeMapExpression<OpSource, OpDest> expression = null!;

            var config = new MapperConfiguration(cfg => expression = cfg.CreateMap<OpSource, OpDest>());
            var mapper = config.CreateMapper();

            first = mapper.Map<OpDest>(Sample());
            Assert.Equal("secret", first!.Secret);

            expression.ForMember(d => d.Secret, o => o.Ignore());

            Assert.Null(mapper.Map<OpDest>(Sample())!.Secret);
        }

        [Fact]
        public void AddingACustomMappingAfterTheFirstMapTakesEffectOnTheNext()
        {
            ITypeMapExpression<OpSource, OpDest> expression = null!;
            var mapper = new MapperConfiguration(cfg => expression = cfg.CreateMap<OpSource, OpDest>()).CreateMapper();

            Assert.Equal("name", mapper.Map<OpDest>(Sample())!.Name);

            expression.ForMember(d => d.Name, o => o.ResolveUsing(s => "changed"));

            Assert.Equal("changed", mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void AddingAConditionAfterTheFirstMapTakesEffectOnTheNext()
        {
            ITypeMapExpression<OpSource, OpDest> expression = null!;
            var mapper = new MapperConfiguration(cfg => expression = cfg.CreateMap<OpSource, OpDest>()).CreateMapper();

            Assert.Equal("name", mapper.Map<OpDest>(Sample())!.Name);

            expression.ForMember(d => d.Name, o => o.Condition(s => false));

            Assert.Null(mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void AddingAnAfterMapHookAfterTheFirstMapTakesEffectOnTheNext()
        {
            ITypeMapExpression<OpSource, OpDest> expression = null!;
            var mapper = new MapperConfiguration(cfg => expression = cfg.CreateMap<OpSource, OpDest>()).CreateMapper();

            mapper.Map<OpDest>(Sample());

            expression.AfterMap((s, d) => d.Name = "hooked");

            Assert.Equal("hooked", mapper.Map<OpDest>(Sample())!.Name);
        }

        [Fact]
        public void ACustomMappingReturningNullForAValueTypeMemberYieldsTheDefault()
        {
            // PropertyInfo.SetValue(dest, null) on an int writes 0 rather than throwing, so a
            // compiled setter has to do the same. Assigning (int)null in an expression tree throws
            // instead, which would turn a quiet zero into a NullReferenceException from inside a
            // lambda_method frame.
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ForMember(d => d.Count, o => o.ResolveUsing(s => (object?)null))).CreateMapper();

            Assert.Equal(0, mapper.Map<OpDest>(Sample())!.Count);
        }

        public class OpNoDefaultCtor
        {
            public OpNoDefaultCtor(int id) { Id = id; }
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Secret { get; set; } = "";
        }

        [Fact]
        public void ADestinationWithNoParameterlessConstructorStillMapsThroughTheFallback()
        {
            // Construction used to be Activator.CreateInstance in a try/catch, where the catch was
            // how this case was detected. Asking for the constructor up front has to reach the same
            // fallback, and nothing covered this before.
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<OpSource, OpNoDefaultCtor>()).CreateMapper();

            var dto = mapper.Map<OpNoDefaultCtor>(Sample());

            Assert.NotNull(dto);
            Assert.Equal(7, dto!.Id);

            // This asserted "" until the constructor path was fixed. The parameter was matched and
            // filled and the mapping stopped, so everything else kept its initialiser.
            Assert.Equal("name", dto.Name);
        }

        [Fact]
        public void ADestinationWithNoParameterlessConstructorStillAppliesOverridesAndHooks()
        {
            string? afterSaw = null;
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpNoDefaultCtor>()
                   .ForMember(d => d.Secret, o => o.Ignore())
                   .ForMember(d => d.Name, o => o.ResolveUsing(s => "resolved"))
                   .AfterMap((s, d) => afterSaw = d.Name)).CreateMapper();

            var dto = mapper.Map<OpNoDefaultCtor>(Sample());

            Assert.Equal("resolved", dto!.Name);
            Assert.Null(dto.Secret);
            Assert.Equal("resolved", afterSaw);
        }

        [Fact]
        public void AConstructorFactorySkipsTheConventionPassEntirely()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OpSource, OpDest>()
                   .ConstructUsing(s => new OpDest { Name = "built" })).CreateMapper();

            var dto = mapper.Map<OpDest>(Sample());

            Assert.Equal("built", dto!.Name);
            Assert.Equal(0, dto.Id);
        }
    }
}
