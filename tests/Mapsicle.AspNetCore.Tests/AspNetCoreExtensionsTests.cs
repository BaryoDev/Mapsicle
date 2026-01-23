using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Mapsicle.AspNetCore;
using Mapsicle.Fluent;
using Mapsicle.Validation;
using Xunit;

namespace Mapsicle.AspNetCore.Tests;

public class AspNetCoreExtensionsTests
{
    #region Test Models

    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class CreateUserRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class UserDtoValidator : AbstractValidator<UserDto>
    {
        public UserDtoValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required");
            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required");
        }
    }

    #endregion

    #region MapToOk Tests

    [Fact]
    public void MapToOk_WithValidSource_ReturnsOkResult()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapToOk<UserDto>();

        Assert.IsType<Ok<UserDto>>(result);
        var okResult = (Ok<UserDto>)result;
        Assert.Equal(1, okResult.Value?.Id);
        Assert.Equal("John", okResult.Value?.FirstName);
    }

    [Fact]
    public void MapToOk_WithNullSource_ReturnsNotFound()
    {
        User? user = null;

        var result = user.MapToOk<UserDto>();

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public void MapToOk_WithMapper_ReturnsOkResult()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 2, FirstName = "Jane", LastName = "Smith" };

        var result = mapper.MapToOk<UserDto>(user);

        Assert.IsType<Ok<UserDto>>(result);
        var okResult = (Ok<UserDto>)result;
        Assert.Equal(2, okResult.Value?.Id);
    }

    #endregion

    #region MapToCreated Tests

    [Fact]
    public void MapToCreated_WithValidSource_ReturnsCreatedResult()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapToCreated<UserDto>("/api/users/1");

        Assert.IsType<Created<UserDto>>(result);
        var createdResult = (Created<UserDto>)result;
        Assert.Equal("/api/users/1", createdResult.Location);
        Assert.Equal(1, createdResult.Value?.Id);
    }

    [Fact]
    public void MapToCreated_WithNullSource_ReturnsBadRequest()
    {
        User? user = null;

        var result = user.MapToCreated<UserDto>("/api/users/1");

        Assert.IsType<BadRequest>(result);
    }

    [Fact]
    public void MapToCreated_WithMapper_ReturnsCreatedResult()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 2, FirstName = "Jane", LastName = "Smith" };

        var result = mapper.MapToCreated<UserDto>(user, "/api/users/2");

        Assert.IsType<Created<UserDto>>(result);
    }

    #endregion

    #region MapToAccepted Tests

    [Fact]
    public void MapToAccepted_WithValidSource_ReturnsAcceptedResult()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapToAccepted<UserDto>("/api/users/1/status");

        Assert.IsType<Accepted<UserDto>>(result);
        var acceptedResult = (Accepted<UserDto>)result;
        Assert.Equal("/api/users/1/status", acceptedResult.Location);
    }

    [Fact]
    public void MapToAccepted_WithNullSource_ReturnsBadRequest()
    {
        User? user = null;

        var result = user.MapToAccepted<UserDto>();

        Assert.IsType<BadRequest>(result);
    }

    #endregion

    #region MapValidateAndReturn Tests

    [Fact]
    public void MapValidateAndReturn_WithValidData_ReturnsOkResult()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = mapper.MapValidateAndReturn<UserDto, UserDtoValidator>(user);

        Assert.IsType<Ok<UserDto>>(result);
    }

    [Fact]
    public void MapValidateAndReturn_WithInvalidData_ReturnsBadRequest()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "", LastName = "" }; // Empty names

        var result = mapper.MapValidateAndReturn<UserDto, UserDtoValidator>(user);

        // Result should be a BadRequest type
        Assert.Contains("BadRequest", result.GetType().Name);
    }

    [Fact]
    public void MapValidateAndReturn_WithNullSource_ReturnsBadRequest()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();

        var result = mapper.MapValidateAndReturn<UserDto, UserDtoValidator>(null);

        // Result should be a BadRequest type
        Assert.Contains("BadRequest", result.GetType().Name);
    }

    #endregion

    #region MapValidateAndCreate Tests

    [Fact]
    public void MapValidateAndCreate_WithValidData_ReturnsCreatedResult()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = mapper.MapValidateAndCreate<UserDto, UserDtoValidator>(user, "/api/users/1");

        Assert.IsType<Created<UserDto>>(result);
    }

    [Fact]
    public void MapValidateAndCreate_WithUriGenerator_ReturnsCreatedWithGeneratedUri()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 5, FirstName = "John", LastName = "Doe" };

        var result = mapper.MapValidateAndCreate<UserDto, UserDtoValidator>(
            user,
            dto => $"/api/users/{dto.Id}");

        Assert.IsType<Created<UserDto>>(result);
        var createdResult = (Created<UserDto>)result;
        Assert.Equal("/api/users/5", createdResult.Location);
    }

    [Fact]
    public void MapValidateAndCreate_WithInvalidData_ReturnsBadRequest()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "", LastName = "" };

        var result = mapper.MapValidateAndCreate<UserDto, UserDtoValidator>(user, "/api/users/1");

        // Result should be a BadRequest type
        Assert.Contains("BadRequest", result.GetType().Name);
    }

    #endregion

    #region Collection Mapping Tests

    [Fact]
    public void MapCollectionToOk_WithValidCollection_ReturnsOkWithList()
    {
        var users = new List<User>
        {
            new() { Id = 1, FirstName = "John", LastName = "Doe" },
            new() { Id = 2, FirstName = "Jane", LastName = "Smith" }
        };

        var result = users.Cast<object>().MapCollectionToOk<UserDto>();

        Assert.IsType<Ok<List<UserDto>>>(result);
        var okResult = (Ok<List<UserDto>>)result;
        Assert.Equal(2, okResult.Value?.Count);
    }

    [Fact]
    public void MapCollectionToOk_WithNullCollection_ReturnsOkWithEmptyArray()
    {
        IEnumerable<User>? users = null;

        var result = users.MapCollectionToOk<UserDto>();

        Assert.IsType<Ok<UserDto[]>>(result);
        var okResult = (Ok<UserDto[]>)result;
        Assert.Empty(okResult.Value!);
    }

    [Fact]
    public void MapCollectionToOk_WithMapper_ReturnsOkWithList()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var users = new List<User>
        {
            new() { Id = 1, FirstName = "John", LastName = "Doe" }
        };

        var result = mapper.MapCollectionToOk<User, UserDto>(users);

        Assert.IsType<Ok<List<UserDto>>>(result);
    }

    #endregion

    #region ToProblemDetails Tests

    [Fact]
    public void ToProblemDetails_WithValidResult_ReturnsOk()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var validationResult = mapper.MapAndValidate<UserDto, UserDtoValidator>(user);
        var result = validationResult.ToProblemDetails();

        Assert.IsType<Ok<UserDto>>(result);
    }

    [Fact]
    public void ToProblemDetails_WithInvalidResult_ReturnsBadRequest()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "", LastName = "" };

        var validationResult = mapper.MapAndValidate<UserDto, UserDtoValidator>(user);
        var result = validationResult.ToProblemDetails("Validation Error", "/api/users");

        Assert.IsType<BadRequest<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>>(result);
    }

    #endregion

    #region MappedResponse Tests

    [Fact]
    public void MappedResponse_Ok_ReturnsSuccessfulResponse()
    {
        var dto = new UserDto { Id = 1, FirstName = "John" };

        var response = MappedResponse<UserDto>.Ok(dto);

        Assert.True(response.Success);
        Assert.Equal(dto, response.Data);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void MappedResponse_Fail_ReturnsFailedResponse()
    {
        var response = MappedResponse<UserDto>.Fail("Error 1", "Error 2");

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal(2, response.Errors.Count);
    }

    [Fact]
    public void MappedResponse_FromValidation_WithValidResult_ReturnsSuccess()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var validationResult = mapper.MapAndValidate<UserDto, UserDtoValidator>(user);
        var response = MappedResponse<UserDto>.FromValidation(validationResult);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public void MappedResponse_FromValidation_WithInvalidResult_ReturnsFailed()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "", LastName = "" };

        var validationResult = mapper.MapAndValidate<UserDto, UserDtoValidator>(user);
        var response = MappedResponse<UserDto>.FromValidation(validationResult);

        Assert.False(response.Success);
        Assert.NotEmpty(response.Errors);
    }

    #endregion
}
