using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Mapsicle.Caching;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Caching.Tests;

public class CachingTests
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

    #endregion

    private IMemoryCache CreateMemoryCache()
    {
        var options = Options.Create(new MemoryCacheOptions());
        return new MemoryCache(options);
    }

    #region Memory Cache Tests

    [Fact]
    public void MapToCached_FirstCall_MapsAndCaches()
    {
        var cache = CreateMemoryCache();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var dto = user.MapToCached<UserDto>(cache, "user:1");

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.Equal("John", dto.FirstName);

        // Verify it's cached
        Assert.True(cache.TryGetValue("user:1", out UserDto? cached));
        Assert.Equal(dto.Id, cached?.Id);
    }

    [Fact]
    public void MapToCached_SecondCall_ReturnsCachedValue()
    {
        var cache = CreateMemoryCache();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var cacheKey = "user:1";

        // First call - maps and caches
        var dto1 = user.MapToCached<UserDto>(cache, cacheKey);

        // Modify source - this should NOT affect cached result
        user.FirstName = "Jane";

        // Second call - should return cached value
        var dto2 = user.MapToCached<UserDto>(cache, cacheKey);

        Assert.NotNull(dto2);
        Assert.Equal("John", dto2.FirstName); // Original cached value
    }

    [Fact]
    public void MapToCached_NullSource_ReturnsDefault()
    {
        var cache = CreateMemoryCache();
        User? user = null;

        var dto = user.MapToCached<UserDto>(cache, "user:null");

        Assert.Null(dto);
    }

    [Fact]
    public void MapToCached_WithMapper_MapsAndCaches()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var mapper = config.CreateMapper();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var dto = mapper.MapToCached<UserDto>(user, cache, "user:1");

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public void MapToCachedAuto_GeneratesKeyAndCaches()
    {
        var cache = CreateMemoryCache();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var dto = user.MapToCachedAuto<UserDto>(cache);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public void MapToCached_WithCustomOptions_UsesOptions()
    {
        var cache = CreateMemoryCache();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        var dto = user.MapToCached<UserDto>(cache, "user:1", options);

        Assert.NotNull(dto);
    }

    [Fact]
    public void MapCollectionToCached_CachesEachItem()
    {
        var cache = CreateMemoryCache();
        var users = new List<object>
        {
            new User { Id = 1, FirstName = "John", LastName = "Doe" },
            new User { Id = 2, FirstName = "Jane", LastName = "Smith" }
        };

        var dtos = users.MapCollectionToCached<UserDto>(cache, u => $"user:{((User)u).Id}");

        Assert.Equal(2, dtos.Count);
        Assert.True(cache.TryGetValue("user:1", out UserDto? _));
        Assert.True(cache.TryGetValue("user:2", out UserDto? _));
    }

    #endregion

    #region Cache Invalidation Tests

    [Fact]
    public void InvalidateMappingCache_RemovesCachedItem()
    {
        var cache = CreateMemoryCache();
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var cacheKey = "user:1";

        // Cache the item
        user.MapToCached<UserDto>(cache, cacheKey);
        Assert.True(cache.TryGetValue(cacheKey, out UserDto? _));

        // Invalidate
        cache.InvalidateMappingCache(cacheKey);

        Assert.False(cache.TryGetValue(cacheKey, out UserDto? _));
    }

    #endregion

    #region Cache Key Generation Tests

    [Fact]
    public void GenerateCacheKey_CreatesUniqueKey()
    {
        var user1 = new User { Id = 1, FirstName = "John" };
        var user2 = new User { Id = 2, FirstName = "Jane" };

        var key1 = CachingExtensions.GenerateCacheKey(user1, typeof(UserDto));
        var key2 = CachingExtensions.GenerateCacheKey(user2, typeof(UserDto));

        Assert.NotEqual(key1, key2);
        Assert.StartsWith("mapsicle:User:UserDto:", key1);
    }

    [Fact]
    public void GenerateCacheKey_SameObject_SameKey()
    {
        var user = new User { Id = 1, FirstName = "John" };

        var key1 = CachingExtensions.GenerateCacheKey(user, typeof(UserDto));
        var key2 = CachingExtensions.GenerateCacheKey(user, typeof(UserDto));

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void CreateCacheKey_WithPrefix_CreatesCorrectKey()
    {
        var key = CachingExtensions.CreateCacheKey("users", "123");

        Assert.Equal("mapsicle:users:123", key);
    }

    [Fact]
    public void CreateEntityCacheKey_CreatesCorrectKey()
    {
        var key = CachingExtensions.CreateEntityCacheKey<User, UserDto>(123);

        Assert.Equal("mapsicle:User:UserDto:123", key);
    }

    #endregion

    #region CachedMappingOptions Tests

    [Fact]
    public void CachedMappingOptions_ToMemoryCacheOptions_CreatesCorrectOptions()
    {
        var options = new CachedMappingOptions
        {
            DefaultSlidingExpiration = TimeSpan.FromMinutes(10),
            DefaultAbsoluteExpiration = TimeSpan.FromHours(1)
        };

        var memOptions = options.ToMemoryCacheOptions();

        Assert.Equal(TimeSpan.FromMinutes(10), memOptions.SlidingExpiration);
        Assert.Equal(TimeSpan.FromHours(1), memOptions.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public void CachedMappingOptions_ToDistributedCacheOptions_CreatesCorrectOptions()
    {
        var options = new CachedMappingOptions
        {
            DefaultSlidingExpiration = TimeSpan.FromMinutes(15),
            DefaultAbsoluteExpiration = TimeSpan.FromHours(2)
        };

        var distOptions = options.ToDistributedCacheOptions();

        Assert.Equal(TimeSpan.FromMinutes(15), distOptions.SlidingExpiration);
        Assert.Equal(TimeSpan.FromHours(2), distOptions.AbsoluteExpirationRelativeToNow);
    }

    #endregion

    #region CachedMapper Tests

    [Fact]
    public void CachedMapper_Map_CachesResult()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var innerMapper = config.CreateMapper();
        var cachedMapper = new CachedMapper(innerMapper, cache);

        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var dto1 = cachedMapper.Map<UserDto>(user);
        var dto2 = cachedMapper.Map<UserDto>(user);

        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.Equal(dto1.Id, dto2.Id);
    }

    [Fact]
    public void CachedMapper_MapGeneric_CachesResult()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var innerMapper = config.CreateMapper();
        var cachedMapper = new CachedMapper(innerMapper, cache);

        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };

        var dto = cachedMapper.Map<User, UserDto>(user);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public void CachedMapper_MapInPlace_DoesNotCache()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var innerMapper = config.CreateMapper();
        var cachedMapper = new CachedMapper(innerMapper, cache);

        var user = new User { Id = 1, FirstName = "John", LastName = "Doe" };
        var dto = new UserDto();

        var result = cachedMapper.Map(user, dto);

        Assert.Equal(1, result.Id);
        Assert.Equal("John", result.FirstName);
    }

    [Fact]
    public void CachedMapper_NullSource_ReturnsDefault()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var innerMapper = config.CreateMapper();
        var cachedMapper = new CachedMapper(innerMapper, cache);

        var dto = cachedMapper.Map<UserDto>(null);

        Assert.Null(dto);
    }

    [Fact]
    public void CachedMapper_WithCustomOptions_UsesOptions()
    {
        var cache = CreateMemoryCache();
        var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
        var innerMapper = config.CreateMapper();
        var options = new CachedMappingOptions
        {
            DefaultSlidingExpiration = TimeSpan.FromSeconds(30)
        };
        var cachedMapper = new CachedMapper(innerMapper, cache, options);

        var user = new User { Id = 1, FirstName = "John" };
        var dto = cachedMapper.Map<UserDto>(user);

        Assert.NotNull(dto);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void MapCollectionToCached_EmptyCollection_ReturnsEmptyList()
    {
        var cache = CreateMemoryCache();
        var users = new List<object>();

        var dtos = users.MapCollectionToCached<UserDto>(cache, u => $"user:{((User)u).Id}");

        Assert.Empty(dtos);
    }

    [Fact]
    public void MapCollectionToCached_NullCollection_ReturnsEmptyList()
    {
        var cache = CreateMemoryCache();
        List<object>? users = null;

        var dtos = users.MapCollectionToCached<UserDto>(cache, u => "key");

        Assert.Empty(dtos);
    }

    #endregion
}
