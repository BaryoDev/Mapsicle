# Mapsicle 🍦

[![NuGet](https://img.shields.io/nuget/v/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![Downloads](https://img.shields.io/nuget/dt/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/T6T01CQT4R)

**Mapsicle** is a high-performance, **zero-dependency** object mapper for .NET. It uses compiled Expression Trees to achieve near-native mapping speed with zero configuration required.

> *"The fastest mapping is the one you don't have to configure."*

---

## 🚀 Why Switch from AutoMapper?

> ⚠️ **AutoMapper is now commercial software.** As of version 13+, AutoMapper requires a paid license for commercial use. Mapsicle remains **100% free and MPL 2.0 licensed** forever.

| Feature              | Mapsicle                   | AutoMapper                  |
| :------------------- | :------------------------- | :-------------------------- |
| **License**          | **MPL 2.0 (Free Forever)**     | Commercial (Paid)           |
| **Dependencies**     | **0 (Zero)**               | 5+ packages                 |
| **Setup Required**   | **None**                   | Profiles, CreateMap, DI     |
| **Binary Size**      | **~15KB**                  | ~500KB+                     |
| **Flattening**       | **Built-in by convention** | Requires `ForMember` config |
| **Case Sensitivity** | **Case-insensitive**       | Configurable                |

### Real Talk: When to Use What

✅ **Use Mapsicle when:**
- You want zero configuration complexity
- You want to avoid commercial licensing fees
- Performance matters (APIs, high-throughput services)
- You're mapping simple-to-moderate object graphs
- You hate NuGet dependency bloat

⚠️ **Use AutoMapper when:**
- You need complex `ForMember` logic with external dependencies
- You're deeply invested in AutoMapper's ecosystem and have a commercial license

---

## 📊 Benchmark Results

Real benchmarks using BenchmarkDotNet on Apple M1, .NET 8.0:

| Scenario             | Manual |  Mapsicle | AutoMapper | Winner                  |
| :------------------- | -----: | --------: | ---------: | :---------------------- |
| **Single Object**    |  31 ns | **59 ns** |      72 ns | 🥇 Mapsicle              |
| **Flattening**       |  14 ns | **29 ns** |      56 ns | 🥇 Mapsicle (2x faster!) |
| **Collection (100)** | 3.5 μs |    7.0 μs |     4.0 μs | AutoMapper              |

**Key Insights:**
- **Single object mapping**: Mapsicle is **22% faster** than AutoMapper
- **Flattening**: Mapsicle is **48% faster** than AutoMapper (no config needed!)
- **Cold start**: ~284μs for first mapping (expression compilation)
- **Memory**: Same allocation as AutoMapper (120 bytes for single object)

Run benchmarks yourself:
```bash
cd tests/Mapsicle.Benchmarks
dotnet run -c Release
```

---

## 📦 Installation

```bash
dotnet add package Mapsicle
```

**That's it.** No DI registration, no profiles, no configuration.

---

## ⚡ Quick Start

### Basic Mapping
```csharp
using Mapsicle;

var user = new User { Id = 1, Name = "Alice", Email = "alice@mail.com" };

// One line. That's it.
UserDto dto = user.MapTo<UserDto>();
```

### Collection Mapping
```csharp
var users = new List<User> { new() { Name = "Alice" }, new() { Name = "Bob" } };

// Direct List<T> result
List<UserDto> dtos = users.MapTo<UserDto>();

// Or as an array
UserDto[] array = users.MapToArray<UserDto>();
```

### Map to Existing Object
```csharp
var existing = new UserDto { Id = 1, Name = "Old", Email = "keep@me.com" };
var source = new User { Id = 1, Name = "New" };

source.Map(existing);
// existing.Name = "New", existing.Email = "keep@me.com" (preserved)
```

---

## 🌟 Features

### 1. Automatic Flattening
No configuration needed. Just follow the naming convention:

```csharp
public class Order { public Customer Customer { get; set; } }
public class Customer { public string Email { get; set; } }

// Destination DTO
public class OrderDto
{
    public string CustomerEmail { get; set; }  // Auto-mapped from Customer.Email!
}

var dto = order.MapTo<OrderDto>();  // CustomerEmail is populated automatically
```

### 2. Attribute-Based Configuration

```csharp
public class UserDto
{
    [MapFrom("UserName")]           // Map from different property name
    public string Name { get; set; }

    [IgnoreMap]                      // Never mapped
    public string Password { get; set; } = "NOT_MAPPED";
}
```

### 3. Dictionary Mapping

```csharp
// Object to Dictionary
var dict = user.ToDictionary();
// { "Id": 1, "Name": "Alice", "Email": "alice@mail.com" }

// Dictionary to Object
var restored = dict.MapTo<User>();
```

### 4. Smart Type Coercion

| Source → Destination  | Supported             |
| :-------------------- | :-------------------- |
| `string` → `string`   | ✅                     |
| `int` → `string`      | ✅ (via ToString)      |
| `Enum` → `int`        | ✅                     |
| `int` → `int?`        | ✅                     |
| `int?` → `int`        | ✅ (default if null)   |
| `ClassA` → `ClassB`   | ✅ (recursive mapping) |
| `List<A>` → `List<B>` | ✅                     |
| `IEnumerable` → `T[]` | ✅                     |

### 5. Record & Immutable Type Support
```csharp
public record UserRecord(int Id, string Name);
var rec = source.MapTo<UserRecord>();  // Works via constructor
```

### 6. Cache Management
```csharp
var info = Mapper.CacheInfo();  // { MapToEntries: 5, Total: 5 }
Mapper.ClearCache();            // Reset for testing
```

---

## 🔧 Migration from AutoMapper

### Before (AutoMapper)
```csharp
// Startup.cs
services.AddAutoMapper(typeof(MappingProfile));

// MappingProfile.cs
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, UserDto>();
        CreateMap<Order, OrderDto>()
            .ForMember(d => d.CustomerEmail, opt => opt.MapFrom(s => s.Customer.Email));
    }
}

// Usage
var dto = _mapper.Map<UserDto>(user);
```

### After (Mapsicle)
```csharp
// No startup config needed!

// Usage
var dto = user.MapTo<UserDto>();

// Flattening works automatically with naming convention
// Order.Customer.Email → OrderDto.CustomerEmail ✅
```

---

## ⚠️ Limitations

- **Circular References**: Not supported (will cause `StackOverflowException`)
- **Complex Resolvers**: No `ForMember` with custom logic—use `[MapFrom]` for simple cases
- **Deep Customization**: If you need per-field transformation logic, write it manually

---

## 📁 Project Structure

```
Mapsicle/
├── src/Mapsicle/           # Core library (~400 lines)
└── tests/
    ├── Mapsicle.Tests/     # 56+ unit tests
    └── Mapsicle.Benchmarks/ # Performance benchmarks
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
