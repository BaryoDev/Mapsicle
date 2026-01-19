using Mapsicle.NamingConventions;

namespace Mapsicle.Examples;

/// <summary>
/// Examples demonstrating Mapsicle.NamingConventions features.
/// </summary>
public static class NamingConventionExamples
{
    public static void Run()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("MAPSICLE.NAMINGCONVENTIONS EXAMPLES");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        SnakeToPascalExample();
        CamelToPascalExample();
        ConvertNameExample();
        NamesMatchExample();
        AllConventionsExample();
    }

    static void SnakeToPascalExample()
    {
        Console.WriteLine("1. Snake Case to Pascal Case (External API)");
        Console.WriteLine("-".PadRight(40, '-'));

        // Simulating a response from a Python/Ruby API
        var apiResponse = new ExternalApiResponse
        {
            user_id = 42,
            first_name = "John",
            last_name = "Doe",
            email_address = "john@example.com",
            order_count = 15,
            created_at = new DateTime(2024, 1, 15)
        };

        // Map with naming convention conversion
        var dto = apiResponse.MapWithConvention<ExternalApiResponse, InternalUserDto>(
            NamingConvention.SnakeCase,
            NamingConvention.PascalCase);

        Console.WriteLine("   External API (snake_case):");
        Console.WriteLine($"     user_id = {apiResponse.user_id}");
        Console.WriteLine($"     first_name = \"{apiResponse.first_name}\"");
        Console.WriteLine($"     email_address = \"{apiResponse.email_address}\"");
        Console.WriteLine();
        Console.WriteLine("   Internal DTO (PascalCase):");
        Console.WriteLine($"     UserId = {dto?.UserId}");
        Console.WriteLine($"     FirstName = \"{dto?.FirstName}\"");
        Console.WriteLine($"     EmailAddress = \"{dto?.EmailAddress}\"");
        Console.WriteLine();
    }

    static void CamelToPascalExample()
    {
        Console.WriteLine("2. Camel Case to Pascal Case (JavaScript API)");
        Console.WriteLine("-".PadRight(40, '-'));

        // Simulating a response from a JavaScript/JSON API
        var jsResponse = new JavaScriptApiResponse
        {
            userId = 99,
            firstName = "Jane",
            lastName = "Smith",
            emailAddress = "jane@example.com"
        };

        var dto = jsResponse.MapWithConvention<JavaScriptApiResponse, CSharpDto>(
            NamingConvention.CamelCase,
            NamingConvention.PascalCase);

        Console.WriteLine("   JavaScript API (camelCase):");
        Console.WriteLine($"     userId = {jsResponse.userId}");
        Console.WriteLine($"     firstName = \"{jsResponse.firstName}\"");
        Console.WriteLine();
        Console.WriteLine("   C# DTO (PascalCase):");
        Console.WriteLine($"     UserId = {dto?.UserId}");
        Console.WriteLine($"     FirstName = \"{dto?.FirstName}\"");
        Console.WriteLine();
    }

    static void ConvertNameExample()
    {
        Console.WriteLine("3. Convert Individual Property Names");
        Console.WriteLine("-".PadRight(40, '-'));

        var examples = new[]
        {
            ("UserName", NamingConvention.PascalCase, NamingConvention.SnakeCase),
            ("first_name", NamingConvention.SnakeCase, NamingConvention.PascalCase),
            ("OrderCount", NamingConvention.PascalCase, NamingConvention.CamelCase),
            ("emailAddress", NamingConvention.CamelCase, NamingConvention.SnakeCase),
            ("user_id", NamingConvention.SnakeCase, NamingConvention.KebabCase),
        };

        foreach (var (name, from, to) in examples)
        {
            var converted = name.ConvertName(from, to);
            Console.WriteLine($"   {name,-20} ({from.Name}) -> {converted,-20} ({to.Name})");
        }
        Console.WriteLine();
    }

    static void NamesMatchExample()
    {
        Console.WriteLine("4. Check if Names Match Across Conventions");
        Console.WriteLine("-".PadRight(40, '-'));

        var tests = new[]
        {
            ("user_id", NamingConvention.SnakeCase, "UserId", NamingConvention.PascalCase),
            ("firstName", NamingConvention.CamelCase, "FirstName", NamingConvention.PascalCase),
            ("email_address", NamingConvention.SnakeCase, "emailAddress", NamingConvention.CamelCase),
            ("user_name", NamingConvention.SnakeCase, "FirstName", NamingConvention.PascalCase), // Should not match
        };

        foreach (var (name1, conv1, name2, conv2) in tests)
        {
            var matches = NamingConvention.NamesMatch(name1, conv1, name2, conv2);
            var symbol = matches ? "==" : "!=";
            Console.WriteLine($"   \"{name1}\" ({conv1.Name}) {symbol} \"{name2}\" ({conv2.Name})");
        }
        Console.WriteLine();
    }

    static void AllConventionsExample()
    {
        Console.WriteLine("5. All Naming Conventions");
        Console.WriteLine("-".PadRight(40, '-'));

        var sourceName = "UserEmailAddress";
        var conventions = new[]
        {
            NamingConvention.PascalCase,
            NamingConvention.CamelCase,
            NamingConvention.SnakeCase,
            NamingConvention.KebabCase,
        };

        Console.WriteLine($"   Source: \"{sourceName}\" (PascalCase)");
        Console.WriteLine();
        Console.WriteLine("   Converted to all conventions:");
        foreach (var conv in conventions)
        {
            var converted = sourceName.ConvertName(NamingConvention.PascalCase, conv);
            Console.WriteLine($"     {conv.Name,-12} -> {converted}");
        }
        Console.WriteLine();
    }
}
