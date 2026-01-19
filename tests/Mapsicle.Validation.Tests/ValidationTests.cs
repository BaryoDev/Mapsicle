using FluentValidation;
using Mapsicle.Fluent;
using Mapsicle.Validation;
using Xunit;

namespace Mapsicle.Validation.Tests;

#region Test Models and Validators

public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

public class UserDtoValidator : AbstractValidator<UserDto>
{
    public UserDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Valid email is required");
        RuleFor(x => x.Age).GreaterThan(0).WithMessage("Age must be positive");
    }
}

public class StrictUserDtoValidator : AbstractValidator<UserDto>
{
    public StrictUserDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 120);
    }
}

#endregion

public class ValidationTests
{
    private readonly IMapper _mapper;

    public ValidationTests()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void MapAndValidate_ValidObject_ReturnsIsValidTrue()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "John Doe",
            Email = "john@example.com",
            Age = 25
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
        Assert.Equal("John Doe", result.Value.Name);
        Assert.Equal("john@example.com", result.Value.Email);
        Assert.Equal(25, result.Value.Age);
    }

    [Fact]
    public void MapAndValidate_InvalidEmail_ReturnsValidationErrors()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "John Doe",
            Email = "invalid-email",
            Age = 25
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.PropertyName == "Email");
    }

    [Fact]
    public void MapAndValidate_MultipleInvalidFields_ReturnsAllErrors()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "",
            Email = "invalid",
            Age = -5
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
        Assert.Contains(result.Errors, e => e.PropertyName == "Email");
        Assert.Contains(result.Errors, e => e.PropertyName == "Age");
    }

    [Fact]
    public void MapAndValidate_WithValidatorInstance_Works()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "John Doe",
            Email = "john@example.com",
            Age = 25
        };

        var validator = new UserDtoValidator();
        var result = _mapper.MapAndValidate<UserDto>(user, validator);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void MapAndValidate_NullSource_ReturnsFailure()
    {
        UserEntity? user = null;

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("null"));
    }

    [Fact]
    public void GetValueOrThrow_ValidResult_ReturnsValue()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "John",
            Email = "john@example.com",
            Age = 30
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);
        var value = result.GetValueOrThrow();

        Assert.Equal("John", value.Name);
    }

    [Fact]
    public void GetValueOrThrow_InvalidResult_ThrowsException()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "",
            Email = "invalid",
            Age = -1
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        var ex = Assert.Throws<ValidationException>(() => result.GetValueOrThrow());
        Assert.NotEmpty(ex.Errors);
        Assert.Contains("Validation failed", ex.Message);
    }

    [Fact]
    public void ErrorsByProperty_ReturnsGroupedErrors()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "",
            Email = "invalid",
            Age = -1
        };

        var result = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);

        Assert.False(result.IsValid);
        Assert.True(result.ErrorsByProperty.ContainsKey("Name"));
        Assert.True(result.ErrorsByProperty.ContainsKey("Email"));
        Assert.True(result.ErrorsByProperty.ContainsKey("Age"));
    }

    [Fact]
    public void Validate_Extension_ValidatesExistingObject()
    {
        var dto = new UserDto
        {
            Id = 1,
            Name = "John",
            Email = "john@example.com",
            Age = 25
        };

        var result = dto.Validate<UserDto, UserDtoValidator>();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Extension_InvalidObject_ReturnsErrors()
    {
        var dto = new UserDto
        {
            Id = 1,
            Name = "",
            Email = "bad",
            Age = 0
        };

        var result = dto.Validate<UserDto, UserDtoValidator>();

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void MapAndValidate_DifferentValidators_ProduceDifferentResults()
    {
        var user = new UserEntity
        {
            Id = 1,
            Name = "J", // Too short for StrictValidator
            Email = "john@example.com",
            Age = 15 // Too young for StrictValidator
        };

        var basicResult = _mapper.MapAndValidate<UserEntity, UserDto, UserDtoValidator>(user);
        var strictResult = _mapper.MapAndValidate<UserEntity, UserDto, StrictUserDtoValidator>(user);

        Assert.True(basicResult.IsValid); // Basic validator passes
        Assert.False(strictResult.IsValid); // Strict validator fails
    }

    [Fact]
    public void MapperValidationResult_Success_CreatesValidResult()
    {
        var dto = new UserDto { Id = 1, Name = "Test", Email = "test@test.com", Age = 20 };

        var result = MapperValidationResult<UserDto>.Success(dto);

        Assert.True(result.IsValid);
        Assert.Equal("Test", result.Value?.Name);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ClearValidatorCache_Works()
    {
        // Should not throw
        ValidationExtensions.ClearValidatorCache();
    }
}
