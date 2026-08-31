using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Xunit;

// Every pair the conformance table covers. Declaring them here is what makes the generated lane
// exist; the runtime lane needs no declaration, which is the asymmetry the whole table is about.
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfFlat), typeof(Mapsicle.SourceGen.Tests.ConfFlatDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfKinds), typeof(Mapsicle.SourceGen.Tests.ConfKindsDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfNullable), typeof(Mapsicle.SourceGen.Tests.ConfNullableDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfDerived), typeof(Mapsicle.SourceGen.Tests.ConfDerivedDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfCasing), typeof(Mapsicle.SourceGen.Tests.ConfCasingDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfPartial), typeof(Mapsicle.SourceGen.Tests.ConfPartialDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfWiden), typeof(Mapsicle.SourceGen.Tests.ConfWidenDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfEnumText), typeof(Mapsicle.SourceGen.Tests.ConfEnumTextDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfCrossEnum), typeof(Mapsicle.SourceGen.Tests.ConfCrossEnumDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfStamp), typeof(Mapsicle.SourceGen.Tests.ConfStampDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfNest), typeof(Mapsicle.SourceGen.Tests.ConfNestDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfList), typeof(Mapsicle.SourceGen.Tests.ConfListDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfFlatten), typeof(Mapsicle.SourceGen.Tests.ConfFlattenDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfLift), typeof(Mapsicle.SourceGen.Tests.ConfLiftDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfCaseEnum), typeof(Mapsicle.SourceGen.Tests.ConfCaseEnumDto))]
[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.Tests.ConfControlled), typeof(Mapsicle.SourceGen.Tests.ConfControlledDto))]

namespace Mapsicle.SourceGen.Tests
{
    public enum ConfColour { Unset = 0, Teal = 1, Amber = 2 }

    public class ConfFlat { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class ConfFlatDto { public int Id { get; set; } public string Name { get; set; } = ""; }

    public class ConfKinds
    {
        public int Count { get; set; }
        public long Ticks { get; set; }
        public decimal Total { get; set; }
        public double Ratio { get; set; }
        public bool Active { get; set; }
        public Guid Reference { get; set; }
        public DateTime Stamp { get; set; }
        public ConfColour Colour { get; set; }
        public string Text { get; set; } = "";
    }

    public class ConfKindsDto
    {
        public int Count { get; set; }
        public long Ticks { get; set; }
        public decimal Total { get; set; }
        public double Ratio { get; set; }
        public bool Active { get; set; }
        public Guid Reference { get; set; }
        public DateTime Stamp { get; set; }
        public ConfColour Colour { get; set; }
        public string Text { get; set; } = "";
    }

    public class ConfNullable { public int? Maybe { get; set; } public string? Words { get; set; } }
    public class ConfNullableDto { public int? Maybe { get; set; } public string? Words { get; set; } }

    public class ConfBase { public string Inherited { get; set; } = ""; }
    public class ConfDerived : ConfBase { public int Own { get; set; } }
    public class ConfDerivedDto { public string Inherited { get; set; } = ""; public int Own { get; set; } }

    public class ConfCasing { public string USERNAME { get; set; } = ""; public int id { get; set; } }
    public class ConfCasingDto { public string Username { get; set; } = ""; public int Id { get; set; } }

    public class ConfPartial { public int Kept { get; set; } public string Dropped { get; set; } = ""; }
    public class ConfPartialDto { public int Kept { get; set; } public string Absent { get; set; } = "not-mapped"; }


    // ---- shapes the emitter learned when it was widened -----------------------------------------

    public class ConfWiden { public int Amount { get; set; } public int? Maybe { get; set; } public short Small { get; set; } }
    public class ConfWidenDto { public long Amount { get; set; } public long? Maybe { get; set; } public decimal Small { get; set; } }

    public class ConfEnumText { public ConfColour Colour { get; set; } public ConfColour? Maybe { get; set; } }
    public class ConfEnumTextDto { public string Colour { get; set; } = ""; public string Maybe { get; set; } = ""; }

    // Amber sits at a different value in each, so a rule matching by value gives a number the
    // destination declares no member for and this row goes red.
    public enum ConfLeft { Unset = 0, Teal = 1, Amber = 7 }
    public enum ConfRight { Unset = 0, Amber = 2, Teal = 5 }
    public class ConfCrossEnum { public ConfLeft Colour { get; set; } public ConfLeft? Maybe { get; set; } }
    public class ConfCrossEnumDto { public ConfRight Colour { get; set; } public ConfRight? Maybe { get; set; } }

