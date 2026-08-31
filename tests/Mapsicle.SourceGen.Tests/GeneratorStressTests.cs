using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Xunit;

// Shapes chosen because each one is a way the generator could be wrong, not because each one is
// expected to generate. Refusal is a correct outcome for most of these, and the invariant below
// holds either way.
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StWidening), typeof(Mapsicle.SourceGen.Tests.StWideningDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StEnumToString), typeof(Mapsicle.SourceGen.Tests.StEnumToStringDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StNested), typeof(Mapsicle.SourceGen.Tests.StNestedDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StCollection), typeof(Mapsicle.SourceGen.Tests.StCollectionDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StNullableToValue), typeof(Mapsicle.SourceGen.Tests.StNullableToValueDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StValueToNullable), typeof(Mapsicle.SourceGen.Tests.StValueToNullableDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StPrivateSetter), typeof(Mapsicle.SourceGen.Tests.StPrivateSetterDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StInitOnly), typeof(Mapsicle.SourceGen.Tests.StInitOnlyDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StStatics), typeof(Mapsicle.SourceGen.Tests.StStaticsDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StDeepInherit), typeof(Mapsicle.SourceGen.Tests.StDeepInheritDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StManyMembers), typeof(Mapsicle.SourceGen.Tests.StManyMembersDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.StFlattenSource), typeof(Mapsicle.SourceGen.Tests.StFlattenDto))]

namespace Mapsicle.SourceGen.Tests
{
    public enum StColour { None, Red }

    public class StWidening { public int Amount { get; set; } }
    public class StWideningDto { public long Amount { get; set; } }

    public class StEnumToString { public StColour Colour { get; set; } }
    public class StEnumToStringDto { public string Colour { get; set; } = ""; }

    public class StInner { public string City { get; set; } = ""; }
    public class StInnerDto { public string City { get; set; } = ""; }
    public class StNested { public StInner? Inner { get; set; } }
    public class StNestedDto { public StInnerDto? Inner { get; set; } }

    public class StItem { public int Qty { get; set; } }
    public class StItemDto { public int Qty { get; set; } }
    public class StCollection { public List<StItem> Items { get; set; } = new(); }
    public class StCollectionDto { public List<StItemDto> Items { get; set; } = new(); }

    public class StNullableToValue { public int? Maybe { get; set; } }
    public class StNullableToValueDto { public int Maybe { get; set; } }

    public class StValueToNullable { public int Definitely { get; set; } }
    public class StValueToNullableDto { public int? Definitely { get; set; } }

    public class StPrivateSetter { public int Id { get; set; } public string Hidden { get; private set; } = "set"; }
    public class StPrivateSetterDto { public int Id { get; set; } public string Hidden { get; private set; } = ""; }

    public class StInitOnly { public int Id { get; set; } }
    public class StInitOnlyDto { public int Id { get; init; } }

    public class StStatics { public int Id { get; set; } public static int Shared { get; set; } = 9; }
    public class StStaticsDto { public int Id { get; set; } public static int Shared { get; set; } }

    public class StL1 { public string One { get; set; } = ""; }
    public class StL2 : StL1 { public string Two { get; set; } = ""; }
    public class StDeepInherit : StL2 { public string Three { get; set; } = ""; }
    public class StDeepInheritDto { public string One { get; set; } = ""; public string Two { get; set; } = ""; public string Three { get; set; } = ""; }

    public class StManyMembers
    {
        public int A { get; set; } public int B { get; set; } public int C { get; set; }
        public string D { get; set; } = ""; public string E { get; set; } = ""; public string F { get; set; } = "";
        public bool G { get; set; } public double H { get; set; } public decimal I { get; set; }
        public Guid J { get; set; }
    }

    public class StManyMembersDto
    {
        public int A { get; set; } public int B { get; set; } public int C { get; set; }
        public string D { get; set; } = ""; public string E { get; set; } = ""; public string F { get; set; } = "";
        public bool G { get; set; } public double H { get; set; } public decimal I { get; set; }
        public Guid J { get; set; }
    }

    public class StFlattenSource { public StInner Inner { get; set; } = new(); }
    public class StFlattenDto { public string InnerCity { get; set; } = ""; }

    /// <summary>
    /// One invariant across many shapes: whatever route a declared pair takes, it must agree with the engine.
    /// </summary>
    /// <remarks>
    /// The generator refuses most of these, and refusal is a correct outcome. That is precisely why
    /// the assertion is not "the generated result is right" but "the result matches the engine".
    /// A refused pair goes through the engine on both sides and passes trivially; an accepted one
    /// has to produce the same answer the engine would.
    ///
    /// This exists because the narrower version of the check shipped a real defect. Accepting a pair
    /// because <em>some</em> member matched meant an Order with a widening id, an enum to string, a
    /// nested reference and a collection generated a mapper for the one nullable DateTime that
    /// happened to match, filled that, and left the other five members at their defaults. It
    /// returned an almost empty object and raised nothing. Every shape here is a way to reach that
    /// class of failure, and the conformance table did not contain one, because every pair in it had
    /// identical member types.
    /// </remarks>
    public class GeneratorStressTests
    {
        /// <summary>Maps both ways and asserts every read agrees.</summary>
        private static void AgreesWithTheEngine<TSource, TDest>(TSource source, params Func<TDest, object?>[] reads)
            where TSource : class
            where TDest : class
        {
            var whicheverRouteTheCompilerChose = ((object)source).MapTo<TDest>();

            using var engine = MapperFactory.Create();
            var reference = engine.MapTo<TDest>(source);

            Assert.NotNull(whicheverRouteTheCompilerChose);
            Assert.NotNull(reference);

            var disagreements = new List<string>();
            for (var i = 0; i < reads.Length; i++)
            {
                var a = reads[i](whicheverRouteTheCompilerChose!);
                var b = reads[i](reference!);
                if (!Equals(a, b)) disagreements.Add($"  member {i}: got {a ?? "null"}, engine says {b ?? "null"}");
            }

            Assert.True(disagreements.Count == 0,
                $"{typeof(TSource).Name} into {typeof(TDest).Name} disagrees with the engine.\n"
                + string.Join("\n", disagreements));
        }

