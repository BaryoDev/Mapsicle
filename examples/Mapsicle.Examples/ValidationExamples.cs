using Mapsicle.Fluent;
using Mapsicle.Validation;

namespace Mapsicle.Examples;

/// <summary>
/// Examples demonstrating Mapsicle.Validation features.
/// </summary>
public static class ValidationExamples
{
    public static void Run()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("MAPSICLE.VALIDATION EXAMPLES");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        ValidMappingExample();
        InvalidMappingExample();
        ErrorsByPropertyExample();
        GetValueOrThrowExample();
        DifferentValidatorsExample();
    }

    static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<CreateUserRequest, ValidatedUserDto>();
        });
        return config.CreateMapper();
    }

    static void ValidMappingExample()
    {
        Console.WriteLine("1. Valid Mapping with Validation");
        Console.WriteLine("-".PadRight(40, '-'));

        var mapper = CreateMapper();

        var request = new CreateUserRequest
        {
            Name = "John Doe",
            Email = "john@example.com",
            Age = 30
        };

        var result = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(request);

        Console.WriteLine($"   Input: Name=\"{request.Name}\", Email=\"{request.Email}\", Age={request.Age}");
        Console.WriteLine($"   IsValid: {result.IsValid}");
        Console.WriteLine($"   Value: {{ Name=\"{result.Value?.Name}\", Email=\"{result.Value?.Email}\" }}");
        Console.WriteLine();
    }

    static void InvalidMappingExample()
    {
        Console.WriteLine("2. Invalid Mapping with Validation Errors");
        Console.WriteLine("-".PadRight(40, '-'));

        var mapper = CreateMapper();

        var request = new CreateUserRequest
        {
            Name = "",              // Invalid: empty
            Email = "not-an-email", // Invalid: not a valid email
            Age = -5                // Invalid: negative
        };

        var result = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(request);

        Console.WriteLine($"   Input: Name=\"{request.Name}\", Email=\"{request.Email}\", Age={request.Age}");
        Console.WriteLine($"   IsValid: {result.IsValid}");
        Console.WriteLine($"   Errors ({result.Errors.Count}):");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"     - {error.PropertyName}: {error.ErrorMessage}");
        }
        Console.WriteLine();
    }

    static void ErrorsByPropertyExample()
    {
        Console.WriteLine("3. ErrorsByProperty Dictionary (API-friendly)");
        Console.WriteLine("-".PadRight(40, '-'));

        var mapper = CreateMapper();

        var request = new CreateUserRequest
        {
            Name = "X",            // Too short
            Email = "bad",         // Invalid format
            Age = 200              // Unrealistic
        };

        var result = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(request);

        Console.WriteLine("   ErrorsByProperty (for API responses):");
        Console.WriteLine("   {");
        foreach (var kvp in result.ErrorsByProperty)
        {
            Console.WriteLine($"     \"{kvp.Key}\": [\"{string.Join("\", \"", kvp.Value)}\"]");
        }
        Console.WriteLine("   }");
        Console.WriteLine();
    }

    static void GetValueOrThrowExample()
    {
        Console.WriteLine("4. GetValueOrThrow Pattern");
        Console.WriteLine("-".PadRight(40, '-'));

        var mapper = CreateMapper();

        // Valid case
        var validRequest = new CreateUserRequest { Name = "Valid", Email = "valid@test.com", Age = 25 };
        var validResult = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(validRequest);

        try
        {
            var value = validResult.GetValueOrThrow();
            Console.WriteLine($"   Valid request: Got value successfully - {value.Name}");
        }
        catch (ValidationException)
        {
            Console.WriteLine("   Valid request: Unexpected exception");
        }

        // Invalid case
        var invalidRequest = new CreateUserRequest { Name = "", Email = "bad", Age = -1 };
        var invalidResult = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(invalidRequest);

        try
        {
            var value = invalidResult.GetValueOrThrow();
            Console.WriteLine("   Invalid request: Should have thrown");
        }
        catch (ValidationException ex)
        {
            Console.WriteLine($"   Invalid request: Caught ValidationException");
            Console.WriteLine($"     Message: {ex.Message.Substring(0, Math.Min(60, ex.Message.Length))}...");
        }
        Console.WriteLine();
    }

    static void DifferentValidatorsExample()
    {
        Console.WriteLine("5. Different Validators for Different Rules");
        Console.WriteLine("-".PadRight(40, '-'));

        var mapper = CreateMapper();

        var request = new CreateUserRequest
        {
            Name = "Teen User",
            Email = "teen@example.com",
            Age = 16
        };

        // Basic validator allows age > 0
        var basicResult = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, ValidatedUserDtoValidator>(request);

        // Adult validator requires age >= 18
        var adultResult = mapper.MapAndValidate<CreateUserRequest, ValidatedUserDto, AdultUserValidator>(request);

        Console.WriteLine($"   Input: Age = {request.Age}");
        Console.WriteLine($"   ValidatedUserDtoValidator (age > 0): IsValid = {basicResult.IsValid}");
        Console.WriteLine($"   AdultUserValidator (age >= 18): IsValid = {adultResult.IsValid}");
        if (!adultResult.IsValid)
        {
            Console.WriteLine($"     Error: {adultResult.Errors.First().ErrorMessage}");
        }
        Console.WriteLine();
    }
}
