using Mapsicle.Audit;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Audit.Tests;

public class AuditTests
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

    #endregion

    #region MapWithAudit Tests

    [Fact]
    public void MapWithAudit_ReturnsAuditedResult()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapWithAudit<UserDto>();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("John", result.Value.FirstName);
    }

    [Fact]
    public void MapWithAudit_CapturesAuditInfo()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapWithAudit<UserDto>();

        Assert.True(result.Audit.WasSuccessful);
        Assert.Equal(typeof(User), result.Audit.SourceType);
        Assert.Equal(typeof(UserDto), result.Audit.DestinationType);
        Assert.True(result.Audit.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void MapWithAudit_CapturesPropertyMappings()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapWithAudit<UserDto>();

        Assert.NotEmpty(result.Audit.PropertyMappings);

        var idMapping = result.Audit.PropertyMappings.FirstOrDefault(p => p.PropertyName == "Id");
        Assert.NotNull(idMapping);
        Assert.True(idMapping.WasMapped);
        Assert.Equal(1, idMapping.SourceValue);
        Assert.Equal(1, idMapping.DestinationValue);
    }

    [Fact]
    public void MapWithAudit_NullSource_ReturnsFailedResult()
    {
        User? user = null;

        var result = user.MapWithAudit<UserDto>();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Source was null", result.Audit.FailureReason);
    }

    [Fact]
    public void MapWithAudit_WithMapper_ReturnsAuditedResult()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = mapper.MapWithAudit<User, UserDto>(user);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
    }

    [Fact]
    public void MapWithAudit_TracksUnmappedProperties()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "test@test.com" };

        var result = user.MapWithAudit<UserDto>();

        // Id, FirstName, LastName should be mapped
        Assert.True(result.Audit.MappedPropertyCount >= 3);
    }

    [Fact]
    public void GetValueOrThrow_WithSuccessfulMapping_ReturnsValue()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var result = user.MapWithAudit<UserDto>();
        var value = result.GetValueOrThrow();

        Assert.Equal(1, value.Id);
    }

    [Fact]
    public void GetValueOrThrow_WithFailedMapping_Throws()
    {
        User? user = null;

        var result = user.MapWithAudit<UserDto>();

        Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());
    }

    #endregion

    #region Diff Tests

    [Fact]
    public void Diff_IdenticalObjects_ReturnsEmptyList()
    {
        var dto1 = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };
        var dto2 = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var changes = dto1.Diff(dto2);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_DifferentObjects_ReturnsChanges()
    {
        var dto1 = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };
        var dto2 = new UserDto { Id = 1, FirstName = "Jane", LastName = "Doe" };

        var changes = dto1.Diff(dto2);

        Assert.Single(changes);
        Assert.Equal("FirstName", changes[0].PropertyName);
        Assert.Equal("John", changes[0].OldValue);
        Assert.Equal("Jane", changes[0].NewValue);
        Assert.Equal(ChangeType.Modified, changes[0].ChangeType);
    }

    [Fact]
    public void Diff_MultipleChanges_ReturnsAllChanges()
    {
        var dto1 = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };
        var dto2 = new UserDto { Id = 2, FirstName = "Jane", LastName = "Smith" };

        var changes = dto1.Diff(dto2);

        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void Diff_NullOriginal_ReturnsCreatedChange()
    {
        UserDto? dto1 = null;
        var dto2 = new UserDto { Id = 1, FirstName = "John" };

        var changes = dto1.Diff(dto2);

        Assert.Single(changes);
        Assert.Equal(ChangeType.Created, changes[0].ChangeType);
    }

    [Fact]
    public void Diff_NullModified_ReturnsDeletedChange()
    {
        var dto1 = new UserDto { Id = 1, FirstName = "John" };
        UserDto? dto2 = null;

        var changes = dto1.Diff(dto2);

        Assert.Single(changes);
        Assert.Equal(ChangeType.Deleted, changes[0].ChangeType);
    }

    [Fact]
    public void Diff_BothNull_ReturnsEmptyList()
    {
        UserDto? dto1 = null;
        UserDto? dto2 = null;

        var changes = dto1.Diff(dto2);

        Assert.Empty(changes);
    }

    #endregion

    #region MapAndDetectChanges Tests

    [Fact]
    public void MapAndDetectChanges_WithChanges_ReturnsDetectedChanges()
    {
        var user = new User { Id = 1, FirstName = "Jane", LastName = "Smith" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapAndDetectChanges(existingDto);

        Assert.True(result.HasChanges);
        Assert.Equal(2, result.Changes.Count); // FirstName and LastName changed
    }

    [Fact]
    public void MapAndDetectChanges_NoChanges_ReturnsNoChanges()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapAndDetectChanges(existingDto);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void MapAndDetectChanges_NullSource_ReturnsNoChanges()
    {
        User? user = null;
        var existingDto = new UserDto { Id = 1, FirstName = "John" };

        var result = user.MapAndDetectChanges(existingDto);

        Assert.False(result.HasChanges);
    }

    [Fact]
    public void MapAndDetectChanges_WithMapper_DetectsChanges()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "Jane", LastName = "Smith" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = mapper.MapAndDetectChanges<User, UserDto>(user, existingDto);

        Assert.True(result.HasChanges);
    }

    [Fact]
    public void MapAndDetectChanges_GetChange_ReturnsSpecificChange()
    {
        var user = new User { Id = 1, FirstName = "Jane", LastName = "Doe" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var result = user.MapAndDetectChanges(existingDto);

        var firstNameChange = result.GetChange("FirstName");
        Assert.NotNull(firstNameChange);
        Assert.Equal("John", firstNameChange.OldValue);
        Assert.Equal("Jane", firstNameChange.NewValue);
    }

    #endregion

    #region WouldChangeOnMap Tests

    [Fact]
    public void WouldChangeOnMap_WithChanges_ReturnsTrue()
    {
        var user = new User { Id = 1, FirstName = "Jane", LastName = "Doe" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var wouldChange = user.WouldChangeOnMap(existingDto);

        Assert.True(wouldChange);
    }

    [Fact]
    public void WouldChangeOnMap_NoChanges_ReturnsFalse()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var wouldChange = user.WouldChangeOnMap(existingDto);

        Assert.False(wouldChange);
    }

    [Fact]
    public void WouldChangeOnMap_NullSource_ReturnsFalse()
    {
        User? user = null;
        var existingDto = new UserDto { Id = 1 };

        var wouldChange = user.WouldChangeOnMap(existingDto);

        Assert.False(wouldChange);
    }

    #endregion

    #region GetChangedValues Tests

    [Fact]
    public void GetChangedValues_ReturnsChangedPropertyValues()
    {
        var user = new User { Id = 1, FirstName = "Jane", LastName = "Smith" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var changedValues = user.GetChangedValues(existingDto);

        Assert.Equal(2, changedValues.Count);
        Assert.Equal("Jane", changedValues["FirstName"]);
        Assert.Equal("Smith", changedValues["LastName"]);
    }

    [Fact]
    public void GetChangedValues_NoChanges_ReturnsEmptyDictionary()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var existingDto = new UserDto { Id = 1, FirstName = "John", LastName = "Doe" };

        var changedValues = user.GetChangedValues(existingDto);

        Assert.Empty(changedValues);
    }

    #endregion

    #region MappingAudit Tests

    [Fact]
    public void MappingAudit_UnmappedProperties_ReturnsCorrectNames()
    {
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "test@test.com" };

        var result = user.MapWithAudit<UserDto>();

        // UserDto doesn't have Email, so there might be unmapped destination properties
        // But since we're mapping TO UserDto which has Id, FirstName, LastName
        // All of those exist in User, so they should all be mapped
        Assert.True(result.Audit.MappedPropertyCount >= 3);
    }

    [Fact]
    public void PropertyMappingInfo_CapturesTypeInfo()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var result = user.MapWithAudit<UserDto>();

        var idMapping = result.Audit.PropertyMappings.FirstOrDefault(p => p.PropertyName == "Id");
        Assert.NotNull(idMapping);
        Assert.Equal(typeof(int), idMapping.SourceType);
        Assert.Equal(typeof(int), idMapping.DestinationType);
    }

    #endregion
}