    public class ConfStamp { public DateTime At { get; set; } public DateTime? Maybe { get; set; } }
    public class ConfStampDto { public DateTimeOffset At { get; set; } public DateTimeOffset? Maybe { get; set; } }

    public class ConfInner2 { public string City { get; set; } = ""; public ConfDeep? Deep { get; set; } }
    public class ConfDeep { public string Iso { get; set; } = ""; }
    public class ConfInner2Dto { public string City { get; set; } = ""; public ConfDeepDto? Deep { get; set; } }
    public class ConfDeepDto { public string Iso { get; set; } = ""; }
    public class ConfNest { public ConfInner2? Inner { get; set; } }
    public class ConfNestDto { public ConfInner2Dto? Inner { get; set; } }

    public class ConfItem { public string Sku { get; set; } = ""; public int Qty { get; set; } }
    public class ConfItemDto { public string Sku { get; set; } = ""; public int Qty { get; set; } }
    public class ConfList { public List<ConfItem> Items { get; set; } = new(); public int[] Numbers { get; set; } = System.Array.Empty<int>(); }
    public class ConfListDto { public List<ConfItemDto> Items { get; set; } = new(); public List<int> Numbers { get; set; } = new(); }

    public class ConfFlatten { public ConfInner2? Inner { get; set; } }
    public class ConfFlattenDto { public string InnerCity { get; set; } = ""; public string InnerDeepIso { get; set; } = ""; }

    // A flattened leaf needing the nullable lift. The emitter's leaf test was narrower than the
    // engine's, so it found no path, and a missing path is a silent skip rather than a refusal: the
    // member came back null while the engine filled it.
    public class ConfLiftInner { public int Count { get; set; } }
    public class ConfLift { public ConfLiftInner Inner { get; set; } = new(); }
    public class ConfLiftDto { public int? InnerCount { get; set; } }

    // Two destination names differing only by case. The engine matches against Enum.GetNames, which
    // is ordered by value; the emitter took declaration order and picked the other member.
    public enum ConfCaseFrom { None = 0, Bravo = 9 }
    public enum ConfCaseTo { None = 0, BRAVO = 2, Bravo = 1 }
    public class ConfCaseEnum { public ConfCaseFrom Value { get; set; } }
    public class ConfCaseEnumDto { public ConfCaseTo Value { get; set; } }

    // The two attributes that are controls rather than conventions. Section 6 says [IgnoreMap] is
    // honoured on every entry point, and the generated one is the fastest entry point, so it is the
    // one most worth pinning.
    public class ConfControlled
    {
        public int Id { get; set; }
        public bool IsAdmin { get; set; }
        public string Actual { get; set; } = "";
        public string Decoy { get; set; } = "";
    }

    public class ConfControlledDto
    {
        public int Id { get; set; }
        [IgnoreMap] public bool IsAdmin { get; set; }
        [MapFrom("Actual")] public string Decoy { get; set; } = "";
    }

    /// <summary>
    /// One table of cases, run through the runtime lane and the generated lane, asserting they agree.
    /// </summary>
    /// <remarks>
    /// The generator is a second implementation of the conversion rules, which is the one thing
    /// CONTRIBUTING says must exist once. That exception is only affordable if the two are proven to
    /// agree rather than assumed to, because this project has already shipped drift twice: it is why
    /// PropertyConversion exists, and 2.0.0 found two more entry points that had quietly stopped
    /// matching it.
    ///
    /// The oracle is an instance mapper. <c>MapperFactory.Create()</c> keeps its own caches and never
    /// consults a generated registration, so it always compiles an expression tree, while the static
    /// door for a declared pair always invokes generated code. Two lanes, same input, no mocking.
    ///
    /// The refusal cases matter as much as the agreements. A shape the generator declines has to keep
    /// working through the engine, or "opt in per pair" is not true.
    /// </remarks>
    [Collection("SourceGenRegistry")]
    public class LaneConformanceTests
    {
        /// <summary>Maps the same source both ways and asserts the results match member by member.</summary>
        private static void LanesAgree<TSource, TDest>(TSource source, params Func<TDest, object?>[] reads)
            where TSource : class
            where TDest : class
        {
            var generated = ((object)source).MapTo<TDest>();

            using var runtime = MapperFactory.Create();
            var interpreted = runtime.MapTo<TDest>(source);

            Assert.NotNull(generated);
            Assert.NotNull(interpreted);

            var disagreements = new List<string>();
            for (var i = 0; i < reads.Length; i++)
            {
                var g = reads[i](generated!);
                var r = reads[i](interpreted!);
                if (!Equals(g, r))
                {
                    disagreements.Add($"  member {i}: generated={Show(g)} runtime={Show(r)}");
                }
            }

            Assert.True(disagreements.Count == 0,
                $"{typeof(TSource).Name} into {typeof(TDest).Name}: the lanes disagree.\n"
                + string.Join("\n", disagreements));
        }

