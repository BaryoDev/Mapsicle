# Mapsicle 🍦

[![NuGet](https://img.shields.io/nuget/v/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![Downloads](https://img.shields.io/nuget/dt/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![License](https://img.shields.io/github/license/BaryoDev/Mapsicle)](LICENSE)

**Mapsicle** is a high-performance, zero-dependency object mapper for .NET. It uses compiled Expression Trees to match properties at near-native speed, designed as a lightweight alternative to lightweight libraries.

## Author
*   [Arnel Isiderio Robles](https://github.com/arnelirobles)

## The Pitch

| Feature              | Mapsicle                | AutoMapper               |
| :------------------- | :---------------------- | :----------------------- |
| **Dependencies**     | **0 (Zero)**            | many                     |
| **Setup**            | **Instant (No Config)** | specific config/profiles |
| **Performance**      | **Near Native**         | Slower startup/first run |
| **Case Insensitive** | **Yes**                 | Configurable             |
| **Size**             | **~100 Lines**          | Large codebase           |

## Quick Start
Install via NuGet:
```bash
dotnet add package Mapsicle
```

### 1. Simple Object Mapping
Just call `.MapTo<T>()` on any object. Matches properties by name (case-insensitive).
```csharp
using Mapsicle;

var user = new User { Id = 1, Name = "Alice", Email = "alice@mail.com" };

// Map to UserDto
UserDto dto = user.MapTo<UserDto>();
```

### 2. List Mapping
Works seamlessly with lists and collections.
```csharp
var users = new List<User> 
{
    new User { Name = "Alice" },
    new User { Name = "Bob" }
};

// Map to valid List<UserDto>
List<UserDto> dtos = users.MapTo<UserDto>().ToList();
```

## Behavior & Limitations
Mapsicle is designed to be **simple** and **fast**. It follows **Strict Type Matching**:

*   **Property Matching**: Properties are matched by name (case-insensitive).
*   **Type Compatibility**: Properties are only mapped if the Source type is assignable to the Destination type.
    *   `string` -> `string`: ✅ Mapped
    *   `int` -> `int`: ✅ Mapped
    *   `User` -> `User` (Same Class): ✅ Mapped (Reference Copy)
    *   `SubClass` -> `BaseClass`: ✅ Mapped
    *   `ClassA` -> `ClassB` (Different Classes): ✅ **Mapped** (Deep Copy via Recursive Mapping)
    *   `int` -> `int?`: ❌ **Skipped** (Strict type match)
    *   `int` -> `string`: ❌ **Skipped**
    *   `ClassA` -> `ClassB` (Different Classes): ❌ **Skipped** (No deep/internal mapping)
*   **Unmatched Properties**: Properties in Source or Destination that do not match are simply ignored (no errors).

## Performance Logic
**Mapsicle** achieves its speed by using **System.Linq.Expressions** to generate and compile mapping code dynamically at runtime.

1.  **First Run:** When you call `.MapTo<T>()` for the first time on a pair of types (e.g., `User` -> `UserDto`), Mapsicle inspects the properties of both types.
2.  **Compilation:** It builds a specialized delegate (function) that copies values from source to destination, handling type checks and assignments. This delegate is compiled into native code.
3.  **Caching:** This compiled delegate is stored in a `ConcurrentDictionary`.
4.  **Subsequent Runs:** Every future call fetches the compiled delegate from the cache and executes it immediately, incurring overhead comparable to writing the mapping code by hand.

## License
MIT
