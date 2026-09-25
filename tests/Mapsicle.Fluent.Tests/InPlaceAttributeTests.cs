using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    public class IpaSrc
    {
        public int Id { get; set; }
        public bool IsAdmin { get; set; }
        public string Real { get; set; } = "";
        public string Alias { get; set; } = "";
    }

    public class IpaDst
    {
        public int Id { get; set; }
        [IgnoreMap] public bool IsAdmin { get; set; }
        [MapFrom(nameof(IpaSrc.Real))] public string Alias { get; set; } = "";
    }

    public class IpaConfiguredSrc { public string Name { get; set; } = ""; }
    public class IpaConfiguredDst { [IgnoreMap] public string Name { get; set; } = ""; }

    /// <summary>
    /// IMapper.Map(source, destination) and the two attributes. It matched members on name alone,
    /// so an [IgnoreMap] IsAdmin was copied from the source and [MapFrom] read the decoy member.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class InPlaceAttributeTests
    {
        public InPlaceAttributeTests() => Mapper.ClearCache();

        [Fact]
        public void InPlaceMap_HonoursIgnoreMap_WithoutAConfiguredMap()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var dst = mapper.Map(new IpaSrc { Id = 1, IsAdmin = true }, new IpaDst());

            Assert.Equal(1, dst.Id);
            Assert.False(dst.IsAdmin);
        }

        [Fact]
        public void InPlaceMap_HonoursIgnoreMap_WithAConfiguredMap()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<IpaSrc, IpaDst>()).CreateMapper();
            var dst = mapper.Map(new IpaSrc { Id = 1, IsAdmin = true }, new IpaDst());

            Assert.Equal(1, dst.Id);
            Assert.False(dst.IsAdmin);
        }

        [Fact]
        public void InPlaceMap_LeavesTheExistingValueOfAnIgnoredMember()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var dst = mapper.Map(new IpaSrc { IsAdmin = false }, new IpaDst { IsAdmin = true });

            Assert.True(dst.IsAdmin);
        }

        [Fact]
        public void InPlaceMap_HonoursMapFrom()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var dst = mapper.Map(new IpaSrc { Real = "real", Alias = "decoy" }, new IpaDst());

            Assert.Equal("real", dst.Alias);
        }

        // Controls: the constructing map already honoured both, and explicit configuration on an
        // [IgnoreMap] member still wins on the in-place path.

        [Fact]
        public void Control_ConstructingMapHonoursBoth()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var dst = mapper.Map<IpaDst>(new IpaSrc { Id = 1, IsAdmin = true, Real = "real", Alias = "decoy" })!;

            Assert.Equal(1, dst.Id);
            Assert.False(dst.IsAdmin);
            Assert.Equal("real", dst.Alias);
        }

        [Fact]
        public void Control_ConfiguredMapFromStillWritesAnIgnoredMember()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<IpaConfiguredSrc, IpaConfiguredDst>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name + "!"))).CreateMapper();

            var dst = mapper.Map(new IpaConfiguredSrc { Name = "n" }, new IpaConfiguredDst());

            Assert.Equal("n!", dst.Name);
        }
    }
}
