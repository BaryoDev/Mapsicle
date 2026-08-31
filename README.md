![Mapsicle](https://raw.githubusercontent.com/BaryoDev/Mapsicle/main/assets/logo-120.png)

# Mapsicle

[![CI](https://github.com/BaryoDev/Mapsicle/actions/workflows/ci.yml/badge.svg)](https://github.com/BaryoDev/Mapsicle/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![Downloads](https://img.shields.io/nuget/dt/Mapsicle.svg)](https://www.nuget.org/packages/Mapsicle)
[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)

[![Sponsor](https://img.shields.io/badge/Sponsor-GitHub_Sponsors-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/arnelirobles)

**Mapsicle** is a high-performance, modular object mapping ecosystem for .NET. Choose only what you need:

> **See it working:** [github.com/arnelirobles/mapsicle_samples](https://github.com/arnelirobles/mapsicle_samples)
> maps one e-commerce order aggregate through Mapsicle, AutoMapper and Mapperly side by side, in a
> CRUD API over SQLite, with an endpoint that reports where the three disagree. Declare a pair and
> Mapsicle matches hand written code on that graph: 288.4 ns against 288.1, allocating the same
> 1.41 KB.


| Package                      | Purpose                           | Dependencies      |
| :--------------------------- | :-------------------------------- | :---------------- |
| **Mapsicle**                 | Zero-config mapping               | None              |
| **Mapsicle.Fluent**          | Fluent configuration + Profiles   | Mapsicle          |
| **Mapsicle.EntityFramework** | EF Core `ProjectTo<T>()`          | Mapsicle.Fluent   |
| **Mapsicle.Validation**      | FluentValidation integration      | Mapsicle.Fluent   |
| **Mapsicle.NamingConventions** | Naming convention support       | Mapsicle.Fluent   |
| **Mapsicle.Json**            | JSON serialization integration    | Mapsicle.Fluent   |
| **Mapsicle.AspNetCore**      | ASP.NET Core Minimal API helpers  | Mapsicle.Validation |
| **Mapsicle.Caching**         | Memory/Distributed cache support  | Mapsicle.Fluent   |
| **Mapsicle.Audit**           | Change tracking/diff detection    | Mapsicle.Fluent   |
| **Mapsicle.DataAnnotations** | DataAnnotations validation        | Mapsicle.Fluent   |
| **Mapsicle.Dapper**          | Dapper query-and-map helpers      | Mapsicle.Fluent   |
| **Mapsicle.Serilog**         | Mapping diagnostics via Serilog   | Mapsicle          |
| **Mapsicle.DependencyInjection** | `AddMapsicle()`, no configuration | Mapsicle      |

> The core `Mapsicle` package has zero dependencies. Extension packages introduce their respective third-party dependencies (listed in the table above).

Zero configuration by default. Configure only where a mapping is genuinely not conventional.

Coming from AutoMapper? [docs/migrating-from-automapper.md](docs/migrating-from-automapper.md) covers what changes, what you can delete, and where Mapsicle is the wrong answer.

---

## Why Choose Mapsicle?

> **AutoMapper 15.0 and later are no longer permissively licensed.** They are governed by
> [RPL-1.5](https://opensource.org/license/rpl-1-5/) or a licence agreement from Lucky Penny
> Software, which includes a free Community License for those who qualify. Earlier versions keep
> their original licence. RPL-1.5 is strong reciprocal: its source obligations reach software that
> is only deployed internally, not just software you distribute. Check the terms against your own
> situation rather than taking this paragraph as advice. Mapsicle is MPL 2.0.

### When Mapsicle is the right choice, and when it is not

Speed is the weakest argument for this library, so it is not the one to lead with. Against a source
generator Mapsicle loses on speed and always will. What it offers is mappings that do not have to
be known when you compile.

**Reach for Mapsicle when:**

| Situation | Why the alternatives do not fit |
| :-------- | :------------------------------ |
| Mapping a `Dictionary<string, object>` into a type | Mapperly has nothing to generate against. AutoMapper needs the pair configured. |
| A collection whose items have different runtime types | Same: nothing to generate, and the shape is only known at runtime. |
| Types arriving from plugins, reflection or configuration | Compile-time generation is not available at all. |
| Hundreds of DTOs and no appetite for a `CreateMap` per pair | Mapsicle maps by convention with no setup. AutoMapper throws when it reaches an unconfigured pair. `AssertConfigurationIsValid()` validates the maps you configured, so it does not catch a pair you never registered. |
| Object graphs that contain cycles | Mapsicle returns the default at `MaxDepth` with no configuration. AutoMapper needs `PreserveReferences()` or `MaxDepth(...)`, and an unhandled cycle can still overflow the stack. Mapperly needs `UseReferenceHandling`. |
| The licence has to be permissive | This is the reason most people are reading this page. |

**Reach for something else when:**

| Situation | Choose |
| :-------- | :----- |
| Every mapping is known at compile time and you will declare them | **Mapperly.** 2.5x to 3x faster and indistinguishable from hand-written code. |
| Collection throughput at around a hundred elements is what your workload is bounded by | **Mapperly**, by about 12 percent over Mapsicle on x64. Mapsicle is ahead of AutoMapper on both architectures. |
| You need AOT with no runtime code generation | **Mapperly.** Mapsicle compiles expression trees at first use. |

Mapsicle is 1.30x to 1.44x faster than AutoMapper on single objects depending on the architecture,
1.09x to 1.20x on collections, and faster again on deeply nested graphs and large collections. That
is real and it is measured below, but it is a supporting argument rather than the reason to switch.

### Quick Comparison

| Feature              | Mapsicle         | AutoMapper   | Mapperly     |
| :------------------- | :--------------- | :----------- | :----------- |
| **License**          | **MPL 2.0**   | RPL-1.5, or a Lucky Penny Software agreement (a free Community License exists) | MIT |
| **Architecture**     | Runtime + Caching, with an opt-in source generator | Runtime + Expressions | Source Generator |
| **Setup Required**   | **None**, or one line per pair to bind it at compile time | Profiles, DI | Partial class |
| **Dependencies**     | **0** (core)     | 8            | 0 (compile-time) |
| **Deployed size**    | **45.5 KB**      | 1,117.4 KB   | 0 at runtime |
| **Warm map, measured** | **1.00x hand written** when the pair is declared, 1.77x when it is not | 2.45x | 1.12x |
| **Compile-time Safety** | Partial. A pair it cannot emit warns and falls back | No | **Full. It will not compile** |
| **AOT Compatible**   | Declared pairs yes, undeclared no | No | **Yes, with no fallback to get wrong** |
| **Circular Refs**    | Stops at a depth ceiling and returns. Safe, but the output holds copies where the input had one | **Preserves the reference by default** (measured on 15.1.3 with a plain `CreateMap`), so the cycle survives intact | Follows it. Default settings overflow the stack and abort the process |
| **Memory Bounded**   | **LRU Option**   | No           | N/A          |
| **Cache Statistics** | **Yes**          | No           | N/A          |
| **Integrated Validation** | **Yes**     | No           | No           |
| **ASP.NET Core Helpers** | **Yes**      | No           | No           |

### Size

Two projects, each referencing one mapper and nothing else, `dotnet publish -c Release` on net8.0:

| | Mapsicle | AutoMapper 15.1.3 |
| :-- | --: | --: |
| the mapper's own assembly | **45.5 KB** | 286.0 KB |
| assemblies it brings with it | **0** | 8 |
| total on disk | **45.5 KB** | **1,117.4 KB** |

More than half of what AutoMapper 15 deploys is not mapping code. `Microsoft.IdentityModel.Tokens`,
`JsonWebTokens`, `Logging` and `Abstractions` come to 599.1 KB, and they are there because
AutoMapper 15 validates a signed licence key. Referencing it puts a JWT validation stack into your
dependency closure, larger than the mapper itself, to check that you are allowed to use the mapper.
The remaining 232.4 KB is `Microsoft.Extensions.*` for dependency injection, options and logging.

Mapsicle's core has none of that, and not by luck: the `core-has-no-dependencies` job packs
`src/Mapsicle` and fails if the nuspec declares a single dependency. Everything else in the
ecosystem is a separate opt-in package.

---

## Detailed Comparison: Mapsicle vs AutoMapper vs Mapperly

### Core Mapping Features

| Feature | Mapsicle | AutoMapper | Mapperly |
|---------|----------|------------|----------|
| Convention-based mapping | ✅ | ✅ | ✅ |
| Flattening (`Address.City` → `AddressCity`) | ✅ | ✅ | ✅ |
| Custom member mapping | ✅ `ForMember()` | ✅ `ForMember()` | ✅ `[MapProperty]` |
| Ignore members | ✅ `[IgnoreMap]` | ✅ `Ignore()` | ✅ `[MapperIgnore]` |
| Reverse mapping | ✅ `ReverseMap()` | ✅ `ReverseMap()` | ✅ (define both) |
| Before/After map hooks | ✅ | ✅ | ✅ |
| Type converters | ✅ `CreateConverter<>()` | ✅ `ConvertUsing()` | ✅ User methods |
| Inheritance/Polymorphism | ✅ `Include<>()` | ✅ `Include<>()` | ✅ |
| Nested object mapping | ✅ | ✅ | ✅ |
| Collection mapping | ✅ | ✅ | ✅ |
| Constructor mapping | ✅ `ConstructUsing()` | ✅ `ConstructUsing()` | ✅ (automatic) |

### Configuration & Organization

| Feature | Mapsicle | AutoMapper | Mapperly |
|---------|----------|------------|----------|
| Profile support | ✅ `MapsicleProfile` | ✅ `Profile` | ❌ (partial classes) |
| Fluent configuration | ✅ | ✅ | ❌ (attributes) |
| Attribute-based config | ✅ `[MapFrom]` | ✅ | ✅ |
| Static zero-config API | ✅ `obj.MapTo<T>()` | ❌ | ❌ |
| DI-friendly | ✅ `IMapper` | ✅ `IMapper` | ✅ |
| Assembly scanning | ✅ | ✅ | N/A |

### Extension Packages

| Package/Feature | Mapsicle | AutoMapper | Mapperly |
|-----------------|----------|------------|----------|
| **EF Core ProjectTo** | ✅ `Mapsicle.EntityFramework` | ✅ Built-in | ✅ (expressions) |
| **FluentValidation** | ✅ `Mapsicle.Validation` | ❌ | ❌ |
| **DataAnnotations** | ✅ `Mapsicle.DataAnnotations` | ❌ | ❌ |
| **JSON serialization** | ✅ `Mapsicle.Json` | ❌ | ❌ |
| **ASP.NET Core** | ✅ `Mapsicle.AspNetCore` | ❌ | ❌ |
| **Caching** | ✅ `Mapsicle.Caching` | ❌ | N/A |
| **Audit/Change tracking** | ✅ `Mapsicle.Audit` | ❌ | ❌ |
| **Naming conventions** | ✅ 5 conventions | ✅ Built-in | ✅ `NamingStrategy` |

### Naming Convention Support

| Convention | Mapsicle | AutoMapper | Mapperly |
|------------|----------|------------|----------|
| PascalCase | ✅ | ✅ | ✅ |
| camelCase | ✅ | ✅ | ✅ |
| snake_case | ✅ | ✅ | ✅ |
| kebab-case | ✅ | ❌ | ❌ |
| SCREAMING_SNAKE_CASE | ✅ | ❌ | ❌ |

### Performance Characteristics

Measured rather than described. One order aggregate, nine types, three levels of nesting, two
collections, mapped on an Apple M1 under .NET 8 Release, against the same projection written out by
hand. Reproduce it from
[github.com/arnelirobles/mapsicle_samples](https://github.com/arnelirobles/mapsicle_samples).

| Aspect | Mapsicle | AutoMapper | Mapperly |
|--------|----------|------------|----------|
| **Warm map, pair declared** | **288.4 ns (1.00x hand written)** | n/a | 321.5 ns (1.12x) |
| **Warm map, nothing declared** | 523.0 ns (1.77x) | 728.8 ns (2.45x) | n/a, it always generates |
| **Allocated per map** | **1.41 KB, equal to hand written** | 1.48 KB | 1.50 KB |
| **First map of a pair** | 2,480 ns declared, 367,138 ns not | high, it compiles too | **none, it is already code** |
| **Startup time impact** | none for declared pairs | medium | **none** |
| **AOT compatible** | declared pairs yes, undeclared no | no | **yes** |

Mapperly's 1.12 is one habit rather than anything structural: its collection helpers take
`IReadOnlyCollection<T>` where the member is a `List<T>`, so every `foreach` boxes the struct
enumerator. That is also why it is the only row above the hand written allocation. The section on
[compile-time mapping](#compile-time-mapping-and-how-it-compares-to-mapperly) shows both emitted
loops side by side.

### When to Use Each

| Scenario | Recommendation | Why |
|----------|----------------|-----|
| **Fastest warm mapping** | **Mapsicle**, pair declared | 1.00x hand written against Mapperly's 1.12x, and it allocates less |
| **The compiler must prove every pair maps** | **Mapperly** | A pair it cannot generate does not compile. Mapsicle warns and falls back, which is safer at run time and weaker as a guarantee |
| **AOT, and nothing may fall back to reflection** | **Mapperly** | Mapsicle's declared pairs are AOT clean, but an undeclared one compiles an expression tree at run time. Mapperly has no such path to leave open by accident |
| **AOT, and you will declare every pair** | **Mapsicle** or **Mapperly** | Both work. Check the build for `MSG001` if you pick Mapsicle |
| **An object graph with reference cycles** | **AutoMapper**, or **Mapsicle** | AutoMapper preserves the reference so the cycle survives. Mapsicle stops at a ceiling and returns something usable. Mapperly's default settings abort the process |
| **Quick prototyping, zero setup** | **Mapsicle** | No configuration of any kind, and you can add the generator later without touching a call site |
| **A large graph you do not want to declare** | **Mapsicle** | An undeclared pair still maps, at 1.77x hand written and still 1.4x faster than AutoMapper |
| **Need integrated validation** | **Mapsicle** | `Mapsicle.Validation`, no equivalent in either |
| **Existing AutoMapper codebase** | **AutoMapper** (if licensed) or migrate | |
| **Budget-conscious or OSS project** | **Mapsicle** or **Mapperly** | MPL-2.0 and MIT respectively |
| **Complex mapping configurations** | **AutoMapper** or **Mapsicle** (fluent) | |
| **ASP.NET Core Minimal APIs** | **Mapsicle** (AspNetCore package) | |
| **Need audit trail of changes** | **Mapsicle** (Audit package) | |

Two of those rows go to Mapperly on purpose. Its guarantee is stronger than Mapsicle's precisely
because it has no fallback: if it cannot emit a mapper you find out at compile time, every time.
Mapsicle trades that for a mapper that always works, and the cost of the trade is that a `MSG001` you
did not read is a pair running 1.77x instead of 1.00x.

### Code Comparison

**Mapsicle (Static - Zero Config)**
```csharp
var dto = user.MapTo<UserDto>();
```

**Mapsicle (Fluent)**
```csharp
var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
var mapper = config.CreateMapper();
var dto = mapper.Map<UserDto>(user);
```

**AutoMapper**
```csharp
var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
var mapper = config.CreateMapper();
var dto = mapper.Map<UserDto>(user);
```

**Mapperly**
```csharp
[Mapper]
public partial class UserMapper
{
    public partial UserDto ToDto(User user);
}
// Usage
var dto = new UserMapper().ToDto(user);
```

### Unique Mapsicle Features

Features not found in AutoMapper or Mapperly:

1. **Static zero-config API**: `user.MapTo<UserDto>()` - no setup required
2. **Built-in validation integration**: Map + validate in one call with FluentValidation or DataAnnotations
3. **Audit/diff tracking**: Track what changed during mapping with `MapWithAudit<T>()`
4. **Caching integration**: Cache mapped results with `IMemoryCache`/`IDistributedCache`
5. **ASP.NET Core IResult helpers**: `MapValidateAndReturn<T, TValidator>()`
6. **JSON map-and-serialize**: `MapToJson<T>()`, `MapFromJson<T>()`
7. **LRU cache option**: Memory-bounded cache for long-running applications

---

## Quick Start

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

**Requirements:** .NET Standard 2.0+, .NET 8.0 or .NET 10.0 (`Mapsicle.AspNetCore` and `Mapsicle.EntityFramework` require .NET 8.0+)
**Installation:** `dotnet add package Mapsicle`

### Which Package Do I Need?

```
Do you need EF Core query translation (ProjectTo)?
├─ YES → Install: Mapsicle + Mapsicle.Fluent + Mapsicle.EntityFramework
└─ NO
   ├─ Do you need post-mapping validation?
   │  └─ YES → Install: Mapsicle + Mapsicle.Fluent + Mapsicle.Validation
   ├─ Do you need naming convention support (snake_case ↔ PascalCase)?
   │  └─ YES → Install: Mapsicle + Mapsicle.Fluent + Mapsicle.NamingConventions
   ├─ Do you need custom mapping logic (ForMember, hooks)?
   │  └─ YES → Install: Mapsicle + Mapsicle.Fluent
   └─ NO → Install: Mapsicle (core only - zero config)
```

| Scenario | Packages Needed |
|:---------|:----------------|
| Simple POCO mapping | `Mapsicle` |
| API DTOs with transformations | `Mapsicle.Fluent` |
| EF Core with SQL projection | `Mapsicle.EntityFramework` |
| Map + validate DTOs | `Mapsicle.Validation` |
| snake_case ↔ PascalCase mapping | `Mapsicle.NamingConventions` |

---

## Benchmark Results

### Core Mapping Performance

BenchmarkDotNet 0.13.12, .NET 8, five warmup iterations and twenty measured, on two architectures
because one is not evidence of anything portable. Reproduce with
`dotnet run -c Release --project tests/Mapsicle.Benchmarks -- --core`, which is the job the CI gate
runs.

The arm64 figures are the **median of six runs** on an idle 4-core Ampere VM. BenchmarkDotNet's
error column describes how consistent the iterations were inside one process, which is not the same
as whether the run reproduces. On that machine it does not, closely: repeating the identical commit
moved the Mapperly collection row by 36 percent, and Mapperly is a source generator this project
has never touched. A single run of any of these rows is one sample.

**Single object, five properties:**

| Runtime | Manual  | Mapsicle    | AutoMapper | Mapperly | Mapsicle vs AutoMapper |
| :------ | ------: | ----------: | ---------: | -------: | :--------------------- |
| x64 Linux (CI runner)   | 18.6 ns | **57.8 ns** | 83.2 ns | 18.8 ns | 1.44x faster |
| arm64 Linux (Ampere VM) | 23.0 ns | **101.2 ns** | 131.3 ns | 28.2 ns | 1.30x faster |

**Mapperly is not a competitor, it is a different trade, and it wins the one this table measures.**
At 18.2 ns against hand-written code's 18.3 ns it is not close to manual, it is indistinguishable
from it, because a source generator emits ordinary C# assignments at compile time and leaves no
delegate, no cache lookup and no indirection at runtime. Mapsicle and AutoMapper both build an
expression tree, compile it, cache it, look it up and invoke through it. That apparatus is the
entire 2.5x to 3x gap, and no runtime mapper can close it, because the apparatus is what makes it
a runtime mapper.

What you buy with it: Mapperly needs a `partial class` with a `[Mapper]` attribute and a declared
method for every pair, all known at compile time. It cannot map a `Dictionary<string, object>` into
a type chosen at runtime, or a collection whose items turn out to have different runtime types,
because there is nothing for it to generate against. Mapsicle needs no configuration and resolves
types as it meets them.

**If your mappings are all known at compile time and you are willing to declare them, choose
Mapperly.** Mapsicle is for the case where they are not, and its comparison is with AutoMapper.

The Mapperly column is here for scale, not as a fair fight. It is a source generator measured
against two runtime mappers, and it wins on throughput by construction. The comparison becomes
like for like when 2.2.0 lands its own generator as an opt-in lane, which is described under
Unreleased in [CHANGELOG.md](CHANGELOG.md). Read the AutoMapper column as the competitive one and
the Mapperly column as the ceiling.

**Other scenarios:**

| Scenario                        |    Mapsicle | AutoMapper |  Mapperly | vs AutoMapper |
| :------------------------------ | ----------: | ---------: | --------: | :------------ |
| **Flattening**, x64             |  **64.9 ns** |   101.2 ns |   21.1 ns | 1.56x faster  |
| **Flattening**, arm64           | **110.0 ns** |   140.2 ns |   33.0 ns | 1.28x faster  |
| **Collection (100)**, x64       | **2,175 ns** |  2,618 ns  |  1,933 ns | 1.20x faster  |
| **Collection (100)**, arm64     | **4,311 ns** |  4,696 ns  |  2,894 ns | 1.09x faster  |
| **Collection (10,000)**, arm64  | **481 us**   |  1,134 us  |    322 us | 2.36x faster  |
| **Deep nesting (15 levels)**, arm64 | **626 ns** | 5,145 ns  |    282 ns | 8.22x faster  |

Allocation per operation matches hand-written code for single objects and flattening (48 B and
56 B, the destination and nothing else). On a collection Mapsicle allocates 5,656 B against
AutoMapper's 6,992 B, about 19 percent less, and the same as source-generated Mapperly.

**About the collection rows.** A `List<T>` is mapped by a loop compiled for its element type, which
is where most of that number comes from. Before that loop existed these rows were 1.07x slower on
x64 and 1.04x faster on arm64, which is to say parity. Arrays, and lists whose element type is
`object`, an interface or abstract, keep the older loop and its older cost, because a loop compiled
for a type no element actually is sends every element down a slower path. If collection throughput
at this size is what your workload is bounded by, Mapperly is still faster than both, though at
2,175 ns against its 1,933 the gap on x64 is now about 12 percent.

At ten thousand the picture changes, and not because the per-element cost changed. AutoMapper
allocates 742 KB there against Mapsicle's 560 KB, enough to reach generation 2 collections while
Mapsicle stays in 0 and 1. The 2.36x is mostly that.

Three things worth more than the table:

- **The job length changes the answer.** These numbers come from five warmup iterations and twenty
  measured. The same commits under BenchmarkDotNet's ShortRun, three and three, put the arm64
  collection row at 1.05x slower rather than 1.05x faster, because three warmup iterations do not
  get compiled delegates to steady state and the mapper that compiles more pays for it. On a hosted
  runner ShortRun gave an interval of plus or minus 43 percent of the mean. The gate used to run
  that job. It does not now.
- **`Mapsicle.Fluent` is not on this table.** A complex object through the fluent configuration API
  is about 1.03x AutoMapper, and a hundred of them about 1.22x, because the fluent path maps a
  collection element by element rather than through the compiled loop. The static API is what the
  rows above measure.
- An earlier version of this table claimed 2.1x on single objects and rough parity on collections.
  Neither held when the benchmark was re-run. It went unnoticed because CI measured the comparison,
  printed it and exited zero regardless. CI now fails when the comparison changes direction, and it
  reads BenchmarkDotNet's own summary rather than a stopwatch loop, so the published numbers and
  the gated numbers come from one source.

### Edge Case Performance

| Scenario                     | Mapsicle      | AutoMapper    | Mapperly      | Notes                     |
| :--------------------------- | :------------ | :------------ | :------------ | :------------------------ |
| **Deep Nesting (15 levels)** | **626 ns**    | 5,145 ns      | 282 ns        | All safe; Mapsicle 8.22x AutoMapper |
| **Circular References**      | Handled by default | Opt in via `PreserveReferences()` | Opt in via `UseReferenceHandling` | Only Mapsicle needs no configuration |
| **Large Collection (10K)**   | **0.48 ms**   | 1.13 ms       | 0.32 ms       | Mapsicle 2.36x AutoMapper |
| **Parallel (1000 threads)**  | ✅ Thread-safe | ✅ Thread-safe | ✅ Thread-safe | All thread-safe           |
| **Cold Start**               | Medium        | Slow          | **None**      | Mapperly pre-compiled     |

### Performance Optimizations (v1.1+)

| Optimization                       | Improvement                       | Status |
| :--------------------------------- | :-------------------------------- | :----- |
| **TypedMapperCache&lt;T,D&gt;**    | Zero-allocation generic cache     | ✅ NEW |
| **MapTo&lt;TSource,TDest&gt;()**   | Strongly-typed mapping, no boxing | ✅ NEW |
| **Skip depth tracking for simple** | No overhead for flat types        | ✅ NEW |
| **Lock-free cache reads**          | Eliminates contention             | ✅      |
| **Collection mapper caching**      | +20% for collections (v1.1)       | ✅      |
| **PropertyInfo caching**           | +15% faster cold starts           | ✅      |
| **Primitive fast path**            | Skips depth tracking              | ✅      |
| **Cached compiled actions**        | No runtime reflection             | ✅      |
| **LRU cache option**               | Memory-bounded in long-run apps   | ✅      |
| **Collection pre-allocation**      | Capacity hints for known sizes    | ✅      |

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

### The claims are gated

The comparison above is not only published, it is checked. `dotnet run -c Release --project
tests/Mapsicle.Benchmarks -- --quick` runs Mapsicle against AutoMapper and returns a non-zero exit
code when a ratio moves outside its bound. CI runs it on every pull request.

Two more gates guard the other half of the pitch:

- `core-has-no-dependencies` packs `Mapsicle` and fails if the nuspec declares a single dependency.
- `licence-boundary` fails if anything under `src/` references AutoMapper, which is RPL-1.5 or a
  paid licence. It is compared against in `tests/`, which is never packed.

### Run Benchmarks Yourself

```bash
cd tests/Mapsicle.Benchmarks
dotnet run -c Release              # Full suite
dotnet run -c Release -- --quick   # Smoke test
dotnet run -c Release -- --edge    # Edge cases only
```

---

## Compile-time mapping, and how it compares to Mapperly

`Mapsicle.SourceGen` generates a mapper at build time for pairs you declare. It is opt in per pair,
and everything you do not declare keeps mapping through the runtime engine exactly as before.

```csharp
[assembly: MapsicleGenerate(typeof(Order), typeof(OrderDto))]

// unchanged at the call site, now bound at compile time
var dto = order.MapTo<OrderDto>();
```

That is the whole setup. One line per pair, anywhere in the assembly.

A working repository that maps one e-commerce order aggregate through Mapsicle, AutoMapper and
Mapperly side by side, with a CRUD API over SQLite and the benchmarks below:
**[github.com/arnelirobles/mapsicle_samples](https://github.com/arnelirobles/mapsicle_samples)**

### How to actually get the fast path

Five things, and the build tells you when you have missed one.

**1. Declare the pairs that are hot.** One line each, anywhere in the assembly. You do not need to
declare everything: an undeclared pair still maps, it just pays the engine.

```csharp
[assembly: MapsicleGenerate(typeof(Order), typeof(OrderDto))]
```

**2. Declare it in the assembly that calls it.** The generated extension is `internal` to the
assembly holding the attribute, so declaring `Order` into `OrderDto` in your domain project does not
bind a call site in your API project. Put the attribute in both.

This one fails silently and only costs you part of the win. The module initializer registers the
generated delegate process-wide, so a call site in another assembly still runs generated code; it
just reaches it through the dictionary lookup instead of the compiler. That is 309.7 ns against
288.4, not the 523.0 an undeclared pair pays.

**3. Call it on a typed variable, not `object`.** The binding is the compiler choosing a more specific
extension, so it needs to see the type.

```csharp
var dto = order.MapTo<OrderDto>();              // 288 ns, bound at compile time
var dto = ((object)order).MapTo<OrderDto>();    // 310 ns, back to a dictionary lookup
```

Mapping a **collection** of a declared pair is the exception worth knowing. `orders.MapTo<OrderDto>()`
on a `List<Order>` binds to the collection overload, not to the per element extension, so it does not
get the compile-time binding. It does still run the generated mapper: the compiled list loop stands
aside for a declared pair and invokes the generated delegate per item, because inlining what the
expression builder would have produced would quietly ignore the generated mapper. That is the slower
loop and the faster mapper. Declaring the pair is still worth it; it just wins less on collections
than on single objects.

**4. Type collection members as `List<T>` or `T[]`.** The emitted loop indexes the source, which is
where most of the margin over Mapperly comes from. A member declared `IEnumerable<T>` cannot be
indexed, so the pair is refused and stays on the engine.

**5. Read the build output for `MSG001`.** That warning is the generator telling you a pair you asked
for fell back:

```
warning MSG001: Cannot generate a mapper from 'Shop.WithEnumerable' to 'Shop.WithEnumerableDto':
'Items' converts System.Collections.Generic.IEnumerable<Shop.Item> into
System.Collections.Generic.List<Shop.ItemDto>, which the engine performs and this generator
has no emitted rule for. The pair still maps through the runtime engine.
```

Nothing is broken when you see it. It is the difference between 1.00x and 1.77x on that pair, and it
names the member responsible.

**To check it worked**, turn on the emitted files and read them:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<!-- Required. The files land inside the project directory, so the SDK globs them back in as
     ordinary source and every generated type ends up defined twice. -->
<ItemGroup>
  <Compile Remove="generated/**" />
</ItemGroup>
```

A declared pair appears as a `MapTo` extension in your source type's namespace. If it is not there,
the pair was refused and `MSG001` says why.

### The numbers

`Order` into `OrderDto`: nine types, three levels of nesting, two collections, a widening, an enum
into a string, an enum into a different enum, a `DateTime` into a `DateTimeOffset` and two flattened
paths. Measured on an Apple M1, .NET 8, Release, against the same projection written out by hand,
which is the only baseline worth having.

| whole graph | mean | vs hand written | allocated |
| :---------- | ---: | --------------: | --------: |
| hand written | 288.1 ns | 1.00 | 1.41 KB |
| **Mapsicle, declared pair** | **288.4 ns** | **1.00** | **1.41 KB (1.00)** |
| Mapsicle, declared, reached through an untyped call | 309.7 ns | 1.07 | 1.41 KB (1.00) |
| Mapperly 4.1.1 | 321.5 ns | 1.12 | 1.50 KB (1.07) |
| Mapsicle, undeclared, through the engine | 523.0 ns | 1.77 | 1.41 KB (1.00) |
| AutoMapper 15.1.3 | 728.8 ns | 2.45 | 1.48 KB (1.06) |

Generated code is level with hand written: 0.3 ns apart on a 288 ns call, standard deviations near
3 ns, and the same 1.41 KB. Declaring the pair is what moves it from 1.77 to 1.00.

The third row is the same generated method reached a different way. `((object)order).MapTo<OrderDto>()`
pays a `GetType`, a dictionary probe for the delegate, a second probe to decide on depth tracking and
a cast before it runs. That 21 ns is what the compile-time binding removes.

Cold start is the larger and less obvious win:

| first map of a pair | |
| :------------------ | -----: |
| declared             | 2,480 ns |
| undeclared           | 367,138 ns |

That 148x is the `Expression.Compile` a declared pair never pays. It is what short-lived processes
hit, and it is why NativeAOT works for declared pairs: nothing generates IL at runtime.

### What you write, side by side

Mapperly needs a partial class, an attribute, and a declared method per mapping, and the call site
becomes the class you declared:

```csharp
[Mapper]
public partial class OrderMapper
{
    public partial OrderDto Map(Order source);
}

var mapper = new OrderMapper();
var dto = mapper.Map(order);
```

Mapsicle needs a declaration and no call-site change:

```csharp
[assembly: MapsicleGenerate(typeof(Order), typeof(OrderDto))]

var dto = order.MapTo<OrderDto>();
var dtos = orders.MapTo<OrderDto>();
```

The difference that matters is not the line count. Mapperly's method **is** the mapping, so a pair it
cannot generate does not exist and code calling it does not compile. Mapsicle's declaration is a
request: a refused pair reports `MSG001` and still maps through the engine, and a type you never
declared still maps with no declaration at all.

|  | setup | a pair you forget | a member it cannot emit |
| :--- | :--- | :--- | :--- |
| **Mapsicle** | none, or one line to bind it | nothing to forget | `MSG001` warning, engine handles it, build continues |
| **Mapperly** | a partial method per mapping | `RMG020` warning | compile error |
| **AutoMapper** | a `CreateMap` per pair in the graph | member is silently empty at run time | silently empty |

### What comes out, side by side

Both emit a method per type with direct calls, and they are nearly identical. The divergence is the
collection loop, and it is the whole of the 12 percent above.

Mapperly widens the parameter to an interface:

```csharp
private List<OrderLineDto> MapToListOfOrderLineDto(
    IReadOnlyCollection<OrderLine> source)      // widened from List<T>
{
    var target = new List<OrderLineDto>(source.Count);
    foreach (var item in source)                // boxes List<T>'s struct enumerator
        target.Add(MapToOrderLineDto(item));
    return target;
}
```

Mapsicle keeps the concrete type and indexes it:

```csharp
internal static List<OrderLineDto> P0_List4(List<OrderLine>? source)
{
    if (source is null) return new List<OrderLineDto>();

    var target = new List<OrderLineDto>(source.Count);
    for (var i = 0; i < source.Count; i++)
    {
        var item = source[i];
        target.Add(P0_Object5(item)!);
    }
    return target;
}
```

`foreach` over `IReadOnlyCollection<T>` calls `IEnumerable<T>.GetEnumerator()`, which boxes
`List<T>`'s struct enumerator on the heap and then dispatches every `MoveNext` and `Current` through
an interface. Measured in isolation on four collections holding five items, that costs **38.5 ns and
120 bytes** against the indexed form. The whole-graph gap between Mapperly and hand written is 33.4 ns
and about 90 bytes. It is the same thing, and it is the only column where Mapperly sits above the
baseline.

None of that is a criticism of Mapperly, which is an excellent library and was 12 percent from the
speed limit before anyone went looking for the reason.

### Where the two behave differently

|  | Mapsicle | Mapperly 4.1.1 |
| :--- | :--- | :--- |
| enum into a different enum | matched by **name** | matched by **value**, and will return a number the destination declares no member for |
| a reference cycle | engine stops at a depth ceiling and returns | follows it until the stack ends, aborting the process |
| a pair it cannot handle | maps through the engine | does not exist |
| a source member nothing maps | silent | `RMG020` |

### What the generator emits

Numeric widening, enum into a string, enum into a different enum by name, `DateTime` into
`DateTimeOffset`, nullable lifting, nested objects, `List<T>` and array collections, and flattened
paths up to four levels deep.

Three things are refused on purpose, and each is a refusal rather than a gap:

- **A cyclic graph.** Generated code has no depth ceiling and the engine has one, so emitting a mapper
  that follows a cycle would produce a lane that aborts the process where the other returns.
- **A destination member with a non-public setter.** Reflection can write one and generated code
  cannot, so emitting the pair would silently return less than the engine does.
- **Anything into a string that is not an enum.** The engine formats through
  `CultureInfo.InvariantCulture`, and re-deriving that in the emitter is how two implementations of
  one rule start disagreeing.

A refusal reports `MSG001`, the build carries on, and the call site does not change.

Two things to know before using it on your own code. The generated extension is **internal to the
assembly that declares the pair**, so declaring it in one project does nothing for another. And if
you turn on `EmitCompilerGeneratedFiles`, exclude the output folder from compilation: it lands inside
the project directory, the SDK globs it back in, and every generated type is defined twice.

### Why both lanes are proven to agree

The generator is a second implementation of the conversion rules, which is the one thing
[CONTRIBUTING.md](CONTRIBUTING.md) says must exist once. That exception is paid for with a
conformance suite: one table of cases run through the runtime engine and the generated code,
asserting identical output member by member.

It also asserts every declared pair was actually generated, which is the assertion the suite is worth
having for. A refused pair falls back to the runtime engine, which is the lane the comparison uses,
so both sides agree and every test passes. Making the generator's name matching case sensitive, so it
matched nothing, left the entire suite green until that check existed. The same trap caught widening,
nesting, collections and flattening: all four had passing tests before any of them was emitted.

The band is gated too, by two instruments:

```bash
dotnet run -c Release --project tests/Mapsicle.Benchmarks -- --band
```

CI checks allocation exactly, because generated code allocating more than hand written is how the
collection regression shows up and bytes are deterministic where a shared runner's clock is not.
Reintroducing the interface loop above fails it with the cause named. The 1.00 plus or minus 0.05
band is checked under `--band` on an idle machine, because the `claims` job runs on `ubuntu-latest`
where this repository has measured a 99.9 percent interval of plus or minus 43 percent of the mean,
and a five percent bound on a forty percent instrument fails for reasons that have nothing to do with
the emitter.

---

## Installation

```bash
# Core package - zero config
dotnet add package Mapsicle

# Fluent configuration + Profiles (optional)
dotnet add package Mapsicle.Fluent

# EF Core ProjectTo (optional)
dotnet add package Mapsicle.EntityFramework

# FluentValidation integration (optional)
dotnet add package Mapsicle.Validation

# Naming conventions support (optional)
dotnet add package Mapsicle.NamingConventions

# Serilog structured logging (optional)
dotnet add package Mapsicle.Serilog

# Dapper integration (optional)
dotnet add package Mapsicle.Dapper

# JSON serialization (optional)
dotnet add package Mapsicle.Json

# ASP.NET Core Minimal API helpers (optional)
dotnet add package Mapsicle.AspNetCore

# Memory/Distributed caching (optional)
dotnet add package Mapsicle.Caching

# Change tracking/audit (optional)
dotnet add package Mapsicle.Audit

# DataAnnotations validation (optional)
dotnet add package Mapsicle.DataAnnotations
```

---

## Package 1: Mapsicle (Core)

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

## Package 2: Mapsicle.Fluent

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

## Package 3: Mapsicle.EntityFramework

**`ProjectTo<T>()`** that translates to SQL, no in-memory loading.

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

## Package 4: Mapsicle.Validation

**Post-mapping validation** using FluentValidation, validate DTOs immediately after mapping.

### Basic Usage

```csharp
using FluentValidation;
using Mapsicle.Fluent;
using Mapsicle.Validation;

// 1. Define your validator
public class UserDtoValidator : AbstractValidator<UserDto>
{
    public UserDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Age).GreaterThan(0).WithMessage("Age must be positive");
    }
}

// 2. Map and validate in one call
var result = mapper.MapAndValidate<User, UserDto, UserDtoValidator>(user);

if (result.IsValid)
{
    return Ok(result.Value);  // The mapped DTO
}
else
{
    return BadRequest(result.ErrorsByProperty);  // { "Email": ["Valid email is required"] }
}
```

### API Overview

```csharp
// Map and validate with validator type
var result = mapper.MapAndValidate<TSource, TDest, TValidator>(source);

// Map and validate with validator instance
var validator = new UserDtoValidator();
var result = mapper.MapAndValidate<UserDto>(source, validator);

// Validate an existing object
var result = dto.Validate<UserDto, UserDtoValidator>();

// Get value or throw exception
var dto = result.GetValueOrThrow();  // Throws ValidationException if invalid
```

### Result Properties

```csharp
result.IsValid           // bool - true if validation passed
result.Value             // TDest - the mapped object
result.Errors            // IList<ValidationFailure> - all validation errors
result.ErrorsByProperty  // IDictionary<string, string[]> - errors grouped by property
result.ValidationResult  // FluentValidation.Results.ValidationResult - full result
```

### Real-World Example: API Controller

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;
    private readonly IUserRepository _repo;

    public UsersController(IMapper mapper, IUserRepository repo)
    {
        _mapper = mapper;
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var result = _mapper.MapAndValidate<CreateUserRequest, UserDto, UserDtoValidator>(request);

        if (!result.IsValid)
        {
            return BadRequest(new { errors = result.ErrorsByProperty });
        }

        var user = await _repo.CreateAsync(result.Value);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }
}
```

---

## Package 5: Mapsicle.NamingConventions

**Automatic naming convention conversion**, map between `snake_case`, `PascalCase`, `camelCase`, and `kebab-case`!

### Basic Usage

```csharp
using Mapsicle.NamingConventions;

// Source uses snake_case (e.g., from Python API or database)
public class ApiResponse
{
    public int user_id { get; set; }
    public string first_name { get; set; }
    public string email_address { get; set; }
}

// Destination uses PascalCase (C# convention)
public class UserDto
{
    public int UserId { get; set; }
    public string FirstName { get; set; }
    public string EmailAddress { get; set; }
}

// Map with naming convention conversion
var dto = apiResponse.MapWithConvention<ApiResponse, UserDto>(
    NamingConvention.SnakeCase,
    NamingConvention.PascalCase);

// dto.UserId == apiResponse.user_id
// dto.FirstName == apiResponse.first_name
```

### Built-in Conventions

| Convention | Example | C# Property |
|:-----------|:--------|:------------|
| `NamingConvention.PascalCase` | `UserName` | Standard C# |
| `NamingConvention.CamelCase` | `userName` | JavaScript/JSON |
| `NamingConvention.SnakeCase` | `user_name` | Python/Ruby/SQL |
| `NamingConvention.KebabCase` | `user-name` | URLs/CSS |

### Convert Property Names

```csharp
// Convert a single name
var snake = "UserName".ConvertName(NamingConvention.PascalCase, NamingConvention.SnakeCase);
// Result: "user_name"

var pascal = "first_name".ConvertName(NamingConvention.SnakeCase, NamingConvention.PascalCase);
// Result: "FirstName"

var camel = "OrderCount".ConvertName(NamingConvention.PascalCase, NamingConvention.CamelCase);
// Result: "orderCount"
```

### Use with Fluent Mapper

```csharp
// Combine with IMapper for convention-based mapping
var dto = mapper.MapWithConvention<ApiResponse, UserDto>(
    apiResponse,
    NamingConvention.SnakeCase,
    NamingConvention.PascalCase);
```

### Check Name Matching

```csharp
// Check if names match across conventions
bool match = NamingConvention.NamesMatch(
    "user_name", NamingConvention.SnakeCase,
    "UserName", NamingConvention.PascalCase);
// Result: true
```

### Real-World Example: External API Integration

```csharp
public class ExternalApiClient
{
    private readonly HttpClient _http;

    public async Task<UserDto> GetUserAsync(int id)
    {
        // External API returns snake_case JSON
        var response = await _http.GetFromJsonAsync<ExternalUserResponse>($"/users/{id}");

        // Convert to C# conventions
        return response.MapWithConvention<ExternalUserResponse, UserDto>(
            NamingConvention.SnakeCase,
            NamingConvention.PascalCase);
    }
}

// External API response (snake_case)
public class ExternalUserResponse
{
    public int user_id { get; set; }
    public string first_name { get; set; }
    public string last_name { get; set; }
    public string email_address { get; set; }
    public DateTime created_at { get; set; }
}

// Internal DTO (PascalCase)
public class UserDto
{
    public int UserId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string EmailAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## Package 6: Mapsicle.Serilog

**Structured logging integration** for enterprise diagnostics and observability.

### Basic Setup

```csharp
using Mapsicle.Serilog;
using Serilog;

// Configure Serilog logger
var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateLogger();

// Enable Mapsicle logging
MapsicleLogging.UseSerilog(logger);
```

### Map with Logging

```csharp
// Log individual mappings
var dto = user.MapWithLogging<User, UserDto>(logger);
// Output: [INF] Mapsicle: Mapped User -> UserDto in 0.5ms

// Log collection mappings
var dtos = users.MapCollectionWithLogging<User, UserDto>(logger);
// Output: [INF] Mapsicle: Mapped 100 User -> UserDto items in 5.2ms
```

### Slow Mapping Warnings

```csharp
// Configure slow mapping threshold (default: 100ms)
MapsicleLogging.SlowMappingThreshold = TimeSpan.FromMilliseconds(50);

// Slow mappings automatically log warnings
var dto = largeObject.MapWithLogging<Large, LargeDto>(logger);
// Output: [WRN] Mapsicle: Slow mapping detected Large -> LargeDto took 75ms
```

### Scoped Logging for Batch Operations

```csharp
using (var scope = new MappingLoggingScope(logger, "OrderProcessing"))
{
    // All mappings in this scope are logged with the operation context
    var orderDto = order.MapWithLogging<Order, OrderDto>(logger);
    var itemDtos = items.MapCollectionWithLogging<Item, ItemDto>(logger);
}
// Output includes: OperationName = "OrderProcessing"
```

---

## Package 7: Mapsicle.Dapper

**Dapper integration** for mapping database query results directly to DTOs.

### Basic Usage

```csharp
using Mapsicle.Dapper;
using Dapper;

// Query and map in one call
var users = connection.QueryAndMap<User, UserDto>("SELECT * FROM Users").ToList();

// With parameters
var user = connection.QuerySingleAndMap<User, UserDto>(
    "SELECT * FROM Users WHERE Id = @Id",
    param: new { Id = 1 });
```

### Async Support

```csharp
// Async query and map
var users = await connection.QueryAndMapAsync<User, UserDto>("SELECT * FROM Users");

// Async single result
var user = await connection.QuerySingleAndMapAsync<User, UserDto>(
    "SELECT * FROM Users WHERE Id = @Id",
    param: new { Id = 1 });
```

### With Custom Configuration

```csharp
// Use a custom mapper configuration
var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<User, UserSummaryDto>()
        .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
});

var users = connection.QueryAndMap<User, UserSummaryDto>(
    "SELECT * FROM Users", config).ToList();

// Or use IMapper instance
var mapper = config.CreateMapper();
var users = connection.QueryAndMap<User, UserSummaryDto>(
    "SELECT * FROM Users", mapper).ToList();
```

### Transaction Support

```csharp
using var transaction = connection.BeginTransaction();

// Mappings work within transactions
var users = connection.QueryAndMap<User, UserDto>(
    "SELECT * FROM Users WHERE Active = 1",
    transaction: transaction).ToList();

transaction.Commit();
```

### Map Existing Dapper Results

```csharp
// Map existing IEnumerable from Dapper
var users = connection.Query<User>("SELECT * FROM Users");
var dtos = users.MapTo<User, UserDto>(mapper);
```

---

## Migration from AutoMapper

### API Compatibility

| AutoMapper                 | Mapsicle                              |
| :------------------------- | :------------------------------------ |
| `CreateMap<S,D>()`         | Same.                                 |
| `ForMember().MapFrom()`    | Same.                                 |
| `.Ignore()`                | Same.                                 |
| `BeforeMap/AfterMap`       | Same.                                 |
| `Include<Derived>()`       | Same.                                 |
| `ConstructUsing()`         | Same.                                 |
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
- `IMemberValueResolver` interface - use `ResolveUsing(func)` instead
- `ITypeConverter` interface - use `CreateConverter<T, U>()` instead
- Conditional mapping with complex predicates
- MaxDepth per individual mapping (only global `Mapper.MaxDepth`)

✅ **Now Supported (via extension packages):**
- Custom naming conventions → `Mapsicle.NamingConventions`
- Post-mapping validation → `Mapsicle.Validation`

⚠️ **Behavioral Differences:**
- **Circular references**: Mapsicle returns the default at `MaxDepth` with no configuration. AutoMapper needs `PreserveReferences()` or `MaxDepth(...)` to handle them.
- **Unmapped properties**: Both ignore, but Mapsicle has `GetUnmappedProperties<T, U>()` for validation
- **Null handling**: Both return null for null source, but Mapsicle is more aggressive with null-safe navigation

---

## Troubleshooting

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

## Mapping Untrusted Input

A mapper copies every matching property it can. Pointed at a request body it will set anything
whose name lines up, including a property the caller had no business setting. This is true of
every convention-based mapper, Mapsicle included, and it is worth stating plainly rather than
leaving for you to discover:

```csharp
// An attacker controls the keys.
var body = new Dictionary<string, object?>
{
    ["Email"] = "user@example.com",
    ["IsAdmin"] = true,          // not a field the caller should decide
};

var account = body.MapTo<Account>();   // account.IsAdmin is now true
```

**Map untrusted input into a DTO that holds only the fields a caller may set**, then map that
into your entity:

```csharp
public class AccountUpdateDto        // no IsAdmin, no Balance
{
    public string Email { get; set; } = "";
}

var dto = body.MapTo<AccountUpdateDto>();
dto.Map(existingAccount);            // reaches nothing the DTO does not declare
```

Where a shared type is unavoidable, `[IgnoreMap]` is an enforceable control and is honoured on
every entry point, including the dictionary path.

What Mapsicle does guarantee about untrusted values:

- **Values are copied, never interpreted.** Nothing in a string is parsed, executed or sanitised.
  A value containing SQL, script or format-string syntax arrives byte for byte.
- **A value of the wrong type is dropped**, not coerced and not thrown. A caller cannot use a type
  mismatch to crash a request handler or to smuggle a value through a loose conversion.
  Before 2.0.0 this was true of the object entry point and false of the dictionary one, which ran
  `Convert.ChangeType` on anything `IConvertible`. Both now behave the same. If you need the old
  parsing (a form post arrives as strings, and that is a legitimate reason to want it), set
  `Mapper.CoerceDictionaryValues = true` and it parses with the invariant culture. Lossless widening,
  enum and nullable conversions are unaffected and apply either way.
- **Unknown keys are ignored** rather than throwing.
- **Conversions do not depend on where the process runs.** Numbers and dates format with the
  invariant culture, so the same input produces the same output in every region.

One thing this list deliberately does not claim is a deep copy. A destination member that can hold
the source instance as it is receives that instance, on every entry point, so mutating the source
afterwards reaches into the destination. That is the same choice AutoMapper makes and it is what
keeps mapping allocation-free beyond the destination object, but if you map onto a long-lived
domain entity it is worth knowing. `DataIntegrityTests` pins it.

All of this is covered by `UntrustedInputTests` and `DataIntegrityTests` in `tests/Mapsicle.Tests`.
A failure there means one of these statements stopped being true.

## Known Limitations

### Feature Limitations

❌ **Not Supported:**
- Async mapping operations
- Source/destination value injection (context passing)
- Open generic types
- Explicit type conversion configuration beyond built-ins

✅ **Supported via Extension Packages:**
- Custom naming conventions (PascalCase ↔ snake_case) → `Mapsicle.NamingConventions`
- Post-mapping validation → `Mapsicle.Validation`

⚠️ **Partial Support:**
- Nested flattening limited to 1 level (`Address.City` ✅, `Address.Street.Line1` ❌)
- Collection mapping is slower than AutoMapper: 2,428 ns against 1,823 ns for 100 items, while allocating about 19 percent less. Competitive again at 10K+.
- EF Core ProjectTo works with `ForMember` expressions, but not `ResolveUsing` delegates

### Behavioral Differences from AutoMapper

- **Circular references**: Returns default value instead of throwing exception
- **Null safety**: More aggressive null-safe navigation (fewer NullReferenceException). A null
  reference-typed source mapped to a `string` destination yields `null` rather than throwing.
- **`Map(destination)` is not atomic**: it writes properties in order, so a setter that throws
  part-way leaves the earlier ones already written. Map into a fresh instance if you need
  all-or-nothing.
- **Exceptions from your accessors propagate unwrapped**: a getter throwing
  `InvalidOperationException` surfaces as `InvalidOperationException`, not wrapped in
  `TargetInvocationException`.
- **Unmapped properties**: Silent (use `GetUnmappedProperties` for validation)
- **Cache behavior**: Default is unbounded (must opt-in to LRU)

### Platform Support

| .NET Version | Mapsicle Support |
|:-------------|:-----------------|
| .NET 10.0 | ✅ Fully supported, tested on every build |
| .NET 8.0 | ✅ Fully supported, tested on every build (end of life 10 Nov 2026) |
| .NET 6.0-7.0 | ✅ Via .NET Standard 2.0 |
| .NET 5.0 | ✅ Via .NET Standard 2.0 |
| .NET Core 2.0+ | ✅ Via .NET Standard 2.0 |
| .NET Framework 4.6.1+ | ✅ Via .NET Standard 2.0 |

The netstandard2.0 assemblies are exercised by their own test project, which forces that
asset rather than the net8.0 one and asserts it really loaded it. Before 2.0.0 they were
built, packed and published without a test ever loading them.

---

## API Reference

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

### Validation Extensions (`using Mapsicle.Validation`)

#### `MapAndValidate<TDest, TValidator>(this IMapper mapper, object? source)`

Maps source to destination and validates using the specified validator type.

**Type Parameters:**
- `TDest` - Destination type
- `TValidator` - FluentValidation validator type (must have parameterless constructor)

**Returns:**
- `MapperValidationResult<TDest>` - Contains `IsValid`, `Value`, `Errors`, `ErrorsByProperty`

**Example:**
```csharp
var result = mapper.MapAndValidate<User, UserDto, UserDtoValidator>(user);
if (result.IsValid) return result.Value;
```

---

#### `MapAndValidate<TDest>(this IMapper mapper, object? source, IValidator<TDest> validator)`

Maps source to destination and validates using a provided validator instance.

**Example:**
```csharp
var validator = new UserDtoValidator();
var result = mapper.MapAndValidate<UserDto>(user, validator);
```

---

#### `Validate<T, TValidator>(this T value)`

Validates an existing object using the specified validator type.

**Example:**
```csharp
var result = dto.Validate<UserDto, UserDtoValidator>();
```

---

### NamingConventions Extensions (`using Mapsicle.NamingConventions`)

#### `MapWithConvention<TSource, TDest>(this TSource source, NamingConvention sourceConvention, NamingConvention destConvention)`

Maps source to destination with naming convention transformation.

**Parameters:**
- `sourceConvention` - The naming convention of source properties
- `destConvention` - The naming convention of destination properties

**Returns:**
- `TDest?` - New instance with convention-matched properties

**Example:**
```csharp
var dto = apiResponse.MapWithConvention<ApiResponse, UserDto>(
    NamingConvention.SnakeCase,
    NamingConvention.PascalCase);
```

---

#### `ConvertName(this string name, NamingConvention from, NamingConvention to)`

Converts a property name from one convention to another.

**Example:**
```csharp
var snakeName = "UserName".ConvertName(NamingConvention.PascalCase, NamingConvention.SnakeCase);
// Result: "user_name"
```

---

#### `NamingConvention.NamesMatch(string sourceName, NamingConvention sourceConvention, string destName, NamingConvention destConvention)`

Checks if two names match when their conventions are applied.

**Example:**
```csharp
bool match = NamingConvention.NamesMatch("user_id", NamingConvention.SnakeCase, "UserId", NamingConvention.PascalCase);
// Result: true
```

---

### Serilog Extensions (`using Mapsicle.Serilog`)

#### `MapsicleLogging.UseSerilog(ILogger logger)`

Enables global Serilog integration for Mapsicle mapping operations.

**Parameters:**
- `logger` - Serilog ILogger instance

**Example:**
```csharp
MapsicleLogging.UseSerilog(Log.Logger);
```

---

#### `MapWithLogging<TSource, TDest>(this TSource source, ILogger logger)`

Maps source to destination with timing and structured logging.

**Returns:**
- `TDest?` - Mapped destination object

**Example:**
```csharp
var dto = user.MapWithLogging<User, UserDto>(logger);
// Logs: Mapsicle: Mapped User -> UserDto in 0.5ms
```

---

#### `MapCollectionWithLogging<TSource, TDest>(this IEnumerable<TSource> source, ILogger logger)`

Maps a collection with aggregated timing and logging.

**Returns:**
- `List<TDest>` - List of mapped destination objects

**Example:**
```csharp
var dtos = users.MapCollectionWithLogging<User, UserDto>(logger);
// Logs: Mapsicle: Mapped 100 User -> UserDto items in 5.2ms
```

---

#### `MapsicleLogging.SlowMappingThreshold`

Configures the threshold for slow mapping warnings.

**Default:** 100ms

**Example:**
```csharp
MapsicleLogging.SlowMappingThreshold = TimeSpan.FromMilliseconds(50);
```

---

### Dapper Extensions (`using Mapsicle.Dapper`)

#### `QueryAndMap<TSource, TDest>(this IDbConnection connection, string sql, ...)`

Executes a SQL query and maps results to destination type.

**Overloads:**
- `QueryAndMap<TSource, TDest>(sql, param?, transaction?, commandTimeout?)` - Auto-mapping
- `QueryAndMap<TSource, TDest>(sql, MapperConfiguration, param?, ...)` - With configuration
- `QueryAndMap<TSource, TDest>(sql, IMapper, param?, ...)` - With mapper instance

**Returns:**
- `IEnumerable<TDest>` - Mapped results

**Example:**
```csharp
var users = connection.QueryAndMap<User, UserDto>("SELECT * FROM Users").ToList();
```

---

#### `QueryAndMapAsync<TSource, TDest>(this IDbConnection connection, string sql, ...)`

Async version of QueryAndMap.

**Example:**
```csharp
var users = await connection.QueryAndMapAsync<User, UserDto>("SELECT * FROM Users");
```

---

#### `QuerySingleAndMap<TSource, TDest>(this IDbConnection connection, string sql, ...)`

Executes a query expecting a single result and maps it.

**Returns:**
- `TDest?` - Mapped result or null

**Example:**
```csharp
var user = connection.QuerySingleAndMap<User, UserDto>(
    "SELECT * FROM Users WHERE Id = @Id", param: new { Id = 1 });
```

---

#### `QueryFirstAndMap<TSource, TDest>(this IDbConnection connection, string sql, ...)`

Executes a query and maps the first result.

**Returns:**
- `TDest?` - First mapped result or null

**Example:**
```csharp
var user = connection.QueryFirstAndMap<User, UserDto>("SELECT * FROM Users ORDER BY CreatedAt DESC");
```

---

#### `MapTo<TSource, TDest>(this IEnumerable<TSource>? source, IMapper mapper)`

Maps an existing collection using a provided mapper.

**Returns:**
- `List<TDest>` - Mapped results

**Example:**
```csharp
var users = connection.Query<User>("SELECT * FROM Users");
var dtos = users.MapTo<User, UserDto>(mapper);
```

---

## Complete Feature List

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

### Validation Features (Mapsicle.Validation)
- ✅ MapAndValidate with FluentValidation
- ✅ Validator type parameter
- ✅ Validator instance injection
- ✅ Validation result with IsValid, Errors
- ✅ ErrorsByProperty dictionary
- ✅ GetValueOrThrow pattern
- ✅ Validator caching

### Naming Convention Features (Mapsicle.NamingConventions)
- ✅ PascalCase convention
- ✅ camelCase convention
- ✅ snake_case convention
- ✅ kebab-case convention
- ✅ MapWithConvention extension
- ✅ ConvertName string extension
- ✅ NamesMatch cross-convention comparison
- ✅ Property mapping cache

### Serilog Features (Mapsicle.Serilog)
- ✅ UseSerilog global integration
- ✅ MapWithLogging extension
- ✅ MapCollectionWithLogging extension
- ✅ Slow mapping warnings
- ✅ MappingLoggingScope for batch operations
- ✅ Structured logging with properties
- ✅ Configurable thresholds

### Dapper Features (Mapsicle.Dapper)
- ✅ QueryAndMap / QueryAndMapAsync
- ✅ QuerySingleAndMap / QuerySingleAndMapAsync
- ✅ QueryFirstAndMap / QueryFirstAndMapAsync
- ✅ Transaction support
- ✅ Custom MapperConfiguration support
- ✅ IMapper instance support
- ✅ MapTo collection extension

---

## Test Coverage

`dotnet test Mapsicle.sln -c Release` runs all of it, including the allocation budgets.

| Package                    |   Tests | What it covers                                  |
| :------------------------- | ------: | :---------------------------------------------- |
| Mapsicle                   |     284 | Core, plus regression, load, fault and untrusted input |
| Mapsicle.NamingConventions |      55 | Naming conventions                              |
| Mapsicle.Fluent            |      39 | Fluent configuration and profiles               |
| Mapsicle.Validation        |      27 | FluentValidation integration                    |
| Mapsicle.Json              |      26 | JSON serialization                              |
| Mapsicle.Audit             |      26 | Change tracking                                 |
| Mapsicle.Dapper            |      25 | Dapper integration                              |
| Mapsicle.DataAnnotations   |      24 | DataAnnotations validation                      |
| Mapsicle.AspNetCore        |      23 | ASP.NET Core helpers                            |
| Mapsicle.Serilog           |      22 | Serilog logging                                 |
| Mapsicle.Caching           |      21 | Caching integration                             |
| Mapsicle.EntityFramework   |      19 | EF Core ProjectTo                               |
| Mapsicle.Performance       |       8 | Allocation budgets on warm paths                |
| **Total**                  | **599** |                                                 |

Four of those suites exist because a passing test count on its own says very little:

- **`IssueRegressionTests`** carries one test per fixed defect, named by issue number. Reverted
  against the pre-fix source, 19 of its 28 fail. The 9 that pass are the controls, and they are
  supposed to pass either way.
- **`UntrustedInputTests`** pins the security statements in this README, so a change to any of
  them fails the build rather than quietly making the documentation wrong.
- **`FaultInjectionTests`** covers what happens when a mapping fails: which exception surfaces,
  whether it is wrapped, whether the destination is left half-written, and whether a failure
  poisons the cache for later calls.
- **`LoadTests`** verifies every mapping against the input that produced it, because the failure a
  shared compiled delegate produces under concurrency is a wrong answer rather than an exception.

Allocation budgets run under `dotnet test` rather than only in a benchmark, so a change that
starts boxing a value or allocating a closure on a warm path fails CI instead of being noticed in a
profiler later.

---

## Project Structure

```
Mapsicle/
├── src/
│   ├── Mapsicle/                    # Core - zero config
│   ├── Mapsicle.Fluent/             # Fluent + DI + Profiles
│   ├── Mapsicle.EntityFramework/    # EF Core ProjectTo
│   ├── Mapsicle.Validation/         # FluentValidation integration
│   ├── Mapsicle.NamingConventions/  # Naming convention support
│   ├── Mapsicle.Serilog/            # Serilog structured logging
│   ├── Mapsicle.Dapper/             # Dapper integration
│   ├── Mapsicle.Json/               # JSON serialization
│   ├── Mapsicle.AspNetCore/         # ASP.NET Core Minimal API
│   ├── Mapsicle.Caching/            # Memory/Distributed caching
│   ├── Mapsicle.Audit/              # Change tracking/diff
│   └── Mapsicle.DataAnnotations/    # DataAnnotations validation
├── tests/
│   ├── Mapsicle.Tests/
│   ├── Mapsicle.Fluent.Tests/
│   ├── Mapsicle.EntityFramework.Tests/
│   ├── Mapsicle.Validation.Tests/
│   ├── Mapsicle.NamingConventions.Tests/
│   ├── Mapsicle.Serilog.Tests/
│   ├── Mapsicle.Dapper.Tests/
│   ├── Mapsicle.Json.Tests/
│   ├── Mapsicle.AspNetCore.Tests/
│   ├── Mapsicle.Caching.Tests/
│   ├── Mapsicle.Audit.Tests/
│   ├── Mapsicle.DataAnnotations.Tests/
│   └── Mapsicle.Benchmarks/
└── examples/
    └── Mapsicle.Examples/           # Working examples for all packages
```

### Run Examples

```bash
dotnet run --project examples/Mapsicle.Examples
```

---

## Contributing

PRs welcome. Areas for contribution:
- Performance optimizations
- Additional type coercion scenarios
- Documentation improvements

---

## License

MPL 2.0 License © [Arnel Isiderio Robles](https://github.com/arnelirobles)

---

**Stop configuring. Start mapping.**

*Free forever. Zero dependencies. Pure performance.*
