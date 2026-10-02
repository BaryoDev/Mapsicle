using Mapsicle;
using Mapsicle.Fluent;
using Mapsicle.NamingConventions;
using Xunit;

namespace Mapsicle.NamingConventions.Tests
{
#pragma warning disable IDE1006
    public class WsSnake
    {
        public int user_id { get; set; }
        public int http_status { get; set; }
        public string address_line_1 { get; set; } = "";
        public string address_line2 { get; set; } = "";
        public string straße_name { get; set; } = "";
        public string display_name { get; set; } = "";
        public bool is_active { get; set; }
    }
#pragma warning restore IDE1006

    public class WsPascal
    {
        public int UserID { get; set; }
        public int HTTPStatus { get; set; }
        public string AddressLine1 { get; set; } = "";
        public string AddressLine2 { get; set; } = "";
        public string StraßeName { get; set; } = "";
    }

    public class WsInitialised
    {
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    public class WsUnrelated
    {
        public int UserKey { get; set; }
        public string Street { get; set; } = "";
    }

    [Collection("StaticMapperTests")]
    public class WordSplittingTests
    {
        public WordSplittingTests()
        {
            Mapper.ClearCache();
            NamingConventionExtensions.ClearMappingCache();
        }

        private static WsSnake Snake() => new WsSnake
        {
            user_id = 7,
            http_status = 200,
            address_line_1 = "1 Main St",
            address_line2 = "Unit 4",
            straße_name = "Hauptstraße",
            display_name = "ann",
            is_active = false
        };

        [Theory]
        [InlineData("ID", new[] { "ID" })]
        [InlineData("XMLParser", new[] { "XML", "Parser" })]
        [InlineData("HTTPServerID", new[] { "HTTP", "Server", "ID" })]
        [InlineData("UserID", new[] { "User", "ID" })]
        [InlineData("HTTP2Server", new[] { "HTTP2", "Server" })]
        [InlineData("AddressLine1", new[] { "Address", "Line1" })]
        [InlineData("StraßeName", new[] { "Straße", "Name" })]
        [InlineData("ÉcoleNom", new[] { "École", "Nom" })]
        public void PascalCase_ToWords_KeepsAcronymsAndNonAsciiLettersWhole(string input, string[] expected)
        {
            Assert.Equal(expected, NamingConvention.PascalCase.ToWords(input));
        }

        [Theory]
        [InlineData("userID", new[] { "user", "ID" })]
        [InlineData("straßeName", new[] { "straße", "Name" })]
        public void CamelCase_ToWords_KeepsAcronymsAndNonAsciiLettersWhole(string input, string[] expected)
        {
            Assert.Equal(expected, NamingConvention.CamelCase.ToWords(input));
        }

        [Theory]
        [InlineData("HTTPServerID", "http_server_id")]
        [InlineData("UserID", "user_id")]
        [InlineData("AddressLine1", "address_line1")]
        [InlineData("StraßeName", "straße_name")]
        public void Convert_PascalToSnake_KeepsAcronymsWhole(string input, string expected)
        {
            Assert.Equal(expected, input.ConvertName(NamingConvention.PascalCase, NamingConvention.SnakeCase));
        }

        [Theory]
        [InlineData("user_id", "UserID")]
        [InlineData("http_status", "HTTPStatus")]
        [InlineData("address_line_1", "AddressLine1")]
        [InlineData("address_line1", "AddressLine1")]
        [InlineData("straße_name", "StraßeName")]
        public void NamesMatch_SnakeAndPascal_IgnoresWhereTheWordsBreak(string snake, string pascal)
        {
            Assert.True(NamingConvention.NamesMatch(snake, NamingConvention.SnakeCase, pascal, NamingConvention.PascalCase));
        }

        [Theory]
        [InlineData("user_id", "UserKey")]
        [InlineData("address_line_1", "AddressLine2")]
        [InlineData("straße_name", "StrasseName")]
        public void Control_NamesMatch_StillRefusesDifferentNames(string snake, string pascal)
        {
            Assert.False(NamingConvention.NamesMatch(snake, NamingConvention.SnakeCase, pascal, NamingConvention.PascalCase));
        }

        [Fact]
        public void MapWithConvention_FillsAcronymDigitAndNonAsciiMembers()
        {
            var dto = Snake().MapWithConvention<WsSnake, WsPascal>(NamingConvention.SnakeCase, NamingConvention.PascalCase)!;

            Assert.Equal(7, dto.UserID);
            Assert.Equal(200, dto.HTTPStatus);
            Assert.Equal("1 Main St", dto.AddressLine1);
            Assert.Equal("Unit 4", dto.AddressLine2);
            Assert.Equal("Hauptstraße", dto.StraßeName);
        }

        [Fact]
        public void Control_MapWithConvention_LeavesUnrelatedMembersAlone()
        {
            var dto = Snake().MapWithConvention<WsSnake, WsUnrelated>(NamingConvention.SnakeCase, NamingConvention.PascalCase)!;

            Assert.Equal(0, dto.UserKey);
            Assert.Equal("", dto.Street);
        }

        [Fact]
        public void MapperOverload_FillsAMemberStillAtItsInitialValue()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<WsSnake, WsInitialised>()).CreateMapper();

            var dto = mapper.MapWithConvention<WsSnake, WsInitialised>(Snake(), NamingConvention.SnakeCase, NamingConvention.PascalCase)!;

            Assert.Equal("ann", dto.DisplayName);
            Assert.False(dto.IsActive);
        }

        [Fact]
        public void Control_MapperOverload_KeepsWhatTheMapperSet()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<WsSnake, WsInitialised>()
                    .ForMember(d => d.DisplayName, o => o.MapFrom(s => "from config"))).CreateMapper();

            var dto = mapper.MapWithConvention<WsSnake, WsInitialised>(Snake(), NamingConvention.SnakeCase, NamingConvention.PascalCase)!;

            Assert.Equal("from config", dto.DisplayName);
        }
    }
}
