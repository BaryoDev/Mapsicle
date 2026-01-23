using System.ComponentModel.DataAnnotations;
using Mapsicle.DataAnnotations;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.DataAnnotations.Tests;

public class DataAnnotationsTests
{
    #region Test Models

    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required")]
        public string LastName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string? Email { get; set; }

        [Range(0, 150, ErrorMessage = "Age must be between 0 and 150")]
        public int Age { get; set; }
    }

    public class CreateUserRequest
    {
        [Required(ErrorMessage = "Name is required")]
        [MinLength(2, ErrorMessage = "Name must be at least 2 characters")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Range(18, 120, ErrorMessage = "Age must be between 18 and 120")]
        public int Age { get; set; }
    }

    #endregion

    #region MapAndValidateAnnotations Tests

    [Fact]
    public void MapAndValidateAnnotations_ValidData_ReturnsSuccess()
    {
        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        var result = user.MapAndValidateAnnotations<UserDto>();

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MapAndValidateAnnotations_InvalidData_ReturnsErrors()
    {
        var user = new User
        {
            Id = 1,
            FirstName = "", // Required validation will fail
            LastName = "",  // Required validation will fail
            Email = "invalid-email", // Email format will fail
            Age = 200 // Range validation will fail
        };

        var result = user.MapAndValidateAnnotations<UserDto>();

        Assert.False(result.IsValid);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void MapAndValidateAnnotations_NullSource_ReturnsFailure()
    {
        User? user = null;

        var result = user.MapAndValidateAnnotations<UserDto>();

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void MapAndValidateAnnotations_WithMapper_ValidData_ReturnsSuccess()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        var result = mapper.MapAndValidateAnnotations<UserDto>(user);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MapAndValidateAnnotations_WithMapperGeneric_ValidData_ReturnsSuccess()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        var result = mapper.MapAndValidateAnnotations<User, UserDto>(user);

        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAnnotations Tests

    [Fact]
    public void ValidateAnnotations_ValidObject_ReturnsSuccess()
    {
        var dto = new UserDto
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        var result = dto.ValidateAnnotations();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateAnnotations_InvalidObject_ReturnsErrors()
    {
        var dto = new UserDto
        {
            Id = 1,
            FirstName = "J", // Too short (min 2)
            LastName = "",   // Required
            Email = "not-an-email",
            Age = -5         // Out of range
        };

        var result = dto.ValidateAnnotations();

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void IsValidAnnotations_ValidObject_ReturnsTrue()
    {
        var dto = new UserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30
        };

        Assert.True(dto.IsValidAnnotations());
    }

    [Fact]
    public void IsValidAnnotations_InvalidObject_ReturnsFalse()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };

        Assert.False(dto.IsValidAnnotations());
    }

    [Fact]
    public void GetValidationErrors_ReturnsAllErrors()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Email = "invalid",
            Age = -1
        };

        var errors = dto.GetValidationErrors();

        Assert.NotEmpty(errors);
        Assert.True(errors.Count >= 2); // At least FirstName and LastName required
    }

    #endregion

    #region ErrorsByProperty Tests

    [Fact]
    public void ErrorsByProperty_GroupsErrorsByPropertyName()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Email = "invalid-email",
            Age = 200
        };

        var result = dto.ValidateAnnotations();

        var errorsByProperty = result.ErrorsByProperty;

        Assert.Contains("FirstName", errorsByProperty.Keys);
        Assert.Contains("LastName", errorsByProperty.Keys);
    }

    [Fact]
    public void ErrorMessages_ReturnsAllMessages()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };

        var result = dto.ValidateAnnotations();

        var messages = result.ErrorMessages.ToList();

        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.False(string.IsNullOrEmpty(m)));
    }

    #endregion

    #region GetValueOrThrow Tests

    [Fact]
    public void GetValueOrThrow_ValidResult_ReturnsValue()
    {
        var dto = new UserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Age = 30
        };

        var result = dto.ValidateAnnotations();
        var value = result.GetValueOrThrow();

        Assert.Equal("John", value.FirstName);
    }

    [Fact]
    public void GetValueOrThrow_InvalidResult_ThrowsValidationException()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };

        var result = dto.ValidateAnnotations();

        Assert.Throws<ValidationException>(() => result.GetValueOrThrow());
    }

    #endregion

    #region OnSuccess/OnFailure Tests

    [Fact]
    public void OnSuccess_ValidResult_ExecutesAction()
    {
        var dto = new UserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Age = 30
        };
        var wasExecuted = false;

        dto.ValidateAnnotations()
           .OnSuccess(v => wasExecuted = true);

        Assert.True(wasExecuted);
    }

    [Fact]
    public void OnSuccess_InvalidResult_DoesNotExecuteAction()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };
        var wasExecuted = false;

        dto.ValidateAnnotations()
           .OnSuccess(v => wasExecuted = true);

        Assert.False(wasExecuted);
    }

    [Fact]
    public void OnFailure_InvalidResult_ExecutesAction()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };
        var wasExecuted = false;

        dto.ValidateAnnotations()
           .OnFailure(errors => wasExecuted = true);

        Assert.True(wasExecuted);
    }

    [Fact]
    public void OnFailure_ValidResult_DoesNotExecuteAction()
    {
        var dto = new UserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Age = 30
        };
        var wasExecuted = false;

        dto.ValidateAnnotations()
           .OnFailure(errors => wasExecuted = true);

        Assert.False(wasExecuted);
    }

    #endregion

    #region Match Tests

    [Fact]
    public void Match_ValidResult_ReturnsSuccessResult()
    {
        var dto = new UserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Age = 30
        };

        var result = dto.ValidateAnnotations()
            .Match(
                onSuccess: v => $"Hello, {v.FirstName}!",
                onFailure: errors => "Validation failed"
            );

        Assert.Equal("Hello, John!", result);
    }

    [Fact]
    public void Match_InvalidResult_ReturnsFailureResult()
    {
        var dto = new UserDto
        {
            FirstName = "",
            LastName = "",
            Age = 200
        };

        var result = dto.ValidateAnnotations()
            .Match(
                onSuccess: v => "Success",
                onFailure: errors => $"Failed with {errors.Count} errors"
            );

        Assert.StartsWith("Failed with", result);
    }

    #endregion

    #region Static Factory Methods Tests

    [Fact]
    public void Success_CreatesValidResult()
    {
        var dto = new UserDto { FirstName = "John", LastName = "Doe" };

        var result = DataAnnotationsValidationResult<UserDto>.Success(dto);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_CreatesInvalidResult()
    {
        var result = DataAnnotationsValidationResult<UserDto>.Failure(
            null,
            new ValidationResult("Error 1"),
            new ValidationResult("Error 2")
        );

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    #endregion

    #region Complex Validation Tests

    [Fact]
    public void MapAndValidate_ComplexRequest_ValidatesAllFields()
    {
        var request = new CreateUserRequest
        {
            Name = "J", // Too short
            Email = "invalid", // Invalid format
            Age = 10 // Below minimum
        };

        var result = request.ValidateAnnotations();

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void MapAndValidate_ComplexRequest_ValidData_Succeeds()
    {
        var request = new CreateUserRequest
        {
            Name = "John Doe",
            Email = "john@example.com",
            Age = 25
        };

        var result = request.ValidateAnnotations();

        Assert.True(result.IsValid);
    }

    #endregion
}
