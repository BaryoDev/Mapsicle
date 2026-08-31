using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A mapper registered at compile time has to be the one the engine actually invokes.
    /// </summary>
    /// <remarks>
    /// The seam exists so a source generator can replace the factory without touching the engine.
    /// Its whole value is that the generated mapping is used, and the failure mode is silent: the
    /// engine compiles its own delegate, produces the same answer, and nobody notices the generated
    /// one was ignored. So every test here registers a mapper that returns a value convention could
    /// never produce, and asserts that value comes back.
    ///
    /// The plan for this seam had it filling the typed cache alone, which would have covered
    /// MapTo&lt;TSource, TDest&gt;() and missed MapTo&lt;TDest&gt;(object), nested members and
    /// collections. These are the tests that pin that down.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class RegisterGeneratedTests
    {
        public class RgSource { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class RgDest { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class RgHolder { public RgSource? Item { get; set; } }
        public class RgHolderDest { public RgDest? Item { get; set; } }

        private const string Marker = "FROM-GENERATED";

        private static void RegisterMarker() =>
            Mapper.RegisterGenerated<RgSource, RgDest>(
                s => new RgDest { Id = s.Id, Name = Marker }, requiresDepthTracking: false);

        private static RgSource Sample() => new() { Id = 7, Name = "convention-would-copy-this" };

        [Fact]
        public void TheTypedDoorUsesIt()
        {
            Mapper.ClearCache();
            RegisterMarker();

            Assert.Equal(Marker, Sample().MapTo<RgSource, RgDest>()!.Name);
        }

        [Fact]
        public void TheUntypedDoorUsesIt()
        {
            // The call every README example uses. Registering only the typed cache would leave this
            // silently compiling its own mapper and returning the convention answer.
            Mapper.ClearCache();
            RegisterMarker();

            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ACollectionUsesIt()
        {
            Mapper.ClearCache();
            RegisterMarker();

            var mapped = ((IEnumerable)new List<RgSource> { Sample(), Sample() }).MapTo<RgDest>();

            Assert.Equal(2, mapped.Count);
            Assert.All(mapped, d => Assert.Equal(Marker, d.Name));
        }

        [Fact]
        public void ANestedMemberUsesIt()
        {
            Mapper.ClearCache();
            RegisterMarker();

            var dto = ((object)new RgHolder { Item = Sample() }).MapTo<RgHolderDest>();

            Assert.Equal(Marker, dto!.Item?.Name);
        }

        [Fact]
        public void RegisteringAfterThePairHasAlreadyMappedStillWins()
        {
            // A module initializer runs before user code, but a plugin loaded later should still win
            // for its own pairs rather than lose to whatever the engine compiled first.
            Mapper.ClearCache();

            Assert.Equal("convention-would-copy-this", ((object)Sample()).MapTo<RgDest>()!.Name);

            RegisterMarker();

            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ACollectionMappedBeforeRegistrationPicksItUpAfterwards()
        {
            // The list loop inlines the expression tree for a pair. A loop compiled before
            // registration would keep using the mapper this call replaced.
            Mapper.ClearCache();
            var source = new List<RgSource> { Sample() };

            Assert.Equal("convention-would-copy-this", ((IEnumerable)source).MapTo<RgDest>()[0].Name);

            RegisterMarker();

            Assert.Equal(Marker, ((IEnumerable)source).MapTo<RgDest>()[0].Name);
        }

        [Fact]
        public void ClearCacheDiscardsIt()
        {
            // Registration is a cache fill, so clearing the cache drops it. A generator re-registers
            // from its module initializer, which has already run, so this is the documented
            // consequence rather than an accident.
            Mapper.ClearCache();
            RegisterMarker();
            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);

            Mapper.ClearCache();

            Assert.Equal("convention-would-copy-this", ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ADerivedSourceStillResolvesItsOwnPair()
        {
            // The untyped door keys on the runtime type. A generated mapper for the base must not
            // be applied to a derived instance, which has its own members.
            Mapper.ClearCache();
            RegisterMarker();

            var derived = new RgDerived { Id = 3, Name = "n", Extra = "e" };
            var dto = ((object)derived).MapTo<RgDerivedDest>();

            Assert.Equal("n", dto!.Name);
            Assert.Equal("e", dto.Extra);
        }

        public class RgDerived : RgSource { public string Extra { get; set; } = ""; }
        public class RgDerivedDest { public int Id { get; set; } public string Name { get; set; } = ""; public string Extra { get; set; } = ""; }

        [Fact]
        public void ANullMapperIsRefused()
        {
            Assert.Throws<ArgumentNullException>(
                () => Mapper.RegisterGenerated<RgSource, RgDest>(null!, false));
        }

        [Fact]
        public void TheBoundedCacheAcceptsARegistration()
        {
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = true;
            try
            {
                Mapper.ClearCache();
                RegisterMarker();
                Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }
    }
}
