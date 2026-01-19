using Mapsicle.NamingConventions;
using Xunit;

namespace Mapsicle.NamingConventions.Tests;

#region Test Models

public class PascalCaseSource
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public int OrderCount { get; set; }
}

public class CamelCaseDest
{
    public int userId { get; set; }
    public string firstName { get; set; } = "";
    public string lastName { get; set; } = "";
    public string emailAddress { get; set; } = "";
    public int orderCount { get; set; }
}

public class SnakeCaseSource
{
    public int user_id { get; set; }
    public string first_name { get; set; } = "";
    public string last_name { get; set; } = "";
    public string email_address { get; set; } = "";
    public int order_count { get; set; }
}

public class KebabCaseLikeDest
{
    // Note: C# doesn't support hyphens in property names, so we'll test the conversion logic
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
}

#endregion

public class NamingConventionTests
{
    #region PascalCase Tests

    [Theory]
    [InlineData("UserName", new[] { "User", "Name" })]
    [InlineData("FirstName", new[] { "First", "Name" })]
    [InlineData("ID", new[] { "I", "D" })]
    [InlineData("XMLParser", new[] { "X", "M", "L", "Parser" })]
    [InlineData("userId", new[] { "user", "Id" })]
    public void PascalCase_ToWords_SplitsCorrectly(string input, string[] expected)
    {
        var result = NamingConvention.PascalCase.ToWords(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(new[] { "User", "Name" }, "UserName")]
    [InlineData(new[] { "first", "name" }, "FirstName")]
    [InlineData(new[] { "ID" }, "Id")]
    public void PascalCase_FromWords_FormatsCorrectly(string[] input, string expected)
    {
        var result = NamingConvention.PascalCase.FromWords(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region CamelCase Tests

    [Theory]
    [InlineData("userName", new[] { "user", "Name" })]
    [InlineData("firstName", new[] { "first", "Name" })]
    [InlineData("orderCount", new[] { "order", "Count" })]
    public void CamelCase_ToWords_SplitsCorrectly(string input, string[] expected)
    {
        var result = NamingConvention.CamelCase.ToWords(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(new[] { "User", "Name" }, "userName")]
    [InlineData(new[] { "first", "name" }, "firstName")]
    [InlineData(new[] { "ORDER", "COUNT" }, "orderCount")]
    public void CamelCase_FromWords_FormatsCorrectly(string[] input, string expected)
    {
        var result = NamingConvention.CamelCase.FromWords(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region SnakeCase Tests

    [Theory]
    [InlineData("user_name", new[] { "user", "name" })]
    [InlineData("first_name", new[] { "first", "name" })]
    [InlineData("order_count", new[] { "order", "count" })]
    [InlineData("id", new[] { "id" })]
    public void SnakeCase_ToWords_SplitsCorrectly(string input, string[] expected)
    {
        var result = NamingConvention.SnakeCase.ToWords(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(new[] { "User", "Name" }, "user_name")]
    [InlineData(new[] { "first", "name" }, "first_name")]
    [InlineData(new[] { "ORDER", "COUNT" }, "order_count")]
    public void SnakeCase_FromWords_FormatsCorrectly(string[] input, string expected)
    {
        var result = NamingConvention.SnakeCase.FromWords(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region KebabCase Tests

    [Theory]
    [InlineData("user-name", new[] { "user", "name" })]
    [InlineData("first-name", new[] { "first", "name" })]
    [InlineData("order-count", new[] { "order", "count" })]
    public void KebabCase_ToWords_SplitsCorrectly(string input, string[] expected)
    {
        var result = NamingConvention.KebabCase.ToWords(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(new[] { "User", "Name" }, "user-name")]
    [InlineData(new[] { "first", "name" }, "first-name")]
    [InlineData(new[] { "ORDER", "COUNT" }, "order-count")]
    public void KebabCase_FromWords_FormatsCorrectly(string[] input, string expected)
    {
        var result = NamingConvention.KebabCase.FromWords(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Conversion Tests

    [Theory]
    [InlineData("UserName", "user_name")]
    [InlineData("FirstName", "first_name")]
    [InlineData("EmailAddress", "email_address")]
    public void Convert_PascalToSnake_Works(string input, string expected)
    {
        var result = NamingConvention.Convert(input, NamingConvention.PascalCase, NamingConvention.SnakeCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("user_name", "UserName")]
    [InlineData("first_name", "FirstName")]
    [InlineData("email_address", "EmailAddress")]
    public void Convert_SnakeToPascal_Works(string input, string expected)
    {
        var result = NamingConvention.Convert(input, NamingConvention.SnakeCase, NamingConvention.PascalCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("UserName", "userName")]
    [InlineData("FirstName", "firstName")]
    [InlineData("OrderCount", "orderCount")]
    public void Convert_PascalToCamel_Works(string input, string expected)
    {
        var result = NamingConvention.Convert(input, NamingConvention.PascalCase, NamingConvention.CamelCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("userName", "UserName")]
    [InlineData("firstName", "FirstName")]
    [InlineData("orderCount", "OrderCount")]
    public void Convert_CamelToPascal_Works(string input, string expected)
    {
        var result = NamingConvention.Convert(input, NamingConvention.CamelCase, NamingConvention.PascalCase);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsEmpty()
    {
        var result = NamingConvention.Convert("", NamingConvention.PascalCase, NamingConvention.SnakeCase);
        Assert.Equal("", result);
    }

    [Fact]
    public void Convert_NullString_ReturnsNull()
    {
        var result = NamingConvention.Convert(null!, NamingConvention.PascalCase, NamingConvention.SnakeCase);
        Assert.Null(result);
    }

    #endregion

    #region NamesMatch Tests

    [Theory]
    [InlineData("UserName", "user_name", true)]
    [InlineData("FirstName", "first_name", true)]
    [InlineData("UserId", "user_id", true)]
    [InlineData("UserName", "first_name", false)]
    public void NamesMatch_PascalAndSnake_Works(string pascal, string snake, bool expected)
    {
        var result = NamingConvention.NamesMatch(pascal, NamingConvention.PascalCase,
                                                   snake, NamingConvention.SnakeCase);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("userName", "UserName", true)]
    [InlineData("firstName", "FirstName", true)]
    [InlineData("orderId", "order_id", true)]
    public void NamesMatch_CrossConventions_Works(string name1, string name2, bool expected)
    {
        var result = NamingConvention.NamesMatch(name1, NamingConvention.CamelCase,
                                                   name2, NamingConvention.PascalCase);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void ConvertName_Extension_Works()
    {
        var result = "UserName".ConvertName(NamingConvention.PascalCase, NamingConvention.SnakeCase);
        Assert.Equal("user_name", result);
    }

    #endregion

    #region MapWithConvention Tests

    [Fact]
    public void MapWithConvention_PascalToCamel_MapsProperties()
    {
        var source = new PascalCaseSource
        {
            UserId = 1,
            FirstName = "John",
            LastName = "Doe",
            EmailAddress = "john@example.com",
            OrderCount = 5
        };

        var dest = source.MapWithConvention<PascalCaseSource, CamelCaseDest>(
            NamingConvention.PascalCase,
            NamingConvention.CamelCase);

        Assert.NotNull(dest);
        Assert.Equal(1, dest.userId);
        Assert.Equal("John", dest.firstName);
        Assert.Equal("Doe", dest.lastName);
        Assert.Equal("john@example.com", dest.emailAddress);
        Assert.Equal(5, dest.orderCount);
    }

    [Fact]
    public void MapWithConvention_SnakeToPascal_MapsProperties()
    {
        var source = new SnakeCaseSource
        {
            user_id = 42,
            first_name = "Jane",
            last_name = "Smith",
            email_address = "jane@example.com",
            order_count = 10
        };

        var dest = source.MapWithConvention<SnakeCaseSource, KebabCaseLikeDest>(
            NamingConvention.SnakeCase,
            NamingConvention.PascalCase);

        Assert.NotNull(dest);
        Assert.Equal(42, dest.UserId);
        Assert.Equal("Jane", dest.FirstName);
    }

    [Fact]
    public void MapWithConvention_NullSource_ReturnsDefault()
    {
        PascalCaseSource? source = null;

        var dest = source.MapWithConvention<PascalCaseSource, CamelCaseDest>(
            NamingConvention.PascalCase,
            NamingConvention.CamelCase);

        Assert.Null(dest);
    }

    [Fact]
    public void GetPropertyMappings_ReturnsCachedMappings()
    {
        // First call
        var mappings1 = NamingConventionExtensions.GetPropertyMappings<PascalCaseSource, CamelCaseDest>(
            NamingConvention.PascalCase,
            NamingConvention.CamelCase);

        // Second call should return cached
        var mappings2 = NamingConventionExtensions.GetPropertyMappings<PascalCaseSource, CamelCaseDest>(
            NamingConvention.PascalCase,
            NamingConvention.CamelCase);

        Assert.Same(mappings1, mappings2);
        Assert.True(mappings1.Count > 0);
    }

    [Fact]
    public void ClearMappingCache_Works()
    {
        // Should not throw
        NamingConventionExtensions.ClearMappingCache();
    }

    #endregion

    #region Convention Name Tests

    [Fact]
    public void ConventionNames_AreCorrect()
    {
        Assert.Equal("PascalCase", NamingConvention.PascalCase.Name);
        Assert.Equal("camelCase", NamingConvention.CamelCase.Name);
        Assert.Equal("snake_case", NamingConvention.SnakeCase.Name);
        Assert.Equal("kebab-case", NamingConvention.KebabCase.Name);
    }

    #endregion
}
