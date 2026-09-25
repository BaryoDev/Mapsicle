using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    public class ImscRolesSrc { public List<string> Roles { get; set; } = new(); }
    public class ImscRolesDst { [IgnoreMap] public List<string> Roles { get; } = new(); }

    public class ImscFieldSrc { public bool IsAdmin; public string Name = ""; }
    public class ImscFieldDst { [IgnoreMap] public bool IsAdmin { get; set; } public string Name { get; set; } = ""; }

    public class ImscOpenRolesSrc { public List<string> Roles { get; set; } = new(); }
    public class ImscOpenRolesDst { public List<string> Roles { get; } = new(); }

    public class ImscShadowSrc { public string Real { get; set; } = ""; public string Alias = ""; }
    public class ImscShadowDst { [MapFrom(nameof(ImscShadowSrc.Real))] public string Alias { get; set; } = ""; }

    /// <summary>
    /// [IgnoreMap] on the two passes that run beside the property bindings: filling a getter-only
    /// collection, and copying a public source field onto a destination property.
    /// </summary>
    /// <remarks>
    /// Both passes matched on name alone, so an ignored Roles list was filled and an ignored IsAdmin
    /// was copied from a source field, on MapTo, on MapperFactory and on in-place Map.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class IgnoreMapSideChannelTests
    {
        public IgnoreMapSideChannelTests() => Mapper.ClearCache();

        private static T? ViaFactory<T>(object source)
        {
            using var mapper = MapperFactory.Create();
            return mapper.MapTo<T>(source);
        }

        [Fact]
        public void MapTo_LeavesAnIgnoredGetterOnlyCollectionEmpty() =>
            Assert.Empty(((object)new ImscRolesSrc { Roles = { "admin" } }).MapTo<ImscRolesDst>()!.Roles);

        [Fact]
        public void Factory_LeavesAnIgnoredGetterOnlyCollectionEmpty() =>
            Assert.Empty(ViaFactory<ImscRolesDst>(new ImscRolesSrc { Roles = { "admin" } })!.Roles);

        [Fact]
        public void InPlaceMap_LeavesAnIgnoredGetterOnlyCollectionEmpty() =>
            Assert.Empty(new ImscRolesSrc { Roles = { "admin" } }.Map(new ImscRolesDst()).Roles);

        [Fact]
        public void MapTo_DoesNotCopyAFieldOntoAnIgnoredProperty()
        {
            var dst = ((object)new ImscFieldSrc { IsAdmin = true, Name = "n" }).MapTo<ImscFieldDst>()!;
            Assert.False(dst.IsAdmin);
            Assert.Equal("n", dst.Name);
        }

        [Fact]
        public void Factory_DoesNotCopyAFieldOntoAnIgnoredProperty()
        {
            var dst = ViaFactory<ImscFieldDst>(new ImscFieldSrc { IsAdmin = true, Name = "n" })!;
            Assert.False(dst.IsAdmin);
            Assert.Equal("n", dst.Name);
        }

        [Fact]
        public void InPlaceMap_DoesNotCopyAFieldOntoAnIgnoredProperty()
        {
            var dst = new ImscFieldSrc { IsAdmin = true, Name = "n" }.Map(new ImscFieldDst());
            Assert.False(dst.IsAdmin);
            Assert.Equal("n", dst.Name);
        }

        [Fact]
        public void MapFrom_WinsOverASourceFieldWithTheDestinationName()
        {
            var source = new ImscShadowSrc { Real = "real", Alias = "decoy" };

            Assert.Equal("real", ((object)source).MapTo<ImscShadowDst>()!.Alias);
            Assert.Equal("real", ViaFactory<ImscShadowDst>(source)!.Alias);
            Assert.Equal("real", source.Map(new ImscShadowDst()).Alias);
        }

        // Controls: the same shapes without the attribute still map on every lane.

        [Fact]
        public void Control_AGetterOnlyCollectionWithoutIgnoreMapIsStillFilled()
        {
            var source = new ImscOpenRolesSrc { Roles = { "user" } };

            Assert.Equal(new[] { "user" }, ((object)source).MapTo<ImscOpenRolesDst>()!.Roles);
            Assert.Equal(new[] { "user" }, ViaFactory<ImscOpenRolesDst>(source)!.Roles);
            Assert.Equal(new[] { "user" }, source.Map(new ImscOpenRolesDst()).Roles);
        }

        [Fact]
        public void Control_AFieldIsStillCopiedOntoAPropertyWithoutIgnoreMap()
        {
            var source = new ImscFieldSrc { Name = "n" };

            Assert.Equal("n", ((object)source).MapTo<ImscFieldDst>()!.Name);
            Assert.Equal("n", ViaFactory<ImscFieldDst>(source)!.Name);
            Assert.Equal("n", source.Map(new ImscFieldDst()).Name);
        }
    }
}
