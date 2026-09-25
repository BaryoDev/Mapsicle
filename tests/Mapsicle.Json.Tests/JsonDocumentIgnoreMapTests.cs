using System.Text.Json;
using Mapsicle;
using Mapsicle.Json;
using Xunit;

namespace Mapsicle.Json.Tests
{
    public class JdIgIn { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }

    public class JdIgProfile
    {
        public string Bio { get; set; } = "";
        [IgnoreMap] public string Role { get; set; } = "user";
    }

    public class JdIgOut
    {
        public string Name { get; set; } = "";
        [IgnoreMap] public bool IsAdmin { get; set; }
        public JdIgProfile? Profile { get; set; }
    }

    [Collection("StaticMapperTests")]
    public class JsonDocumentIgnoreMapTests
    {
        private const string Body =
            "{\"name\":\"ann\",\"isAdmin\":true,\"profile\":{\"bio\":\"hi\",\"role\":\"admin\"}}";

        public JsonDocumentIgnoreMapTests() => Mapper.ClearCache();

        [Fact]
        public void Control_MapFromJson_HonoursIgnoreMap()
        {
            var dto = "{\"name\":\"ann\",\"isAdmin\":true}".MapFromJson<JdIgIn, JdIgOut>()!;
            Assert.Equal("ann", dto.Name);
            Assert.False(dto.IsAdmin);
        }

        [Fact]
        public void Control_MapFromJsonDocument_StillMapsMembersThatAreNotIgnored()
        {
            using var doc = JsonDocument.Parse(Body);
            var dto = doc.MapFromJsonDocument<JdIgOut>()!;
            Assert.Equal("ann", dto.Name);
            Assert.Equal("hi", dto.Profile!.Bio);
        }

        [Fact]
        public void MapFromJsonDocument_HonoursIgnoreMap()
        {
            using var doc = JsonDocument.Parse(Body);
            Assert.False(doc.MapFromJsonDocument<JdIgOut>()!.IsAdmin);
        }

        [Fact]
        public void MapFromJsonElement_HonoursIgnoreMap()
        {
            using var doc = JsonDocument.Parse(Body);
            Assert.False(doc.RootElement.MapFromJsonElement<JdIgOut>()!.IsAdmin);
        }

        [Fact]
        public void MapFromJsonDocument_IgnoredNestedMemberKeepsItsInitialValue()
        {
            using var doc = JsonDocument.Parse(Body);
            Assert.Equal("user", doc.MapFromJsonDocument<JdIgOut>()!.Profile!.Role);
        }

        [Fact]
        public void MapFromJsonElement_WithCallerOptions_HonoursIgnoreMap()
        {
            using var doc = JsonDocument.Parse("{\"name\":\"ann\",\"is_admin\":true}");
            var dto = doc.RootElement.MapFromJsonElement<JdIgOut>(JsonMappingOptions.SnakeCase)!;
            Assert.Equal("ann", dto.Name);
            Assert.False(dto.IsAdmin);
        }

        [Fact]
        public void Control_CallerOptionsAreNotChanged()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            using var doc = JsonDocument.Parse(Body);
            doc.MapFromJsonDocument<JdIgOut>(options);

            Assert.True(JsonSerializer.Deserialize<JdIgOut>(Body, options)!.IsAdmin);
        }
    }
}
