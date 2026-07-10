# Changelog

All notable changes to Mapsicle are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.2] - Unreleased

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

## [1.2.1] - 2026-07

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
