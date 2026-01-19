using Mapsicle;

namespace Mapsicle.Examples;

/// <summary>
/// Examples demonstrating Mapsicle core features (zero-config mapping).
/// </summary>
public static class CoreExamples
{
    public static void Run()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("MAPSICLE CORE EXAMPLES");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        BasicMapping();
        CollectionMapping();
        FlatteningExample();
        DictionaryMapping();
        CacheStatistics();
    }

    static void BasicMapping()
    {
        Console.WriteLine("1. Basic Object Mapping");
        Console.WriteLine("-".PadRight(40, '-'));

        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        // Zero-config mapping - just works!
        var dto = user.MapTo<UserDto>();

        Console.WriteLine($"   Source: User {{ Id={user.Id}, Name={user.FirstName} {user.LastName} }}");
        Console.WriteLine($"   Mapped: UserDto {{ Id={dto?.Id}, Name={dto?.FirstName} {dto?.LastName} }}");
        Console.WriteLine();
    }

    static void CollectionMapping()
    {
        Console.WriteLine("2. Collection Mapping");
        Console.WriteLine("-".PadRight(40, '-'));

        var users = new List<User>
        {
            new() { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@example.com", Age = 25 },
            new() { Id = 2, FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", Age = 35 },
            new() { Id = 3, FirstName = "Carol", LastName = "White", Email = "carol@example.com", Age = 28 }
        };

        // Map entire collection in one call
        var dtos = users.MapTo<UserDto>();

        Console.WriteLine($"   Mapped {dtos.Count} users:");
        foreach (var dto in dtos)
        {
            Console.WriteLine($"     - {dto.FirstName} {dto.LastName} ({dto.Email})");
        }
        Console.WriteLine();
    }

    static void FlatteningExample()
    {
        Console.WriteLine("3. Automatic Flattening");
        Console.WriteLine("-".PadRight(40, '-'));

        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30,
            Address = new Address
            {
                Street = "123 Main St",
                City = "New York",
                Country = "USA"
            }
        };

        // AddressCity automatically maps from Address.City
        var dto = user.MapTo<UserDetailDto>();

        Console.WriteLine($"   Source: User with Address.City = \"{user.Address.City}\"");
        Console.WriteLine($"   Mapped: UserDetailDto.AddressCity = \"{dto?.AddressCity}\"");
        Console.WriteLine($"   Mapped: UserDetailDto.AddressCountry = \"{dto?.AddressCountry}\"");
        Console.WriteLine();
    }

    static void DictionaryMapping()
    {
        Console.WriteLine("4. Dictionary Mapping");
        Console.WriteLine("-".PadRight(40, '-'));

        var user = new User
        {
            Id = 42,
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            Age = 28
        };

        // Convert object to dictionary
        var dict = user.ToDictionary();
        Console.WriteLine("   Object to Dictionary:");
        foreach (var kvp in dict.Take(4))
        {
            Console.WriteLine($"     [\"{kvp.Key}\"] = {kvp.Value}");
        }

        // Convert dictionary back to object
        var restored = dict.MapTo<UserDto>();
        Console.WriteLine($"   Dictionary back to UserDto: {restored?.FirstName} {restored?.LastName}");
        Console.WriteLine();
    }

    static void CacheStatistics()
    {
        Console.WriteLine("5. Cache Statistics");
        Console.WriteLine("-".PadRight(40, '-'));

        // Perform some mappings to populate cache
        for (int i = 0; i < 100; i++)
        {
            new User { Id = i, FirstName = $"User{i}" }.MapTo<UserDto>();
        }

        var stats = Mapper.CacheInfo();
        Console.WriteLine($"   Cache entries: {stats.Total}");
        Console.WriteLine($"   Cache hits: {stats.Hits}");
        Console.WriteLine($"   Cache misses: {stats.Misses}");
        Console.WriteLine($"   Hit ratio: {stats.HitRatio:P1}");
        Console.WriteLine();
    }
}
