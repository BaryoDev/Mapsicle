using System.Text;
using System.Text.Json;
using Mapsicle.Fluent;
using Mapsicle.Json;
using Xunit;

namespace Mapsicle.Json.Tests;

public class JsonMappingTests
{
    #region Test Models

    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class ApiResponse
    {
        public int user_id { get; set; }
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
    }

    public class Order
    {
        public int OrderId { get; set; }
        public decimal Total { get; set; }
        public List<OrderItem> Items { get; set; } = new();
    }

    public class OrderItem
    {
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class OrderDto
    {
        public int OrderId { get; set; }
        public decimal Total { get; set; }
    }

    #endregion

    #region MapToJson Tests

    [Fact]
    public void MapToJson_SingleObject_ReturnsJsonString()
    {
        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com"
        };

        var json = user.MapToJson<UserDto>();

        Assert.NotNull(json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"firstName\":\"John\"", json);
        Assert.Contains("\"lastName\":\"Doe\"", json);
        Assert.DoesNotContain("email", json.ToLower()); // Email not in DTO
    }

    [Fact]
    public void MapToJson_NullSource_ReturnsNull()
    {
        User? user = null;

        var json = user.MapToJson<UserDto>();

        Assert.Null(json);
    }

    [Fact]
    public void MapToJson_WithCustomOptions_UsesOptions()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        var json = user.MapToJson<UserDto>(options);