        private static string Show(object? value) => value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            _ => value.ToString() ?? "null",
        };

        [Fact]
        public void AFlatPairAgrees() =>
            LanesAgree<ConfFlat, ConfFlatDto>(
                new ConfFlat { Id = 7, Name = "Ada" },
                d => d.Id, d => d.Name);

        [Fact]
        public void EveryPrimitiveKindAgrees() =>
            LanesAgree<ConfKinds, ConfKindsDto>(
                new ConfKinds
                {
                    Count = 42,
                    Ticks = 9_000_000_000L,
                    Total = 19.99m,
                    Ratio = 0.5d,
                    Active = true,
                    Reference = Guid.Parse("2f1c4a8e-0000-4000-8000-000000000001"),
                    Stamp = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
                    Colour = ConfColour.Amber,
                    Text = "text",
                },
                d => d.Count, d => d.Ticks, d => d.Total, d => d.Ratio,
                d => d.Active, d => d.Reference, d => d.Stamp, d => d.Colour, d => d.Text);

        [Fact]
        public void NullablesWithValuesAgree() =>
            LanesAgree<ConfNullable, ConfNullableDto>(
                new ConfNullable { Maybe = 3, Words = "here" },
                d => d.Maybe, d => d.Words);

        [Fact]
        public void NullablesWithoutValuesAgree() =>
            LanesAgree<ConfNullable, ConfNullableDto>(
                new ConfNullable { Maybe = null, Words = null },
                d => d.Maybe, d => d.Words);

        [Fact]
        public void DefaultValuesAgree() =>
            LanesAgree<ConfKinds, ConfKindsDto>(
                new ConfKinds(),
                d => d.Count, d => d.Ticks, d => d.Total, d => d.Ratio,
                d => d.Active, d => d.Reference, d => d.Stamp, d => d.Colour, d => d.Text);

        [Fact]
        public void AnInheritedMemberAgrees() =>
            LanesAgree<ConfDerived, ConfDerivedDto>(
                new ConfDerived { Inherited = "from base", Own = 5 },
                d => d.Inherited, d => d.Own);

        [Fact]
        public void NameMatchingIgnoresCaseInBothLanes() =>
            LanesAgree<ConfCasing, ConfCasingDto>(
                new ConfCasing { USERNAME = "ada", id = 9 },
                d => d.Username, d => d.Id);

        [Fact]
        public void AMemberWithNoSourceIsLeftAloneInBothLanes() =>
            LanesAgree<ConfPartial, ConfPartialDto>(
                new ConfPartial { Kept = 4, Dropped = "ignored" },
                d => d.Kept, d => d.Absent);

        [Fact]
        public void AnEmptyStringAgrees() =>
            LanesAgree<ConfFlat, ConfFlatDto>(
                new ConfFlat { Id = 0, Name = "" },
                d => d.Id, d => d.Name);


        // ---- the widened rules -------------------------------------------------------------------

        [Fact]
        public void WideningAgrees() =>
            LanesAgree<ConfWiden, ConfWidenDto>(
                new ConfWiden { Amount = 7, Maybe = 3, Small = 5 },
                d => d.Amount, d => d.Maybe, d => d.Small);

        [Fact]
        public void WideningANullNullableAgrees() =>
            LanesAgree<ConfWiden, ConfWidenDto>(
                new ConfWiden { Amount = 0, Maybe = null, Small = 0 },
                d => d.Amount, d => d.Maybe, d => d.Small);

