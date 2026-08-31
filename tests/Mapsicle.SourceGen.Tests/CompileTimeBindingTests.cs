using System;
using Mapsicle;
using Xunit;

namespace Mapsicle.SourceGen.Tests
{
    /// <summary>
    /// Proves an uncast <c>MapTo</c> binds to the generated extension rather than the engine.
    /// </summary>
    /// <remarks>
    /// Timing says it does, 32 ns against 89, but timing is not proof. The two routes normally
    /// produce identical output, which is the design goal and also what makes them impossible to
    /// tell apart by result.
    ///
    /// So they are forced to disagree. Registering a different mapper for the pair changes what the
    /// engine returns, because the engine looks the pair up. The generated extension has the
    /// mapping inline and never consults the registry, so it is unaffected. Whichever value comes
    /// back names the route that ran.
    ///
    /// That difference is worth knowing for its own sake: for a declared pair at a call site where
    /// the extension is in scope, a later RegisterGenerated no longer overrides anything.
    /// </remarks>
    [Collection("SourceGenBinding")]
    public class CompileTimeBindingTests : IDisposable
    {
        private const string EngineMarker = "FROM-THE-REGISTRY";

        public void Dispose() => Mapper.ResetGeneratedRegistrations();

        private static GenUser Sample() => new() { Id = 7, FirstName = "Ada", LastName = "Lovelace", IsActive = true };

        private static void RegisterAMapperTheEngineWillUse() =>
            Mapper.RegisterGenerated<GenUser, GenUserDto>(
                s => new GenUserDto { Id = s.Id, FirstName = EngineMarker }, requiresDepthTracking: false);

        [Fact]
        public void AnUncastCallBindsToTheGeneratedExtension()
        {
            RegisterAMapperTheEngineWillUse();

            // No cast, so the compiler chose. The extension maps inline and never reads the registry.
            var dto = Sample().MapTo<GenUserDto>();

            Assert.Equal("Ada", dto!.FirstName);
        }

        [Fact]
        public void ACastToObjectGoesThroughTheEngineInstead()
        {
            // The positive control. Casting to object removes the more specific extension from
            // consideration, so this must take the other route, or the test above proves nothing
            // about which route was taken.
            RegisterAMapperTheEngineWillUse();

            var dto = ((object)Sample()).MapTo<GenUserDto>();

            Assert.Equal(EngineMarker, dto!.FirstName);
        }

        [Fact]
        public void AnUndeclaredDestinationFallsThroughToTheEngine()
        {
            // The extension is generated per source type and handles the destinations that source
            // was declared with. Anything else has to reach the engine or the generator would have
            // broken every other mapping from that type.
            Mapper.ResetGeneratedRegistrations();

            var dto = Sample().MapTo<GenUserPartial>();

            Assert.Equal(7, dto!.Id);
            Assert.Equal("Ada", dto.FirstName);
        }

        public class GenUserPartial { public int Id { get; set; } public string FirstName { get; set; } = ""; }

        [Fact]
        public void ANullSourceStillReturnsTheDefault()
        {
            GenUser? nothing = null;

            Assert.Null(nothing.MapTo<GenUserDto>());
        }
    }

    [CollectionDefinition("SourceGenBinding", DisableParallelization = true)]
    public class SourceGenBindingCollection { }
}
