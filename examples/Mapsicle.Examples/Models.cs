namespace Mapsicle.Examples;

// =============================================================================
// Core Mapping Models
// =============================================================================

public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public Address? Address { get; set; }
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

public class UserDetailDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AddressCity { get; set; } = "";      // Flattened from Address.City
    public string AddressCountry { get; set; } = "";   // Flattened from Address.Country
}

// =============================================================================
// Validation Models
// =============================================================================

public class CreateUserRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

public class ValidatedUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

// =============================================================================
// Naming Convention Models (snake_case source - simulating external API)
// =============================================================================

public class ExternalApiResponse
{
    public int user_id { get; set; }
    public string first_name { get; set; } = "";
    public string last_name { get; set; } = "";
    public string email_address { get; set; } = "";
    public int order_count { get; set; }
    public DateTime created_at { get; set; }
}

public class InternalUserDto
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public int OrderCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

// =============================================================================
// Naming Convention Models (camelCase source - simulating JavaScript API)
// =============================================================================

public class JavaScriptApiResponse
{
    public int userId { get; set; }
    public string firstName { get; set; } = "";
    public string lastName { get; set; } = "";
    public string emailAddress { get; set; } = "";
}

public class CSharpDto
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string EmailAddress { get; set; } = "";
}