        [Fact]
        public void AnEnumIntoAStringAgrees() =>
            LanesAgree<ConfEnumText, ConfEnumTextDto>(
                new ConfEnumText { Colour = ConfColour.Amber, Maybe = ConfColour.Teal },
                d => d.Colour, d => d.Maybe);

        [Fact]
        public void ANullEnumIntoAStringAgrees() =>
            LanesAgree<ConfEnumText, ConfEnumTextDto>(
                new ConfEnumText { Colour = ConfColour.Unset, Maybe = null },
                d => d.Colour, d => d.Maybe);

        [Fact]
        public void AnEnumIntoADifferentEnumAgrees() =>
            LanesAgree<ConfCrossEnum, ConfCrossEnumDto>(
                new ConfCrossEnum { Colour = ConfLeft.Amber, Maybe = ConfLeft.Teal },
                d => d.Colour, d => d.Maybe);

        [Fact]
        public void ANullCrossEnumAgrees() =>
            LanesAgree<ConfCrossEnum, ConfCrossEnumDto>(
                new ConfCrossEnum { Colour = ConfLeft.Unset, Maybe = null },
                d => d.Colour, d => d.Maybe);

        [Fact]
        public void ADateTimeIntoAnOffsetAgrees() =>
            LanesAgree<ConfStamp, ConfStampDto>(
                new ConfStamp
                {
                    At = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
                    Maybe = new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc),
                },
                d => d.At, d => d.Maybe);

        [Fact]
        public void ANullDateTimeIntoAnOffsetAgrees() =>
            LanesAgree<ConfStamp, ConfStampDto>(
                new ConfStamp { At = default, Maybe = null },
                d => d.At, d => d.Maybe);

        [Fact]
        public void ANestedObjectAgrees() =>
            LanesAgree<ConfNest, ConfNestDto>(
                new ConfNest { Inner = new ConfInner2 { City = "Cebu", Deep = new ConfDeep { Iso = "PH" } } },
                d => d.Inner!.City, d => d.Inner!.Deep!.Iso);

        [Fact]
        public void ANullNestedObjectAgrees() =>
            LanesAgree<ConfNest, ConfNestDto>(new ConfNest { Inner = null }, d => d.Inner);

        [Fact]
        public void ANestedObjectWithANullChildAgrees() =>
            LanesAgree<ConfNest, ConfNestDto>(
                new ConfNest { Inner = new ConfInner2 { City = "Cebu", Deep = null } },
                d => d.Inner!.City, d => d.Inner!.Deep);

        [Fact]
        public void ACollectionAgrees() =>
            LanesAgree<ConfList, ConfListDto>(
                new ConfList
                {
                    Items = { new ConfItem { Sku = "a", Qty = 1 }, new ConfItem { Sku = "b", Qty = 2 } },
                    Numbers = new[] { 3, 4, 5 },
                },
                d => d.Items.Count, d => d.Items[0].Sku, d => d.Items[1].Qty,
                d => d.Numbers.Count, d => d.Numbers[2]);

        [Fact]
        public void AnEmptyCollectionAgrees() =>
            LanesAgree<ConfList, ConfListDto>(
                new ConfList(), d => d.Items.Count, d => d.Numbers.Count);

        [Fact]
        public void FlatteningAgrees() =>
            LanesAgree<ConfFlatten, ConfFlattenDto>(
                new ConfFlatten { Inner = new ConfInner2 { City = "Cebu", Deep = new ConfDeep { Iso = "PH" } } },
                d => d.InnerCity, d => d.InnerDeepIso);

        [Fact]
        public void FlatteningThroughANullIntermediateAgrees() =>
            // The one most likely to drift: the engine yields the destination default rather than
            // throwing, and generated code has to write that guard out by hand at every hop.
            LanesAgree<ConfFlatten, ConfFlattenDto>(
                new ConfFlatten { Inner = null }, d => d.InnerCity, d => d.InnerDeepIso);

        [Fact]
        public void FlatteningThroughAPartiallyNullPathAgrees() =>
            LanesAgree<ConfFlatten, ConfFlattenDto>(
                new ConfFlatten { Inner = new ConfInner2 { City = "Cebu", Deep = null } },
                d => d.InnerCity, d => d.InnerDeepIso);

