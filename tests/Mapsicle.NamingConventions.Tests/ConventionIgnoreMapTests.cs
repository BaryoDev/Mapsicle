using Mapsicle;
using Mapsicle.Fluent;
using Mapsicle.NamingConventions;
using Xunit;

namespace Mapsicle.NamingConventions.Tests
{
#pragma warning disable IDE1006
    public class NcIgSnake
    {
        public string user_name { get; set; } = "";
        public bool is_admin { get; set; }
    }
#pragma warning restore IDE1006

    public class NcIgPascalSource
    {
        public string UserName { get; set; } = "";
        public bool IsAdmin { get; set; }
    }

    public class NcIgPascal
    {
        public string UserName { get; set; } = "";
        [IgnoreMap] public bool IsAdmin { get; set; }
    }

    public class NcIgPascalFluent
    {
        public string? UserName { get; set; }
        public bool IsAdmin { get; set; }
    }

    [Collection("StaticMapperTests")]
    public class ConventionIgnoreMapTests
    {
        public ConventionIgnoreMapTests()
        {
            Mapper.ClearCache();
            NamingConventionExtensions.ClearMappingCache();
        }

        private static NcIgSnake Snake() => new NcIgSnake { user_name = "ann", is_admin = true };

        [Fact]
        public void Control_SnakeToPascal_MapsMembersThatAreNotIgnored()
        {
            var dto = Snake().MapWithConvention<NcIgSnake, NcIgPascal>(NamingConvention.SnakeCase, NamingConvention.PascalCase)!;
            Assert.Equal("ann", dto.UserName);
        }

        [Fact]
        public void SnakeToPascal_HonoursIgnoreMap()
        {
            var dto = Snake().MapWithConvention<NcIgSnake, NcIgPascal>(NamingConvention.SnakeCase, NamingConvention.PascalCase)!;
            Assert.False(dto.IsAdmin);
        }

        [Fact]
        public void ExactNameMatch_HonoursIgnoreMap()
        {
            var source = new NcIgPascalSource { UserName = "ann", IsAdmin = true };
            var dto = source.MapWithConvention<NcIgPascalSource, NcIgPascal>(NamingConvention.PascalCase, NamingConvention.PascalCase)!;
            Assert.Equal("ann", dto.UserName);
            Assert.False(dto.IsAdmin);
        }

        [Fact]
        public void MapperOverload_HonoursIgnoreMap()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<NcIgSnake, NcIgPascal>()).CreateMapper();
            var dto = mapper.MapWithConvention<NcIgSnake, NcIgPascal>(Snake(), NamingConvention.SnakeCase, NamingConvention.PascalCase)!;
            Assert.False(dto.IsAdmin);
        }

        private static NcIgPascalFluent ViaMapper(bool ignoreAdmin)
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                var map = cfg.CreateMap<NcIgSnake, NcIgPascalFluent>();
                if (ignoreAdmin) map.ForMember(d => d.IsAdmin, o => o.Ignore());
            }).CreateMapper();
            return mapper.MapWithConvention<NcIgSnake, NcIgPascalFluent>(Snake(), NamingConvention.SnakeCase, NamingConvention.PascalCase)!;
        }

        [Fact]
        public void Control_MapperOverload_FillsMembersTheConfigurationDoesNotIgnore()
        {
            var dto = ViaMapper(ignoreAdmin: false);
            Assert.Equal("ann", dto.UserName);
            Assert.True(dto.IsAdmin);
        }

        [Fact]
        public void MapperOverload_HonoursFluentIgnore()
        {
            var dto = ViaMapper(ignoreAdmin: true);
            Assert.Equal("ann", dto.UserName);
            Assert.False(dto.IsAdmin);
        }
    }
}
