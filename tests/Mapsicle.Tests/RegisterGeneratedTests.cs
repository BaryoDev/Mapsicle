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
    public class RegisterGeneratedTests : IDisposable
    {
        /// <summary>
        /// Forgets what this file registered, because registrations now outlive a cache clear.
        /// </summary>
        /// <remarks>
        /// That is the point of the design, and it makes these tests the only ones in the suite that
        /// can leak into others: three tests elsewhere assert an empty cache after ClearCache and
        /// saw this file's pairs still in it.
        /// </remarks>
        public void Dispose() => Mapper.ResetGeneratedRegistrations();

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
            Mapper.ResetGeneratedRegistrations();
            RegisterMarker();

            Assert.Equal(Marker, Sample().MapTo<RgSource, RgDest>()!.Name);
        }

        [Fact]
        public void TheUntypedDoorUsesIt()
        {
            // The call every README example uses. Registering only the typed cache would leave this
            // silently compiling its own mapper and returning the convention answer.
            Mapper.ResetGeneratedRegistrations();
            RegisterMarker();

            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ACollectionUsesIt()
        {
            Mapper.ResetGeneratedRegistrations();
            RegisterMarker();

            var mapped = ((IEnumerable)new List<RgSource> { Sample(), Sample() }).MapTo<RgDest>();

            Assert.Equal(2, mapped.Count);
            Assert.All(mapped, d => Assert.Equal(Marker, d.Name));
        }

        [Fact]
        public void ANestedMemberUsesIt()
        {
            Mapper.ResetGeneratedRegistrations();
            RegisterMarker();

            var dto = ((object)new RgHolder { Item = Sample() }).MapTo<RgHolderDest>();

            Assert.Equal(Marker, dto!.Item?.Name);
        }

        [Fact]
        public void ANestedMemberPicksUpARegistrationMadeAfterItFirstMapped()
        {
            // The order ANestedMemberUsesIt does not cover, and the one that was broken. A holder
            // caches what it resolved and keeps it while the source type and the cache generation
            // both still match. Registering dropped the compiled list loop and left the holders
            // alone, so a parent mapped before the registration kept invoking the delegate the
            // registration replaced, for the rest of the process.
            Mapper.ResetGeneratedRegistrations();
            Mapper.ClearCache();

            var before = ((object)new RgHolder { Item = Sample() }).MapTo<RgHolderDest>();
            Assert.Equal("convention-would-copy-this", before!.Item?.Name);

            RegisterMarker();

            var after = ((object)new RgHolder { Item = Sample() }).MapTo<RgHolderDest>();
            Assert.Equal(Marker, after!.Item?.Name);
        }

        [Fact]
        public async System.Threading.Tasks.Task ARegistrationSurvivesARacingFirstMap()
        {
            // Both calls complete and the registration used to lose. Thread A takes the cold path,
            // spends a slow Expression.Compile and writes the typed cache; thread B registers; A
            // finishes last and overwrites. It stayed lost for the rest of the process, because the
            // cold path never runs again, and it left the typed and untyped doors disagreeing.
            // Measured at roughly two thirds of three thousand interleavings before the fix.
            var lost = 0;

            for (var i = 0; i < 200; i++)
            {
                Mapper.ResetGeneratedRegistrations();
                Mapper.ClearCache();

                using var gate = new System.Threading.Barrier(2);
                var mapFirst = System.Threading.Tasks.Task.Run(() =>
                {
                    gate.SignalAndWait();
                    _ = Sample().MapTo<RgSource, RgDest>();
                });
                var register = System.Threading.Tasks.Task.Run(() =>
                {
                    gate.SignalAndWait();
                    RegisterMarker();
                });

                await System.Threading.Tasks.Task.WhenAll(mapFirst, register);

                if (Sample().MapTo<RgSource, RgDest>()!.Name != Marker) lost++;
            }

            Assert.True(lost == 0, $"the registration was lost to a racing first map {lost} times in 200");
        }

        [Fact]
        public void TheBoundedTypedCacheDoesNotEvictARegistration()
        {
            // First in, first out, and a module initializer runs first, so registrations were always
            // the oldest and went first. The rebuild path does not consult the registry, so under the
            // bounded cache a declared pair degraded to the engine permanently once enough other
            // pairs had been mapped.
            var previous = Mapper.UseLruCache;
            var previousSize = Mapper.MaxCacheSize;

            try
            {
                Mapper.ResetGeneratedRegistrations();
                Mapper.UseLruCache = true;
                Mapper.MaxCacheSize = 1;
                Mapper.ClearCache();

                RegisterMarker();
                Assert.Equal(Marker, Sample().MapTo<RgSource, RgDest>()!.Name);

                _ = new RgFillerOne { A = 1 }.MapTo<RgFillerOne, RgFillerOneDto>();
                _ = new RgFillerTwo { A = 2 }.MapTo<RgFillerTwo, RgFillerTwoDto>();

                Assert.Equal(Marker, Sample().MapTo<RgSource, RgDest>()!.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.MaxCacheSize = previousSize;
                Mapper.ClearCache();
            }
        }

        public class RgFillerOne { public int A { get; set; } }
        public class RgFillerOneDto { public int A { get; set; } }
        public class RgFillerTwo { public int A { get; set; } }
        public class RgFillerTwoDto { public int A { get; set; } }

        [Fact]
        public void RegisteringAfterMappingStillWinsUnderTheBoundedCache()
        {
            // The bounded cache stores through GetOrAdd, which keeps whichever entry arrived first.
            // That is right for compiled delegates and wrong for a registration that must supersede
            // one: with UseLruCache on, a pair mapped before the registration kept its compiled
            // delegate and the generated mapper never applied again.
            var previous = Mapper.UseLruCache;
            try
            {
                Mapper.ResetGeneratedRegistrations();
                Mapper.UseLruCache = true;
                Mapper.ClearCache();

                Assert.Equal("convention-would-copy-this", ((object)Sample()).MapTo<RgDest>()!.Name);

                RegisterMarker();

                Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }

        [Fact]
        public void RegisteringAfterThePairHasAlreadyMappedStillWins()
        {
            // A module initializer runs before user code, but a plugin loaded later should still win
            // for its own pairs rather than lose to whatever the engine compiled first.
            Mapper.ResetGeneratedRegistrations();

            Assert.Equal("convention-would-copy-this", ((object)Sample()).MapTo<RgDest>()!.Name);

            RegisterMarker();

            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ACollectionMappedBeforeRegistrationPicksItUpAfterwards()
        {
            // The list loop inlines the expression tree for a pair. A loop compiled before
            // registration would keep using the mapper this call replaced.
            Mapper.ResetGeneratedRegistrations();
            var source = new List<RgSource> { Sample() };

            Assert.Equal("convention-would-copy-this", ((IEnumerable)source).MapTo<RgDest>()[0].Name);

            RegisterMarker();

            Assert.Equal(Marker, ((IEnumerable)source).MapTo<RgDest>()[0].Name);
        }

        [Fact]
        public void ClearCacheKeepsIt()
        {
            // This asserted the opposite first, recording that a clear dropped the registration.
            // That was a design flaw rather than a behaviour worth keeping: the module initializer
            // that registered the pair has already run and will not run again, so anything calling
            // ClearCache would lose every generated mapper for the rest of the process and quietly
            // fall back to the expression builder. A generated mapper is a registration, not
            // something the engine compiled, so a clear empties the caches and puts it back.
            Mapper.ResetGeneratedRegistrations();
            RegisterMarker();
            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);

            Mapper.ClearCache();

            Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
        }

        [Fact]
        public void ChangingTheCacheModeKeepsItToo()
        {
            // Toggling UseLruCache reinitialises the caches, which is the other door into the same
            // problem.
            var previous = Mapper.UseLruCache;
            try
            {
                Mapper.ResetGeneratedRegistrations();
                RegisterMarker();
                Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);

                Mapper.UseLruCache = !previous;

                Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ResetGeneratedRegistrations();
            }
        }

        [Fact]
        public void ADerivedSourceStillResolvesItsOwnPair()
        {
            // The untyped door keys on the runtime type. A generated mapper for the base must not
            // be applied to a derived instance, which has its own members.
            Mapper.ResetGeneratedRegistrations();
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
                Mapper.ResetGeneratedRegistrations();
                RegisterMarker();
                Assert.Equal(Marker, ((object)Sample()).MapTo<RgDest>()!.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ResetGeneratedRegistrations();
            }
        }
    }
}
