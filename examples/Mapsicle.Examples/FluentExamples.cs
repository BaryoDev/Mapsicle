using Mapsicle.Fluent;

namespace Mapsicle.Examples;

/// <summary>
/// Examples demonstrating Mapsicle.Fluent features.
/// </summary>
public static class FluentExamples
{
    public static void Run()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("MAPSICLE.FLUENT EXAMPLES");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        ForMemberExample();
        ConditionExample();
        HooksExample();
    }

    static void ForMemberExample()
    {
        Console.WriteLine("1. ForMember Custom Mapping");
        Console.WriteLine("-".PadRight(40, '-'));

        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDetailDto>()
                .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"))
                .ForMember(d => d.Email, opt => opt.MapFrom(s => s.Email.ToLower()));
        });

        var mapper = config.CreateMapper();

        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "JOHN@EXAMPLE.COM",
            Address = new Address { City = "Boston", Country = "USA" }
        };

        var dto = mapper.Map<User, UserDetailDto>(user);

        Console.WriteLine($"   Source: FirstName=\"{user.FirstName}\", LastName=\"{user.LastName}\"");
        Console.WriteLine($"   Mapped: FullName=\"{dto?.FullName}\"");
        Console.WriteLine($"   Source: Email=\"{user.Email}\"");
        Console.WriteLine($"   Mapped: Email=\"{dto?.Email}\" (lowercased)");
        Console.WriteLine();
    }

    static void ConditionExample()
    {
        Console.WriteLine("2. Conditional Mapping");
        Console.WriteLine("-".PadRight(40, '-'));

        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDto>()
                .ForMember(d => d.Email, opt => opt.Condition(s => s.Age >= 18));
        });

        var mapper = config.CreateMapper();

        var adult = new User { Id = 1, FirstName = "Adult", Email = "adult@example.com", Age = 25 };
        var minor = new User { Id = 2, FirstName = "Minor", Email = "minor@example.com", Age = 15 };

        var adultDto = mapper.Map<User, UserDto>(adult);
        var minorDto = mapper.Map<User, UserDto>(minor);

        Console.WriteLine($"   Adult (age {adult.Age}): Email = \"{adultDto?.Email}\"");
        Console.WriteLine($"   Minor (age {minor.Age}): Email = \"{minorDto?.Email}\" (not mapped due to condition)");
        Console.WriteLine();
    }

    static void HooksExample()
    {
        Console.WriteLine("3. BeforeMap/AfterMap Hooks");
        Console.WriteLine("-".PadRight(40, '-'));

        var mappingLog = new List<string>();

        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDto>()
                .BeforeMap((src, dest) => mappingLog.Add($"Starting map for user {src.Id}"))
                .AfterMap((src, dest) => mappingLog.Add($"Completed map: {dest.FirstName} {dest.LastName}"));
        });

        var mapper = config.CreateMapper();

        var user = new User { Id = 42, FirstName = "Hook", LastName = "Demo" };
        var dto = mapper.Map<User, UserDto>(user);

        Console.WriteLine("   Mapping log:");
        foreach (var log in mappingLog)
        {
            Console.WriteLine($"     - {log}");
        }
        Console.WriteLine();
    }
}