        Assert.NotNull(json);
        Assert.Contains("\n", json); // Indented output has newlines
    }

    [Fact]
    public void MapToJson_WithMapper_ReturnsJsonString()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDto>();
        });
        var mapper = config.CreateMapper();

        var user = new User { Id = 1, FirstName = "Jane", LastName = "Smith" };

        var json = mapper.MapToJson<UserDto>(user);

        Assert.NotNull(json);
        Assert.Contains("\"firstName\":\"Jane\"", json);
    }

    [Fact]
    public void MapToJsonBytes_ReturnsUtf8Bytes()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var bytes = user.MapToJsonBytes<UserDto>();

        Assert.NotNull(bytes);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"id\":1", json);
    }

    [Fact]
    public async Task MapToJsonAsync_WritesToStream()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        using var stream = new MemoryStream();

        await user.MapToJsonAsync<UserDto>(stream);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        Assert.Contains("\"id\":1", json);
    }

    #endregion

    #region MapFromJson Tests

    [Fact]
    public void MapFromJson_ValidJson_ReturnsObject()
    {
        var json = "{\"id\":1,\"firstName\":\"John\",\"lastName\":\"Doe\",\"email\":\"john@test.com\"}";

        var dto = json.MapFromJson<User, UserDto>();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.Equal("John", dto.FirstName);
        Assert.Equal("Doe", dto.LastName);
    }

    [Fact]
    public void MapFromJson_NullJson_ReturnsDefault()
    {
        string? json = null;

        var dto = json.MapFromJson<User, UserDto>();

        Assert.Null(dto);
    }

    [Fact]
    public void MapFromJson_EmptyJson_ReturnsDefault()
    {
        var json = "";

        var dto = json.MapFromJson<User, UserDto>();

        Assert.Null(dto);
    }

    [Fact]
    public void MapFromJson_WithMapper_ReturnsObject()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDto>();
        });
        var mapper = config.CreateMapper();
        var json = "{\"id\":2,\"firstName\":\"Jane\",\"lastName\":\"Smith\"}";

        var dto = mapper.MapFromJson<User, UserDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(2, dto.Id);
    }

    [Fact]
    public async Task MapFromJsonAsync_ReadsFromStream()
    {
        var json = "{\"id\":1,\"firstName\":\"John\",\"lastName\":\"Doe\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var dto = await stream.MapFromJsonAsync<User, UserDto>();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public void MapFromJsonBytes_ReturnsObject()
    {
        var json = "{\"id\":1,\"firstName\":\"John\",\"lastName\":\"Doe\"}";
        var bytes = Encoding.UTF8.GetBytes(json);

        var dto = ((ReadOnlySpan<byte>)bytes).MapFromJsonBytes<User, UserDto>();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    #endregion

    #region Collection Mapping Tests

    [Fact]
    public void MapCollectionToJson_ReturnsJsonArray()
    {
        var users = new List<object>
        {
            new User { Id = 1, FirstName = "John", LastName = "Doe" },
            new User { Id = 2, FirstName = "Jane", LastName = "Smith" }
        };

        var json = users.MapCollectionToJson<UserDto>();

        Assert.NotNull(json);
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        Assert.Contains("\"id\":1", json);
        Assert.Contains("\"id\":2", json);
    }

    [Fact]
    public void MapCollectionFromJson_ReturnsListOfObjects()
    {
        var json = "[{\"id\":1,\"firstName\":\"John\"},{\"id\":2,\"firstName\":\"Jane\"}]";

        var dtos = json.MapCollectionFromJson<User, UserDto>();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal(2, dtos[1].Id);
    }

    [Fact]
    public void MapCollectionToJson_NullSource_ReturnsNull()
    {
        List<object>? users = null;

        var json = users.MapCollectionToJson<UserDto>();

        Assert.Null(json);
    }

    [Fact]
    public void MapCollectionFromJson_NullJson_ReturnsEmptyList()
    {
        string? json = null;

        var dtos = json.MapCollectionFromJson<User, UserDto>();

        Assert.Empty(dtos);
    }

    #endregion

    #region JsonDocument Mapping Tests

    [Fact]
    public void MapFromJsonDocument_ReturnsObject()
    {
        var json = "{\"id\":1,\"firstName\":\"John\",\"lastName\":\"Doe\"}";
        using var document = JsonDocument.Parse(json);

        var dto = document.MapFromJsonDocument<UserDto>();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public void MapFromJsonDocument_NullDocument_ReturnsDefault()
    {
        JsonDocument? document = null;

        var dto = document.MapFromJsonDocument<UserDto>();

        Assert.Null(dto);
    }

    [Fact]
    public void MapFromJsonElement_ReturnsObject()
    {
        var json = "{\"id\":1,\"firstName\":\"John\",\"lastName\":\"Doe\"}";
        using var document = JsonDocument.Parse(json);

        var dto = document.RootElement.MapFromJsonElement<UserDto>();

        Assert.NotNull(dto);
        Assert.Equal("John", dto.FirstName);
    }

    #endregion

    #region JsonMappingOptions Tests

    [Fact]
    public void CamelCaseOptions_SerializesWithCamelCase()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var json = JsonSerializer.Serialize(user, JsonMappingOptions.CamelCase);

        Assert.Contains("\"firstName\":", json);
        Assert.DoesNotContain("\"FirstName\":", json);
    }

    [Fact]
    public void SnakeCaseOptions_SerializesWithSnakeCase()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var json = JsonSerializer.Serialize(user, JsonMappingOptions.SnakeCase);

        Assert.Contains("\"first_name\":", json);
    }

    [Fact]
    public void IndentedOptions_SerializesWithIndentation()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var json = JsonSerializer.Serialize(user, JsonMappingOptions.Indented);

        Assert.Contains("\n", json);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void MapToJson_ComplexNestedObject_SerializesCorrectly()
    {
        var order = new Order
        {
            OrderId = 1,
            Total = 99.99m,
            Items = new List<OrderItem>
            {
                new() { ProductName = "Widget", Quantity = 2 }
            }
        };

        var json = order.MapToJson<OrderDto>();

        Assert.NotNull(json);
        Assert.Contains("\"orderId\":1", json);
        Assert.Contains("99.99", json);
    }

    [Fact]
    public void MapFromJson_PartialJson_MapsAvailableProperties()
    {
        var json = "{\"id\":1}"; // Missing firstName and lastName

        var dto = json.MapFromJson<User, UserDto>();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.Equal(string.Empty, dto.FirstName);
    }

    [Fact]
    public void MapToJson_WithSnakeCaseApi_HandlesCorrectly()
    {
        var response = new ApiResponse
        {
            user_id = 1,
            first_name = "John",
            last_name = "Doe"
        };

        var json = response.MapToJson<UserDto>(JsonMappingOptions.SnakeCase);

        Assert.NotNull(json);
        // The DTO won't have matching properties due to naming convention mismatch
        // This tests that serialization doesn't throw
    }

    [Fact]
    public void DefaultOptions_CanBeOverridden()
    {
        var originalDefault = JsonMappingExtensions.DefaultOptions;
        try
        {
            JsonMappingExtensions.DefaultOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var user = new User { Id = 1, FirstName = "John" };
            var json = user.MapToJson<UserDto>();

            Assert.NotNull(json);
            Assert.Contains("\n", json); // Should be indented
        }
        finally
        {
            JsonMappingExtensions.DefaultOptions = originalDefault;
        }
    }

    #endregion
}
