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

        // ---- refusals ---------------------------------------------------------------------------

        public class ConfWiden { public int Amount { get; set; } }
        public class ConfWidenDto { public long Amount { get; set; } }

        public class ConfInner { public string City { get; set; } = ""; }
        public class ConfInnerDto { public string City { get; set; } = ""; }
        public class ConfNested { public ConfInner? Inner { get; set; } }
        public class ConfNestedDto { public ConfInnerDto? Inner { get; set; } }

        [Fact]
        public void AWideningPairIsRefusedAndStillMapsThroughTheEngine()
        {
            // Not declared for generation, because the emitter has no widening rule and guessing one
            // is how the two lanes start disagreeing. The engine still performs it.
            var dto = ((object)new ConfWiden { Amount = 7 }).MapTo<ConfWidenDto>();

            Assert.Equal(7L, dto!.Amount);
        }

        [Fact]
        public void ANestedPairIsRefusedAndStillMapsThroughTheEngine()
        {
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
                nameof(ConfCasing), nameof(ConfDerived), nameof(ConfFlat),
                nameof(ConfKinds), nameof(ConfNullable), nameof(ConfPartial),
            };

            Assert.Equal(covered, declared);
        }
    }
}
