using System;
using System.Linq;
using Mapsicle;
using Xunit;

// This assembly deliberately declares NOTHING. No [assembly: MapsicleGenerate], no
// [assembly: MapsicleGenerateAll]. The analyzer is referenced and the call sites below are the same
// shape scanning looks for, so if generation were ever on by default this is where it would show.
namespace Mapsicle.SourceGen.OptIn.Tests
{
    public class OiOrder { public int Id { get; set; } public string Reference { get; set; } = ""; }
    public class OiOrderDto { public int Id { get; set; } public string Reference { get; set; } = ""; }

    /// <summary>Generation stays off until an assembly asks for it.</summary>
    /// <remarks>
    /// A whole project for one property, because the property cannot be tested anywhere else. Every
    /// other test assembly carries <c>MapsicleGenerateAll</c>, so inside them "scanning ran because
    /// the attribute is present" and "scanning always runs" are indistinguishable: removing the
    /// attribute check left all of those green.
    ///
    /// The property is worth the project. Generation that turned itself on would emit a mapper for
    /// every resolvable call site in every consumer that installs the analyzer, changing their build
    /// time and their output without anyone opting in.
    /// </remarks>
    public class ScanningIsOptInTests
    {
        [Fact]
        public void NothingIsGeneratedWhenTheAssemblyDoesNotAskForIt()
        {
            var registry = typeof(Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                !.GetValue(null)!;

            var keys = ((System.Collections.IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .ToList();

            Assert.DoesNotContain(keys, k => k.Contains(nameof(OiOrder), StringComparison.Ordinal));
        }

        [Fact]
        public void AndTheCallSiteStillMapsThroughTheEngine()
        {
            // The positive control. "Nothing generated" is only the right answer if the mapping
            // still works, otherwise this test would pass on a library that does nothing at all.
            var dto = Map(new OiOrder { Id = 5, Reference = "SO-5" });

            Assert.Equal(5, dto!.Id);
            Assert.Equal("SO-5", dto.Reference);
        }

        // An ordinary call site with a statically known receiver, which is exactly what scanning
        // would pick up if it were running.
        private static OiOrderDto? Map(OiOrder order) => order.MapTo<OiOrderDto>();
    }
}
