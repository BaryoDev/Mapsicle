# Changelog

All notable changes to Mapsicle are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-08-21

Correctness release. Six defects, two of which corrupted data silently and three of which threw
from inside a compiled delegate. If you map numbers, nullable references, or collections holding
more than one runtime type, this is worth taking.

### Fixed

- **Widening numeric conversions produced the destination default instead of the value.** Mapping
  `int 42` into a `long` gave `0`. Same for `int` to `decimal` and `decimal` to `double`. Nothing
  was logged and nothing threw, so the first sign was wrong data downstream. The cause is that
  `Type.IsAssignableFrom` is `false` for every widening pair, since the CLR type system has no
  notion of the implicit numeric conversions the C# language defines, so these fell out of the
  conversion cascade entirely. Narrowing stays unmapped, deliberately, and `double` to `decimal` is
  excluded because it throws on values outside decimal's range.
  ([#5](https://github.com/BaryoDev/Mapsicle/issues/5))
- **A null reference-typed source mapped to a `string` destination threw
  `NullReferenceException`** from inside the compiled delegate, with a stack trace naming
  `lambda_method` and not the property. It now yields `null`, which is what the mapper does
  elsewhere with a value it cannot produce, and what AutoMapper does. Seven call sites were
  unguarded. ([#2](https://github.com/BaryoDev/Mapsicle/issues/2))
- **A collection holding more than one runtime type threw `InvalidCastException`.** A
  `List<Animal>` containing a `Dog` and then a `Cat` failed on the second item, because the cached
  delegate is compiled for the first item's type and its first instruction is a cast. The odd item
  out now maps through its own delegate. ([#6](https://github.com/BaryoDev/Mapsicle/issues/6))
- **`AssertMappingValid` passed for properties the mapper never populates.** It reported
  `NameLength` as mapped from a `string Name`, because `Name` is a prefix and `string` has a
  `Length` property, while the mapper skips `string` sources outright. A validator that certifies
  an unmapped property is worse than no validator. Both now consult one rule.
  ([#3](https://github.com/BaryoDev/Mapsicle/issues/3))
- **`ClearCache()`, `CacheInfo()` and `MaxCacheSize` could not see the strongly-typed mapper
  cache.** It lived in a per-closed-generic static field, so `CacheInfo()` reported `0` with
  delegates cached, `ClearCache()` did not clear it, and it was never bounded. In an application
  closing generics over many type pairs that is permanent, unreportable retention. The fast path is
  still a static field read. ([#7](https://github.com/BaryoDev/Mapsicle/issues/7))
- **The recursive `MapTo` overload was selected by reflection ordering.**
  `GetMethods().First(...)` matched three public overloads and `GetMethods()` does not guarantee
  order, so nested mapping worked by luck. Selected by exact signature now.
  ([#4](https://github.com/BaryoDev/Mapsicle/issues/4))
- **The EF projection cache grew by one entry per `MapperConfiguration` and never shrank.** It was
  keyed partly on the configuration's identity hash, so an application building a configuration per
  request grew it for the life of the process. Projections now live in a `ConditionalWeakTable`
  keyed by the configuration and become collectable with it.
  ([#9](https://github.com/BaryoDev/Mapsicle/issues/9))

### Performance

- **`Mapsicle.Fluent`'s in-place `Map(source, destination)` no longer reflects on every call.** It
  performed two `GetProperties` allocations, a LINQ closure per destination property and a
  `PropertyInfo.SetValue` per assignment, every time. The pairing depends only on the two types, so
  it is resolved once and the assignment compiled: **616 B per call to 0 B**, and 198 ms to 62 ms
  per 100,000 calls. Ignores, conditions, custom mappings and the before and after hooks are still
  evaluated per call, so behaviour is unchanged. ([#8](https://github.com/BaryoDev/Mapsicle/issues/8))

### Changed

- **The conversion cascade is written once.** It existed in three copies, one per entry point, and
  they had drifted: two of the defects above lived in all three, and 1.2.3 shipped a mapper that
  dropped nested objects only when built by `MapperFactory`. Every call site now routes through
  `PropertyConversion`. ([#12](https://github.com/BaryoDev/Mapsicle/issues/12))
- **The README's performance table now matches what the benchmark measures, on two
  architectures.** It claimed 2.1x on single objects and rough parity on collections. Re-measured:
  1.37x on x64 Linux and 1.48x on arm64 macOS for single objects, and collections about a third
  slower than AutoMapper while allocating 19 percent less. The document had contradicted itself,
  since Known Limitations already said collections were slower.
  ([#16](https://github.com/BaryoDev/Mapsicle/issues/16))
- Package versions move to `Directory.Packages.props`. They were declared independently in thirteen
  test projects, so one advisory meant a sweep.
  ([#17](https://github.com/BaryoDev/Mapsicle/issues/17))

### Added

- **Gates for the two claims the project is sold on.** The benchmark job was advisory: it measured
  Mapsicle against AutoMapper, printed the ratio and exited zero regardless, which is how the
  README came to overstate it. It now fails the build when a ratio moves. A `licence-boundary` job
  fails if anything under `src/` references AutoMapper, which is RPL-1.5 or a paid licence and is
  the reason to choose Mapsicle in the first place.
- **Regression, untrusted input, fault injection and load suites.** 525 tests to 599. The
  regression suite carries one test per defect above; reverted against the pre-fix source, 19 of
  its 28 fail.
- **A documented security posture.** Over-posting works, as it does in every convention mapper. The
  README now says so, shows the DTO pattern that contains it, and `UntrustedInputTests` pins each
  statement so a change fails the build rather than making the documentation quietly wrong.
- **`Mapsicle.Validation` coverage.** Inverting every `if (source is null)` guard in that package
  used to change nothing: all thirteen tests still passed. The same mutation now fails four.
  13 tests to 27. ([#15](https://github.com/BaryoDev/Mapsicle/issues/15))
- `CLAUDE.md` and `CODEOWNERS`, neither of which the repository had.

### Behaviour changes to be aware of

- A null reference-typed source mapped to a `string` destination now yields `null` rather than
  throwing. Code relying on the exception will not see it.
- Widening numeric pairs now map. A destination that was silently left at its default will now hold
  the source value, which is the point, but it is a change in observable output.
- `MapAndValidate` and the mapper's own null handling are unchanged.

## [1.2.4] - 2026-08-19

Security release. One shipped change, deliberately: anyone taking this for the advisory
should not have to take a behaviour change with it.

### Security

- **EntityFramework**: `Microsoft.EntityFrameworkCore` moves from 8.0.0 to 8.0.30, clearing
  [GHSA-qj66-m88j-hmgj](https://github.com/advisories/GHSA-qj66-m88j-hmgj) (high) in the
  transitively resolved `Microsoft.Extensions.Caching.Memory` 8.0.0. Every install of
  `Mapsicle.EntityFramework` 1.2.3 and earlier inherited it. Staying on the 8.0 band:
  the package targets `net8.0`, and EF Core 9 is a separate decision. ([#10](https://github.com/BaryoDev/Mapsicle/issues/10))

### Changed

- `using` directives reordered by `dotnet format` in `Mapsicle.AspNetCore` and
  `Mapsicle.Caching`. No behavioural change; recorded because it is the only other
  difference in shipped source since 1.2.3.
- First release published through NuGet Trusted Publishing (OIDC), from a `v*` tag rather
  than a stored API key. ([#18](https://github.com/BaryoDev/Mapsicle/issues/18))

### Known issues, not fixed here

Verified present in this release and targeted at 1.3.0:

- Widening numeric conversions silently produce `0`: `int` to `long`, `int` to `decimal`,
  `decimal` to `double`. `int` to `int?` and `int` to `int` are unaffected, which is what
  makes it easy to miss. ([#5](https://github.com/BaryoDev/Mapsicle/issues/5))
- Mapping a null reference-typed property to a `string` destination throws
  `NullReferenceException`. ([#2](https://github.com/BaryoDev/Mapsicle/issues/2))

## [1.2.3] - 2026-07-10

### Fixed

- **Core**: `MaxDepth` circular-reference protection stayed disabled once a mapping
  delegate was cached, so mapping a cyclic object graph (e.g. EF bidirectional
  navigation properties) against a warm cache crashed the process with a
  `StackOverflowException`. Depth tracking now engages on every mapping of a type
  that can form cycles, including the collection fast paths.
- **Fluent**: `ReverseMap()` inside a `MapsicleProfile` registered a duplicate,
  unconfigured reverse map, causing `AssertConfigurationIsValid()` to fail with
  spurious "Unmapped member" errors even when the reverse side was fully configured.
- **Fluent**: the in-place `Map(source, destination)` overload now invokes
  `BeforeMap`/`AfterMap` hooks, consistent with the other `Map` overloads.
- **Core**: mapper instances created via `MapperFactory.Create()` silently dropped
  nested complex-object properties (left `null`); they now map nested objects like
  the static `Mapper`.
- **EntityFramework**: `ProjectTo<T>()` selected the `Queryable.Select` overload by
  reflection ordering; it now picks the non-indexed overload explicitly.
- **Core**: the bounded cache (`UseLruCache`) evicted in pure insertion (FIFO) order,
  so frequently-used mappers could be evicted and recompiled repeatedly; eviction now
  uses a second-chance scan that keeps recently-read entries.
- **Json**: resolved nullability warnings (CS8604) on the netstandard2.0 target.
- **Caching**: pinned `Microsoft.Extensions.Caching.Abstractions` to 8.0.0
  (8.0.1 does not exist and silently resolved to 9.0.0).

### Changed

- Package metadata is centralized in `src/Directory.Build.props`; packages now ship
  SourceLink debug info and `.snupkg` symbol packages with deterministic CI builds.
- Release builds stamp the release version into assembly metadata
  (`AssemblyVersion`/`FileVersion`), not just the NuGet package version.
- CI now runs on Windows in addition to Linux.

## [1.2.2] - 2026-07

- Improved handling of factory-created instances in `FluentMapper` and performance
  with existing objects.
- Optimized JSON mapping options initialization.
- Improved error handling in `NamingConventionExtensions`.
- Thread-safety improvements in the Serilog integration.
- Cached validator creation in validation extensions.
- Refined LRU cache eviction logic and added cache unit tests.
- Added CI workflow; updated project metadata.

## [1.2.0] and earlier

- Naming-convention and validation support.
- Dapper, DataAnnotations, Json, and Serilog integration packages and test suites.
- Initial releases of the core mapper and extension ecosystem.