        [Fact]
        public void AWideningMemberAgrees() =>
            AgreesWithTheEngine<StWidening, StWideningDto>(new StWidening { Amount = 7 }, d => d.Amount);

        [Fact]
        public void AnEnumToStringMemberAgrees() =>
            AgreesWithTheEngine<StEnumToString, StEnumToStringDto>(
                new StEnumToString { Colour = StColour.Red }, d => d.Colour);

        [Fact]
        public void ANestedReferenceAgrees() =>
            AgreesWithTheEngine<StNested, StNestedDto>(
                new StNested { Inner = new StInner { City = "Cebu" } }, d => d.Inner?.City);

        [Fact]
        public void ANullNestedReferenceAgrees() =>
            AgreesWithTheEngine<StNested, StNestedDto>(new StNested { Inner = null }, d => d.Inner?.City);

        [Fact]
        public void ACollectionMemberAgrees() =>
            AgreesWithTheEngine<StCollection, StCollectionDto>(
                new StCollection { Items = { new StItem { Qty = 3 } } },
                d => d.Items.Count, d => d.Items.FirstOrDefault()?.Qty);

        [Fact]
        public void AnEmptyCollectionMemberAgrees() =>
            AgreesWithTheEngine<StCollection, StCollectionDto>(new StCollection(), d => d.Items.Count);

        [Fact]
        public void ANullableSourceIntoAValueDestinationAgrees() =>
            AgreesWithTheEngine<StNullableToValue, StNullableToValueDto>(
                new StNullableToValue { Maybe = 4 }, d => d.Maybe);

        [Fact]
        public void ANullNullableSourceIntoAValueDestinationAgrees() =>
            AgreesWithTheEngine<StNullableToValue, StNullableToValueDto>(
                new StNullableToValue { Maybe = null }, d => d.Maybe);

        [Fact]
        public void AValueSourceIntoANullableDestinationAgrees() =>
            AgreesWithTheEngine<StValueToNullable, StValueToNullableDto>(
                new StValueToNullable { Definitely = 5 }, d => d.Definitely);

        [Fact]
        public void APrivateSetterAgrees() =>
            AgreesWithTheEngine<StPrivateSetter, StPrivateSetterDto>(
                new StPrivateSetter { Id = 1 }, d => d.Id, d => d.Hidden);

        [Fact]
        public void AnInitOnlyDestinationAgrees() =>
            AgreesWithTheEngine<StInitOnly, StInitOnlyDto>(new StInitOnly { Id = 6 }, d => d.Id);

        [Fact]
        public void StaticMembersAreLeftAloneByBoth() =>
            AgreesWithTheEngine<StStatics, StStaticsDto>(new StStatics { Id = 2 }, d => d.Id);

        [Fact]
        public void ThreeLevelsOfInheritanceAgree() =>
            AgreesWithTheEngine<StDeepInherit, StDeepInheritDto>(
                new StDeepInherit { One = "1", Two = "2", Three = "3" },
                d => d.One, d => d.Two, d => d.Three);

        [Fact]
        public void TenMembersAgree() =>
            AgreesWithTheEngine<StManyMembers, StManyMembersDto>(
                new StManyMembers
                {
                    A = 1, B = 2, C = 3, D = "d", E = "e", F = "f",
                    G = true, H = 1.5, I = 2.5m, J = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                },
                d => d.A, d => d.B, d => d.C, d => d.D, d => d.E,
                d => d.F, d => d.G, d => d.H, d => d.I, d => d.J);

        [Fact]
        public void DefaultsAgree() =>
            AgreesWithTheEngine<StManyMembers, StManyMembersDto>(
                new StManyMembers(),
                d => d.A, d => d.D, d => d.G, d => d.H, d => d.I, d => d.J);

        [Fact]
        public void FlatteningAgrees() =>
            AgreesWithTheEngine<StFlattenSource, StFlattenDto>(
                new StFlattenSource { Inner = new StInner { City = "Cebu" } }, d => d.InnerCity);

        [Fact]
        public void ARefusedPairIsNotSilentlyGeneratedShort()
        {
            // The defect this file exists for, stated directly. A pair where one member happens to
            // match and five do not must not generate: it would fill the one and leave the rest at
            // their defaults. Either every member is emitted or the engine keeps the pair.
            var registry = typeof(Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                !.GetValue(null)!;

            var keys = ((System.Collections.IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .ToList();

            // StWidening's only member cannot be emitted, so the pair must not be registered.
            Assert.DoesNotContain(keys, k => k.Contains(nameof(StWidening), StringComparison.Ordinal));

            // StManyMembers is entirely identical-typed, so it must be.
            Assert.Contains(keys, k => k.Contains(nameof(StManyMembers), StringComparison.Ordinal));
        }
    }
}
