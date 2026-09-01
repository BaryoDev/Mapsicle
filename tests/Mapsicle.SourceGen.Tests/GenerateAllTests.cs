using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Xunit;

// One line instead of one per pair. Everything below is discovered from the call sites at the
// bottom of this file, and nothing here names a pair except the control, which is deliberately
// left undeclared and unused so it cannot be found.
[assembly: MapsicleGenerateAll]

namespace Mapsicle.SourceGen.Tests
{
    public class GaCountry { public string Iso { get; set; } = ""; }
    public class GaCountryDto { public string Iso { get; set; } = ""; }

    public class GaAddress { public string City { get; set; } = ""; public GaCountry Country { get; set; } = new(); }
    public class GaAddressDto { public string City { get; set; } = ""; public GaCountryDto Country { get; set; } = new(); }

    public class GaOrder { public int Id { get; set; } public GaAddress Ship { get; set; } = new(); }
    public class GaOrderDto { public int Id { get; set; } public GaAddressDto Ship { get; set; } = new(); }

    public class GaProduct { public string Sku { get; set; } = ""; }
    public class GaProductDto { public string Sku { get; set; } = ""; }

    // Never appears at a call site with a known receiver type, so scanning has nothing to find.
    // This is the control: without it, "scanning generated everything" and "everything was already
    // generated" look identical.
    public class GaUnseen { public int Id { get; set; } }
    public class GaUnseenDto { public int Id { get; set; } }

    /// <summary>
    /// <c>[assembly: MapsicleGenerateAll]</c> finds pairs from call sites instead of declarations.
    /// </summary>
    /// <remarks>
    /// The scan reads the receiver's static type, so it finds what the compiler can already see and
    /// nothing more. A receiver typed <c>object</c> is exactly the case it cannot help with, and that
    /// is not a gap to close: the type genuinely is not known until the call happens, which is the
    /// whole reason the runtime engine exists.
    /// </remarks>
    [Collection("SourceGenRegistry")]
    public class GenerateAllTests
    {
        private static List<string> Registry()
        {
            var registry = typeof(Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                !.GetValue(null)!;

            return ((System.Collections.IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .ToList();
        }

        [Fact]
        public void APairUsedAtAKnownCallSiteIsGeneratedWithoutBeingDeclared()
        {
            var keys = Registry();

            Assert.Contains(keys, k => k.Contains(nameof(GaOrder), StringComparison.Ordinal)
                                    && k.Contains(nameof(GaOrderDto), StringComparison.Ordinal));
            Assert.Contains(keys, k => k.Contains(nameof(GaProduct), StringComparison.Ordinal)
                                    && k.Contains(nameof(GaProductDto), StringComparison.Ordinal));
        }

        [Fact]
        public void APairWithNoResolvableCallSiteIsNotGenerated()
        {
            // The control. Scanning finds what the compiler can see, so a pair it never sees must
            // not appear, or the test above is passing for the wrong reason.
            Assert.DoesNotContain(Registry(), k => k.Contains(nameof(GaUnseen), StringComparison.Ordinal));
        }

        [Fact]
        public void AScannedPairAgreesWithTheEngine()
        {
            var source = new GaOrder { Id = 7, Ship = new GaAddress { City = "Cebu", Country = new GaCountry { Iso = "PH" } } };

            var generated = ((object)source).MapTo<GaOrderDto>();

            using var runtime = MapperFactory.Create();
            var interpreted = runtime.MapTo<GaOrderDto>(source);

            Assert.Equal(interpreted!.Id, generated!.Id);
            Assert.Equal(interpreted.Ship.City, generated.Ship.City);
            Assert.Equal(interpreted.Ship.Country.Iso, generated.Ship.Country.Iso);
        }

        [Fact]
        public void AnUnresolvableReceiverStillMapsThroughTheEngine()
        {
            object boxed = new GaUnseen { Id = 3 };

            var dto = boxed.MapTo<GaUnseenDto>();

            Assert.Equal(3, dto!.Id);
        }

        // The call sites the scan reads. They are ordinary calls, which is the point: nothing here
        // is written for the generator's benefit.
        private static GaOrderDto? MapAnOrder(GaOrder order) => order.MapTo<GaOrderDto>();

        private static GaProductDto? MapAProduct(GaProduct product) => product.MapTo<GaProductDto>();

        [Fact]
        public void TheCallSitesThisFileScansFromStillWork()
        {
            Assert.Equal(4, MapAnOrder(new GaOrder { Id = 4 })!.Id);
            Assert.Equal("PH-1", MapAProduct(new GaProduct { Sku = "PH-1" })!.Sku);
        }
    }
}
