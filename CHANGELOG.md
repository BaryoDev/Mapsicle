# Changelog

All notable changes to Mapsicle are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