        [Fact]
        public void AFlattenedLeafNeedingTheNullableLiftAgrees() =>
            LanesAgree<ConfLift, ConfLiftDto>(
                new ConfLift { Inner = new ConfLiftInner { Count = 42 } }, d => d.InnerCount);

        [Fact]
        public void ADestinationEnumWithCaseDifferingNamesAgrees() =>
            LanesAgree<ConfCaseEnum, ConfCaseEnumDto>(
                new ConfCaseEnum { Value = ConfCaseFrom.Bravo }, d => d.Value);

        [Fact]
        public void IgnoreMapAndMapFromAgree() =>
            // A privilege escalation if the generated lane regresses: IsAdmin arrives true from a
            // request body the engine would have refused, on every call site, process-wide, because
            // the module initializer registers the pair before user code runs.
            LanesAgree<ConfControlled, ConfControlledDto>(
                new ConfControlled { Id = 4, IsAdmin = true, Actual = "real", Decoy = "decoy" },
                d => d.Id, d => d.IsAdmin, d => d.Decoy);

        // ---- refusals ---------------------------------------------------------------------------

        public class ConfInner { public string City { get; set; } = ""; }
        public class ConfInnerDto { public string City { get; set; } = ""; }
        public class ConfNested { public ConfInner? Inner { get; set; } }
        public class ConfNestedDto { public ConfInnerDto? Inner { get; set; } }

        [Fact]
        public void AnUndeclaredPairStillMapsThroughTheEngine()
        {
            // The property that makes the attribute safe to add or leave off: a pair nobody declared
            // is untouched, and the call site is the same either way. Widening and nesting are both
            // emitted rules now, so this pair is undeclared rather than refused.
            var dto = ((object)new ConfNested { Inner = new ConfInner { City = "Cebu" } }).MapTo<ConfNestedDto>();

            Assert.Equal("Cebu", dto!.Inner?.City);
        }

        [Fact]
        public void EveryDeclaredPairWasActuallyGenerated()
        {
            // Without this the suite is decorative. A refused pair falls back to the runtime engine,
            // which is the same lane the comparison uses, so both sides agree and every test passes.
            // Making the generator's name matching case sensitive, which stops it matching anything
            // on one of these pairs, left all nineteen tests green until this was added.
            var registry = typeof(Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                !.GetValue(null)!;

            var keys = ((System.Collections.IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .ToArray();

            var missing = typeof(LaneConformanceTests).Assembly
                .GetCustomAttributes(typeof(MapsicleGenerateAttribute), false)
                .Cast<MapsicleGenerateAttribute>()
                .Where(a => a.SourceType.Name.StartsWith("Conf", StringComparison.Ordinal))
                .Where(a => !keys.Any(k => k.Contains(a.SourceType.Name, StringComparison.Ordinal)
                                        && k.Contains(a.DestinationType.Name, StringComparison.Ordinal)))
                .Select(a => $"{a.SourceType.Name} into {a.DestinationType.Name}")
                .ToArray();

            Assert.True(missing.Length == 0,
                "these pairs were declared for generation and the generator refused them, so the "
                + "conformance comparison below is running the runtime lane against itself:\n  "
                + string.Join("\n  ", missing));
        }

        [Fact]
        public void TheTableCoversEveryPairTheAssemblyDeclares()
        {
            // A pair added to the declarations without a row here is the drift this file exists to
            // prevent: it would be generated, never compared, and the suite would stay green.
            var declared = typeof(LaneConformanceTests).Assembly
                .GetCustomAttributes(typeof(MapsicleGenerateAttribute), false)
                .Cast<MapsicleGenerateAttribute>()
                .Select(a => a.SourceType.Name)
                .Where(n => n.StartsWith("Conf", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            var covered = new[]
            {
                nameof(ConfCaseEnum), nameof(ConfCasing), nameof(ConfControlled), nameof(ConfCrossEnum),
                nameof(ConfDerived), nameof(ConfEnumText), nameof(ConfFlat), nameof(ConfFlatten),
                nameof(ConfKinds), nameof(ConfLift), nameof(ConfList), nameof(ConfNest),
                nameof(ConfNullable), nameof(ConfPartial), nameof(ConfStamp), nameof(ConfWiden),
            };

            Assert.Equal(covered, declared);
        }
    }
}
