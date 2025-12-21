# Mapsicle 🍦

[![NuGet](https://img.shields.io/nuget/v/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![Downloads](https://img.shields.io/nuget/dt/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/T6T01CQT4R)

**Mapsicle** is a high-performance, modular object mapping ecosystem for .NET. Choose only what you need:

| Package                      | Purpose                  | Dependencies    |
| :--------------------------- | :----------------------- | :-------------- |
| **Mapsicle**                 | Zero-config mapping      | None            |
| **Mapsicle.Fluent**          | Fluent configuration     | Mapsicle        |
| **Mapsicle.EntityFramework** | EF Core `ProjectTo<T>()` | Mapsicle.Fluent |

> *"The fastest mapping is the one you don't have to configure."*

---

## 🚀 Why Switch from AutoMapper?

> ⚠️ **AutoMapper is now commercial software.** As of version 13+, AutoMapper requires a paid license. Mapsicle is **100% free and MPL 2.0 licensed** forever.

| Feature              | Mapsicle         | AutoMapper   |
| :------------------- | :--------------- | :----------- |
| **License**          | **MPL 2.0 (Free)**   | Commercial   |
| **Dependencies**     | **0**            | 5+           |
| **Setup Required**   | **None**         | Profiles, DI |
| **Circular Refs**    | **Handled**      | Crash        |
| **Binary Size**      | **~25KB**        | ~500KB+      |
| **Memory Bounded**   | **LRU Option**   | No           |
| **Cache Statistics** | **Yes**          | No           |

---

## 🚦 Quick Start

### Complete Example (Copy & Paste)

```csharp
using Mapsicle;

// 1. Define your types
public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

// 2. Map - that's it! No configuration needed
var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@example.com" };
var dto = user.MapTo<UserDto>();  // FirstName and LastName copied automatically

// 3. Map collections
List<User> users = GetUsers();
List<UserDto> dtos = users.MapTo<UserDto>();  // Entire list mapped
```

**Requirements:** .NET Standard 2.0+ or .NET 6.0+
**Installation:** `dotnet add package Mapsicle`

### Which Package Do I Need?

```
Do you need EF Core query translation (ProjectTo)?
├─ YES → Install: Mapsicle + Mapsicle.Fluent + Mapsicle.EntityFramework
└─ NO
   ├─ Do you need custom mapping logic (ForMember, hooks)?
   │  ├─ YES → Install: Mapsicle + Mapsicle.Fluent
   │  └─ NO → Install: Mapsicle (core only - zero config)
```

| Scenario | Packages Needed |
|:---------|:----------------|
| Simple POCO mapping | `Mapsicle` |
| API DTOs with transformations | `Mapsicle.Fluent` (includes core) |
| EF Core with SQL projection | All three packages |

---

## 📊 Benchmark Results

Real benchmarks on Apple M1, .NET 8.0, BenchmarkDotNet v0.13.12:

### Core Mapping Performance

| Scenario             | Manual |  Mapsicle | AutoMapper |      Winner       |
| :------------------- | -----: | --------: | ---------: | :---------------: |
| **Single Object**    |  31 ns | **59 ns** |      72 ns | ⭐ Mapsicle (+22%) |
| **Flattening**       |  14 ns | **29 ns** |      56 ns | ⭐ Mapsicle (+93%) |
| **Collection (100)** | 3.5 μs |    5.5 μs |     4.0 μs |    AutoMapper     |

### Edge Case Performance

| Scenario                     | Mapsicle      | AutoMapper    | Notes                     |
| :--------------------------- | :------------ | :------------ | :------------------------ |
| **Deep Nesting (15 levels)** | ✅ Safe        | ✅ Safe        | Both handle with MaxDepth |
| **Circular References**      | ✅ Handled     | ❌ Crashes     | **Mapsicle wins**         |
| **Large Collection (10K)**   | **4 ms**      | 4 ms          | Comparable                |
| **Parallel (1000 threads)**  | ✅ Thread-safe | ✅ Thread-safe | Lock-free reads           |

### Performance Optimizations (v1.1+)

| Optimization                   | Improvement                       | Status |
| :----------------------------- | :-------------------------------- | :----- |
| **Lock-free cache reads**      | Eliminates contention             | ✅      |
| **Collection mapper caching**  | +20% for collections (v1.1)       | ✅      |
| **PropertyInfo caching**       | +15% faster cold starts           | ✅      |
| **Primitive fast path**        | Skips depth tracking              | ✅      |
| **Cached compiled actions**    | No runtime reflection             | ✅      |
| **LRU cache option**           | Memory-bounded in long-run apps   | ✅      |
| **Collection pre-allocation**  | Capacity hints for known sizes    | ✅      |

### Memory & Cache Statistics (v1.1+)

```csharp
// Enable memory-bounded caching
Mapper.UseLruCache = true;
Mapper.MaxCacheSize = 1000;  // Default

// Monitor cache performance
var stats = Mapper.CacheInfo();
Console.WriteLine($"Cache entries: {stats.Total}");
Console.WriteLine($"Hit ratio: {stats.HitRatio:P1}");  // Only when LRU enabled
Console.WriteLine($"Hits: {stats.Hits}, Misses: {stats.Misses}");
```

| Feature                  | Mapsicle (Unbounded) | Mapsicle (LRU) | AutoMapper |
| :----------------------- | :------------------- | :------------- | :--------- |
| **Memory Bounded**       | ❌                    | ✅              | ❌          |
| **Cache Statistics**     | Entry count only     | Full stats     | ❌          |
| **Configurable Limit**   | ❌                    | ✅              | ❌          |
| **Lock-Free Reads**      | ✅                    | ✅              | Partial    |

### Smoke Test Results (10,000 mappings)

```
✓ Core: 10,000 mappings in 19ms
✓ Fluent: 10,000 mappings in 10ms
✓ Deep nesting (10 levels): 1,000 mappings in 3ms
✓ Large collection (10,000 items): 4ms
```

> 💡 **Key Insight**: Mapsicle wins on simple/flattened mappings and safety. Both vastly outperform reflection-based approaches.

### Run Benchmarks Yourself

```bash
cd tests/Mapsicle.Benchmarks
dotnet run -c Release              # Full suite
dotnet run -c Release -- --quick   # Smoke test
dotnet run -c Release -- --edge    # Edge cases only
```

---

## 📦 Installation

```bash
# Core package - zero config
dotnet add package Mapsicle

# Fluent configuration (optional)
dotnet add package Mapsicle.Fluent

# EF Core ProjectTo (optional)
dotnet add package Mapsicle.EntityFramework
```

---

## ⚡ Package 1: Mapsicle (Core)

### Basic Mapping
```csharp
using Mapsicle;

var dto = user.MapTo<UserDto>();              // Single object
List<UserDto> dtos = users.MapTo<UserDto>();  // Collection
var flat = order.MapTo<OrderFlatDto>();       // Auto-flattening
```

### Attributes
```csharp
public class UserDto
{
    [MapFrom("UserName")]  // Map from different property
    public string Name { get; set; }

    [IgnoreMap]             // Never mapped
    public string Secret { get; set; }
}
```

### Stability Features (NEW!)
```csharp
// Cycle Detection - no more StackOverflow
Mapper.MaxDepth = 32;  // Default, configurable

// Validation at startup
Mapper.AssertMappingValid<User, UserDto>();

// Logging
Mapper.Logger = Console.WriteLine;

// Memory-bounded caching (prevents memory leaks in long-running apps)
Mapper.UseLruCache = true;   // Enable LRU cache
Mapper.MaxCacheSize = 1000;  // Limit cache entries

// Cache statistics
var stats = Mapper.CacheInfo();
Console.WriteLine($"Hit ratio: {stats.HitRatio:P1}");

// Scoped instances with isolated caches
using var mapper = MapperFactory.Create();
var dto = mapper.MapTo<UserDto>(user);  // Uses isolated cache
```

---

## ⚡ Package 2: Mapsicle.Fluent

### Basic Configuration
```csharp
using Mapsicle.Fluent;

var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<User, UserDto>()
        .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.First} {s.Last}"))
        .ForMember(d => d.Password, opt => opt.Ignore())
        .ForMember(d => d.Status, opt => opt.Condition(s => s.IsActive));
});

config.AssertConfigurationIsValid();
var mapper = config.CreateMapper();
```

### DI Integration (NEW!)
```csharp
// In Program.cs
services.AddMapsicle(cfg =>
{
    cfg.CreateMap<User, UserDto>();
}, validateConfiguration: true);

// In your service
public class UserService(IMapper mapper)
{
    public UserDto GetUser(User user) => mapper.Map<UserDto>(user);
}
```

### Lifecycle Hooks (NEW!)
```csharp
cfg.CreateMap<Order, OrderDto>()
    .BeforeMap((src, dest) => dest.CreatedAt = DateTime.UtcNow)
    .AfterMap((src, dest) => dest.WasProcessed = true);
```

### Polymorphic Mapping (NEW!)
```csharp
cfg.CreateMap<Vehicle, VehicleDto>()
    .Include<Car, CarDto>()
    .Include<Truck, TruckDto>();
```

### Custom Construction (NEW!)
```csharp
cfg.CreateMap<Order, OrderDto>()
    .ConstructUsing(src => OrderFactory.Create(src.Type));
```

### Global Type Converters (NEW!)
```csharp
cfg.CreateConverter<Money, decimal>(m => m.Amount);
cfg.CreateConverter<Money, string>(m => $"{m.Currency} {m.Amount}");
```

---

## ⚡ Package 3: Mapsicle.EntityFramework

**`ProjectTo<T>()`** that translates to SQL—no in-memory loading!

```csharp
using Mapsicle.EntityFramework;

var dtos = await _context.Users
    .Where(u => u.IsActive)
    .ProjectTo<UserEntity, UserDto>()
    .ToListAsync();

// Flattening in SQL: Customer.Name → CustomerName
var orders = _context.Orders
    .ProjectTo<OrderEntity, OrderFlatDto>()
    .ToList();
```

### ProjectTo with Fluent Configuration (NEW!)
```csharp
// ForMember expressions are translated to SQL!
var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<Order, OrderDto>()
        .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer.FirstName + " " + s.Customer.LastName))
        .ForMember(d => d.Total, opt => opt.MapFrom(s => s.Lines.Sum(l => l.Quantity * l.UnitPrice)));
});

// These expressions translate to SQL queries
var orders = _context.Orders.ProjectTo<Order, OrderDto>(config).ToList();
```

---

## 🔧 Migration from AutoMapper

### API Compatibility

| AutoMapper                 | Mapsicle                              |
| :------------------------- | :------------------------------------ |
| `CreateMap<S,D>()`         | Same!                                 |
| `ForMember().MapFrom()`    | Same!                                 |
| `.Ignore()`                | Same!                                 |
| `BeforeMap/AfterMap`       | Same!                                 |
| `Include<Derived>()`       | Same!                                 |
| `ConstructUsing()`         | Same!                                 |
| `services.AddAutoMapper()` | `services.AddMapsicle()`              |
| `_mapper.Map<T>()`         | `mapper.Map<T>()` or `obj.MapTo<T>()` |

### Step-by-Step Migration Guide

#### 1. Identify Your AutoMapper Usage

**Simple mappings (no profiles)** → Use core `Mapsicle` package
**Profiles with configuration** → Use `Mapsicle.Fluent`
**EF Core ProjectTo** → Use `Mapsicle.EntityFramework`

#### 2. Install Packages

```bash
dotnet remove package AutoMapper
dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection
dotnet add package Mapsicle.Fluent  # Includes core
```

#### 3. Convert Profiles to Configuration

**Before (AutoMapper):**
```csharp
public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
    }
}
```

**After (Mapsicle):**
```csharp
// In Program.cs/Startup.cs
services.AddMapsicle(cfg =>
{
    cfg.CreateMap<User, UserDto>()
        .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
}, validateConfiguration: true);
```

#### 4. Update DI Registration

**Before:**
```csharp
services.AddAutoMapper(typeof(UserProfile).Assembly);
```

**After:**
```csharp
services.AddMapsicle(cfg =>
{
    cfg.CreateMap<User, UserDto>();
    cfg.CreateMap<Order, OrderDto>();
    // ... all your mappings
}, validateConfiguration: true);
```

#### 5. Update Mapping Calls

**Before:**
```csharp
public class UserService
{
    private readonly IMapper _mapper;

    public UserService(IMapper mapper) => _mapper = mapper;

    public UserDto GetUser(User user) => _mapper.Map<UserDto>(user);
}
```

**After (same interface!):**
```csharp
public class UserService
{
    private readonly IMapper _mapper;

    public UserService(IMapper mapper) => _mapper = mapper;

    // Option 1: Same as AutoMapper
    public UserDto GetUser(User user) => _mapper.Map<UserDto>(user);

    // Option 2: Extension method (no DI needed for simple cases)
    public UserDto GetUser(User user) => user.MapTo<UserDto>();
}
```

### Known Incompatibilities

❌ **Not Supported:**
- Custom naming conventions (PascalCase → camelCase)
- `IMemberValueResolver` interface - use `ResolveUsing(func)` instead
- `ITypeConverter` interface - use `CreateConverter<T, U>()` instead
- Conditional mapping with complex predicates
- MaxDepth per individual mapping (only global `Mapper.MaxDepth`)

⚠️ **Behavioral Differences:**
- **Circular references**: AutoMapper throws exception, Mapsicle returns default value
- **Unmapped properties**: Both ignore, but Mapsicle has `GetUnmappedProperties<T, U>()` for validation
- **Null handling**: Both return null for null source, but Mapsicle is more aggressive with null-safe navigation

---

## 🛠️ Troubleshooting

### Common Issues

#### Issue: Properties Not Mapping

**Symptom:** Destination properties remain default/null after mapping

**Causes & Solutions:**

1. **Property name mismatch**
   ```csharp
   // Problem: Source has "UserName", destination has "Name"

   // Solution 1: Use [MapFrom] attribute
   public class UserDto
   {
       [MapFrom("UserName")]
       public string Name { get; set; }
   }

   // Solution 2: Use Fluent configuration
   cfg.CreateMap<User, UserDto>()
       .ForMember(d => d.Name, opt => opt.MapFrom(s => s.UserName));
   ```

2. **Property not readable/writable**
   ```csharp
   // ❌ Won't map (no setter)
   public string Name { get; }

   // ✅ Will map
   public string Name { get; set; }

   // ✅ Also works (init setter)
   public string Name { get; init; }
   ```

3. **Type incompatibility**
   ```csharp
   // Check which properties can't map
   var unmapped = Mapper.GetUnmappedProperties<User, UserDto>();
   Console.WriteLine($"Unmapped: {string.Join(", ", unmapped)}");
   ```

#### Issue: StackOverflowException

**Cause:** Circular references exceeding MaxDepth (default 32)

**Solutions:**
```csharp
// Solution 1: Increase depth limit
Mapper.MaxDepth = 64;

// Solution 2: Enable logging to see depth warnings
Mapper.Logger = msg => Console.WriteLine($"[Mapsicle] {msg}");

// Solution 3: Use [IgnoreMap] to break cycle
public class User
{
    public int Id { get; set; }

    [IgnoreMap]  // Don't map back to parent
    public List<Order> Orders { get; set; }
}
```

#### Issue: Poor Collection Mapping Performance

**Symptom:** Mapping 10,000+ items is slow

**Solutions:**
```csharp
// ❌ Don't: Map items individually
foreach (var user in users)
{
    dtos.Add(user.MapTo<UserDto>());
}

// ✅ Do: Map entire collection
var dtos = users.MapTo<UserDto>();  // 20% faster with cached mapper

// ✅ Do: Pre-warm cache at startup for frequently used types
new User().MapTo<UserDto>();
new Order().MapTo<OrderDto>();
```

#### Issue: Memory Growth in Long-Running Apps

**Symptom:** Memory usage grows over time

**Cause:** Unbounded cache with many dynamic type combinations

**Solution:**
```csharp
// Enable memory-bounded LRU cache
Mapper.UseLruCache = true;
Mapper.MaxCacheSize = 1000;  // Adjust based on # of unique type pairs

// Monitor cache performance
var stats = Mapper.CacheInfo();
if (stats.HitRatio < 0.8)
{
    // Consider increasing cache size
    Mapper.MaxCacheSize = 2000;
}
```

#### Issue: EF Core ProjectTo Not Working

**Symptom:** Exception thrown or results incorrect

**Common Causes:**
1. **Missing configuration**
   ```csharp
   // ❌ Don't use convention mapping with complex expressions
   var dtos = context.Orders.ProjectTo<Order, OrderDto>().ToList();

   // ✅ Pass configuration for ForMember expressions
   var config = new MapperConfiguration(cfg =>
   {
       cfg.CreateMap<Order, OrderDto>()
           .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer.Name));
   });
   var dtos = context.Orders.ProjectTo<Order, OrderDto>(config).ToList();
   ```

2. **Non-translatable expressions**
   ```csharp
   // ❌ Method calls that don't translate to SQL
   cfg.CreateMap<User, UserDto>()
       .ForMember(d => d.Name, opt => opt.ResolveUsing(u => FormatName(u)));

   // ✅ Use expressions that translate to SQL
   cfg.CreateMap<User, UserDto>()
       .ForMember(d => d.Name, opt => opt.MapFrom(u => u.FirstName + " " + u.LastName));
   ```

### Debugging Tips

```csharp
// 1. Enable verbose logging
Mapper.Logger = msg => _logger.LogDebug($"[Mapsicle] {msg}");

// 2. Validate mapping at startup
#if DEBUG
Mapper.AssertMappingValid<User, UserDto>();
#endif

// 3. Check configuration in fluent mapper
config.AssertConfigurationIsValid();

// 4. Monitor cache statistics
var stats = Mapper.CacheInfo();
_logger.LogInformation($"Cache: {stats.Total} entries, Hit ratio: {stats.HitRatio:P1}");

// 5. Use MapperFactory for isolated testing
using var mapper = MapperFactory.Create(new MapperOptions
{
    MaxDepth = 16,
    Logger = Console.WriteLine
});
var dto = mapper.MapTo<UserDto>(user);
```

---

## ⚠️ Known Limitations

### Feature Limitations

❌ **Not Supported:**
- Custom naming conventions (e.g., PascalCase → camelCase)
- Async mapping operations
- Source/destination value injection (context passing)
- Open generic types
- Explicit type conversion configuration beyond built-ins

⚠️ **Partial Support:**
- Nested flattening limited to 1 level (`Address.City` ✅, `Address.Street.Line1` ❌)
- Collection mapping ~27% slower than AutoMapper for 100-1000 items (competitive on 10K+)
- EF Core ProjectTo works with `ForMember` expressions, but not `ResolveUsing` delegates

### Behavioral Differences from AutoMapper

- **Circular references**: Returns default value instead of throwing exception
- **Null safety**: More aggressive null-safe navigation (fewer NullReferenceException)
- **Unmapped properties**: Silent (use `GetUnmappedProperties` for validation)
- **Cache behavior**: Default is unbounded (must opt-in to LRU)

### Platform Support

| .NET Version | Mapsicle Support |
|:-------------|:-----------------|
| .NET 8.0 | ✅ Fully supported |
| .NET 6.0-7.0 | ✅ Via .NET Standard 2.0 |
| .NET 5.0 | ✅ Via .NET Standard 2.0 |
| .NET Core 2.0+ | ✅ Via .NET Standard 2.0 |
| .NET Framework 4.6.1+ | ✅ Via .NET Standard 2.0 |

---

## 📚 API Reference

### Core Extensions (`using Mapsicle`)

#### `MapTo<T>(this object source)`

Maps a source object to a new instance of type T.

**Parameters:**
- `source` - The source object to map from

**Returns:**
- `T?` - New instance of T with mapped properties, or `default(T)` if source is null or max depth exceeded

**Example:**
```csharp
var dto = user.MapTo<UserDto>();
```

---

#### `MapTo<T>(this IEnumerable source)`

Maps a collection to a List<T>.

**Parameters:**
- `source` - The source collection

**Returns:**
- `List<T>` - New list with mapped items (empty if source is null)

**Optimization:** Pre-allocates capacity if source implements ICollection

**Example:**
```csharp
List<UserDto> dtos = users.MapTo<UserDto>();
```

---

#### `Map<TDest>(this object source, TDest destination)`

Updates an existing destination object from source.

**Parameters:**
- `source` - The source object
- `destination` - The destination object to update

**Returns:**
- `TDest` - The updated destination (same instance)

**Example:**
```csharp
source.Map(existingDto);  // Updates existingDto in-place
```

---

#### `ToDictionary(this object source)`

Converts an object to a dictionary of property name/value pairs.

**Returns:**
- `Dictionary<string, object?>` - Case-insensitive dictionary

**Example:**
```csharp
var dict = user.ToDictionary();
```

---

#### `MapTo<T>(this IDictionary<string, object?> source) where T : new()`

Maps a dictionary to an object.

**Constraints:**
- T must have a parameterless constructor

**Example:**
```csharp
var user = dict.MapTo<User>();
```

---

### Static Mapper Configuration

#### `Mapper.MaxDepth`
- **Type:** `int`
- **Default:** `32`
- **Description:** Maximum recursion depth before returning default value (circular reference protection)

```csharp
Mapper.MaxDepth = 64;
```

---

#### `Mapper.UseLruCache`
- **Type:** `bool`
- **Default:** `false`
- **Description:** Enables memory-bounded LRU cache. Clears all caches when changed.

```csharp
Mapper.UseLruCache = true;
```

---

#### `Mapper.MaxCacheSize`
- **Type:** `int`
- **Default:** `1000`
- **Description:** Maximum cache entries when UseLruCache is enabled

```csharp
Mapper.MaxCacheSize = 2000;
```

---

#### `Mapper.Logger`
- **Type:** `Action<string>?`
- **Default:** `null`
- **Description:** Logger for diagnostic messages (depth warnings, etc)

```csharp
Mapper.Logger = msg => _logger.LogDebug(msg);
```

---

#### `Mapper.ClearCache()`
Clears all cached mapping delegates.

```csharp
Mapper.ClearCache();
```

---

#### `Mapper.CacheInfo()`
- **Returns:** `MapperCacheInfo` - Current cache statistics

```csharp
var stats = Mapper.CacheInfo();
Console.WriteLine($"Total: {stats.Total}, Hit Ratio: {stats.HitRatio:P1}");
```

---

#### `Mapper.AssertMappingValid<TSource, TDest>()`
Validates mapping configuration. Throws `InvalidOperationException` if unmapped properties exist.

```csharp
Mapper.AssertMappingValid<User, UserDto>();
```

---

#### `Mapper.GetUnmappedProperties<TSource, TDest>()`
- **Returns:** `List<string>` - Names of destination properties that cannot be mapped

```csharp
var unmapped = Mapper.GetUnmappedProperties<User, UserDto>();
```

---

### MapperFactory

#### `MapperFactory.Create(MapperOptions? options = null)`
Creates an isolated mapper instance with independent cache and depth tracking.

**Parameters:**
- `options` - Optional configuration (MaxDepth, Logger, UseLruCache, MaxCacheSize)

**Returns:**
- `IDisposable` mapper instance

**Example:**
```csharp
using var mapper = MapperFactory.Create(new MapperOptions
{
    MaxDepth = 16,
    UseLruCache = true,
    MaxCacheSize = 100,
    Logger = Console.WriteLine
});
var dto = mapper.MapTo<UserDto>(user);
```

---

### Fluent API (`using Mapsicle.Fluent`)

#### `MapperConfiguration`

```csharp
var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<User, UserDto>()
        .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName))
        .ForMember(d => d.Password, opt => opt.Ignore())
        .ForMember(d => d.IsActive, opt => opt.Condition(s => s.Status == "Active"))
        .BeforeMap((src, dest) => Console.WriteLine("Mapping started"))
        .AfterMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
        .Include<PowerUser, PowerUserDto>()
        .ConstructUsing(src => new UserDto(src.Id))
        .ReverseMap();

    cfg.CreateConverter<Money, decimal>(m => m.Amount);
});

config.AssertConfigurationIsValid();
var mapper = config.CreateMapper();
```

#### Configuration Methods

- **`ForMember<TMember>()`** - Configure individual member mapping
  - `opt.MapFrom(expr)` - Map from custom expression
  - `opt.Ignore()` - Don't map this member
  - `opt.Condition(pred)` - Conditional mapping
  - `opt.ResolveUsing(func)` - Custom resolver function

- **`BeforeMap(action)`** - Execute action before mapping
- **`AfterMap(action)`** - Execute action after mapping
- **`Include<TDerived, TDest>()`** - Polymorphic mapping support
- **`ConstructUsing(factory)`** - Custom object construction
- **`ReverseMap()`** - Create reverse mapping
- **`CreateConverter<TSource, TDest>(converter)`** - Global type converter

---

### EntityFramework Extensions (`using Mapsicle.EntityFramework`)

#### `ProjectTo<TSource, TDest>(this IQueryable<TSource> query, MapperConfiguration? config = null)`

Translates mapping to SQL expression (executed in database).

**Parameters:**
- `query` - Source EF Core queryable
- `config` - Optional mapper configuration for custom mappings

**Returns:**
- `IQueryable<TDest>` - Queryable projection

**Example:**
```csharp
var dtos = await context.Users
    .Where(u => u.IsActive)
    .ProjectTo<User, UserDto>(config)
    .ToListAsync();
```

---

## 📝 Complete Feature List

### Core Features
- ✅ Zero-config convention mapping
- ✅ Collection mapping (List, Array, IEnumerable)
- ✅ Dictionary mapping (object ↔ Dictionary)
- ✅ Flattening (`AddressCity` → `Address.City`)
- ✅ Nullable type coercion (`T` ↔ `T?`)
- ✅ Enum to numeric conversion
- ✅ Nested object mapping
- ✅ Case-insensitive property matching
- ✅ Record type support (positional parameters)
- ✅ Anonymous type support
- ✅ Circular reference protection
- ✅ Thread-safe caching

### Advanced Features
- ✅ `[MapFrom]` attribute
- ✅ `[IgnoreMap]` attribute
- ✅ Fluent configuration API
- ✅ ForMember custom expressions
- ✅ BeforeMap/AfterMap hooks
- ✅ Polymorphic mapping (`.Include<>`)
- ✅ Custom construction (`.ConstructUsing`)
- ✅ Global type converters
- ✅ Conditional mapping
- ✅ ReverseMap
- ✅ DI integration
- ✅ Configuration validation

### Enterprise Features
- ✅ LRU cache option (memory-bounded)
- ✅ Cache statistics (hits, misses, ratio)
- ✅ PropertyInfo caching
- ✅ Lock-free reads
- ✅ Isolated mapper instances
- ✅ Configurable depth limits
- ✅ Diagnostic logging
- ✅ Unmapped property detection

### EF Core Features
- ✅ ProjectTo with SQL translation
- ✅ ForMember in ProjectTo
- ✅ Flattening in SQL
- ✅ Nested projection
- ✅ Type coercion in queries

---

## 🧪 Test Coverage

| Package                  |  Tests | Coverage            |
| :----------------------- | -----: | :------------------ |
| Mapsicle                 |     67 | Core + Stability    |
| Mapsicle.Fluent          |     18 | Fluent + Enterprise |
| Mapsicle.EntityFramework |      7 | EF Core             |
| **Total**                | **92** |                     |

---

## 📁 Project Structure

```
Mapsicle/
├── src/
│   ├── Mapsicle/                  # Core - zero config
│   ├── Mapsicle.Fluent/           # Fluent + DI
│   └── Mapsicle.EntityFramework/  # EF Core ProjectTo
└── tests/
    ├── Mapsicle.Tests/
    ├── Mapsicle.Fluent.Tests/
    ├── Mapsicle.EntityFramework.Tests/
    └── Mapsicle.Benchmarks/
```

---

## 🤝 Contributing

PRs welcome! Areas for contribution:
- Performance optimizations
- Additional type coercion scenarios
- Documentation improvements

---

## 📄 License

MPL 2.0 License © [Arnel Isiderio Robles](https://github.com/arnelirobles)

---

<p align="center">
  <strong>Stop configuring. Start mapping.</strong><br>
  <em>Free forever. Zero dependencies. Pure performance.</em>
</p>
