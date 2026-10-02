using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Mapsicle.SourceGen.NetStandard.Fixture;
using Mapsicle.SourceGen.OldLanguage.Fixture;
using Xunit;

namespace Mapsicle.NetStandard.Tests
{
    /// <summary>
    /// Generated mappers in a consumer that targets netstandard2.0.
    /// </summary>
    /// <remarks>
    /// The two fixture projects are the test as much as the assertions are. Each one is a
    /// netstandard2.0 library with the generator installed, and neither used to build: the target
    /// has no <c>ModuleInitializerAttribute</c>, and its default language version has no nullable
    /// annotations.
    /// </remarks>
    public class NetStandardGeneratedTests
    {
        private static NsGenOrder Order() => new NsGenOrder
        {
            Id = 7,
            Name = "Ana",
            Status = NsGenStatus.Paid,
            Label = NsGenStatus.Paid,
            PlacedAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc),
            Country = new NsGenCountry { Iso = "PH" },
            Lines = { new NsGenLine { Sku = "a", Qty = 2 } },
            Extras = new[] { new NsGenLine { Sku = "b", Qty = 3 } },
        };

        private static bool IsRegistered(Type source, Type destination)
        {
            var registry = typeof(Mapper)
                .GetField("_generatedPairs", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            return ((IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .Any(k => k.Contains(source.Name) && k.Contains(destination.Name));
        }

        [Fact]
        public void TheFixtureIsANetStandardBuild()
        {
            var target = typeof(NsGenCalls).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

            Assert.Equal(".NETStandard,Version=v2.0", target!.FrameworkName);
        }

        [Fact]
        public void ADeclaredPairRegistersFromTheModuleInitializer()
        {
            NsGenCalls.Typed(Order());

            Assert.True(IsRegistered(typeof(NsGenOrder), typeof(NsGenOrderDto)));
        }

        [Fact]
        public void ADeclaredPairMapsLikeTheEngine()
        {
            var typed = NsGenCalls.Typed(Order());
            var untyped = NsGenCalls.Untyped(Order());

            using var runtime = MapperFactory.Create();
            var interpreted = runtime.MapTo<NsGenOrderDto>(Order())!;

            foreach (var generated in new[] { typed, untyped })
            {
                Assert.Equal(7L, generated.Id);
                Assert.Equal("Ana", generated.Name);
                Assert.Equal(NsGenStatusDto.Paid, generated.Status);
                Assert.Equal("Paid", generated.Label);
                Assert.Equal(interpreted.PlacedAt, generated.PlacedAt);
                Assert.Equal("PH", generated.Country.Iso);
                Assert.Equal("PH", generated.CountryIso);
                Assert.Equal("a", Assert.Single(generated.Lines).Sku);
                Assert.Equal(2L, generated.Lines[0].Qty);
                Assert.Equal("b", Assert.Single(generated.Extras).Sku);
                Assert.Equal(3L, generated.Extras[0].Qty);
            }

            Assert.Equal(7L, interpreted.Id);
            Assert.Equal(NsGenStatusDto.Paid, interpreted.Status);
            Assert.Equal("Paid", interpreted.Label);
            Assert.Equal("PH", interpreted.CountryIso);
            Assert.Equal(2L, interpreted.Lines[0].Qty);
            Assert.Equal(3L, interpreted.Extras[0].Qty);
        }

        [Fact]
        public void ADeclaredPairUnderAnOlderLanguageVersionMapsThroughTheEngine()
        {
            var mapped = NsOldCalls.Typed(new NsOldBox { X = 42, Name = "Ana" });

            Assert.Equal(42L, mapped.X);
            Assert.Equal("Ana", mapped.Name);
            Assert.False(IsRegistered(typeof(NsOldBox), typeof(NsOldBoxDto)));
        }
    }
}
