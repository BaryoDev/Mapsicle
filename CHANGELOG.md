# Changelog

All notable changes to Mapsicle are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

Nothing released since 2.0.0. The next release is 3.0.0, and its shape is settled rather than open,
so it is written down here. Everything below is planned and none of it is implemented.

### Planned for 3.0.0: the source generator option

Mapperly cannot be beaten at being Mapperly. A runtime mapper structurally loses to a source
generator on throughput, cold start, NativeAOT and compile-time diagnostics, and the platform is
moving toward AOT, so that headwind grows. 3.0.0 does not fight that race and does not overhaul the
engine. It absorbs the source generator as an opt-in lane behind the existing API, and spends the
real investment on the lane no generator can enter.

**Conceded to a generator, then absorbed as an option.** Generated code is hand-written code, which
is the throughput ceiling. Mapsicle pays `Expression.Compile` on the first map of each pair and
generated code pays nothing. Runtime IL generation is not permitted under AOT. Unmapped members can
be build diagnostics rather than a runtime surprise.

**Where a generator cannot follow, which is where the investment goes.** A
`Dictionary<string, object>` into a type gives a generator nothing to generate against. Neither do
collections whose item types are only known at runtime, or types arriving from plugins, reflection
or configuration. Nor does mapping with no declaration at all: no partial class, no `CreateMap`,
ever.

**A cache pre-loader, not an engine swap.** The engine already separates how a mapper is made from
how it is used: the first map of a pair builds a delegate, the cache holds it, every later call just
invokes it. The expression tree is only the factory. So the generator replaces nothing. It emits the
same delegate as plain C# at build time and registers it into the caches the engine already reads,
through a `[ModuleInitializer]` calling one new, purely additive seam:

```csharp
Mapper.RegisterGenerated<TSource, TDest>(mapper, requiresDepthTracking);
```

No existing signature changes, so the API stability rule holds and anyone who never installs the
generator package sees no difference. A pair generated at build time runs static code with no
compile step and no cold start. Everything else runs the engine exactly as it does today, so the
dynamic lane is the fallback rather than a casualty.

**Two doors for discovery.** 3.0.0 ships the explicit one:
`[assembly: MapsicleGenerate(typeof(User), typeof(UserDto))]`, which is the simplest thing that
proves the seam and the conformance harness end to end. 3.1 adds usage scanning, where the generator
walks call sites for `.MapTo<T>()` and generates for every pair whose source type is statically known
there, so pairs that can be fast become fast with no annotation and unresolvable call sites fall to
the runtime. Later, `[RequiresDynamicCode]` on the runtime fallback so the AOT analyzer warns only
when a dynamic path is genuinely reachable, and the comparison table can stop saying "partial" and
say something measured instead.

It ships as `Mapsicle.SourceGen`, a Roslyn analyzer package that contributes nothing at runtime, so
the core keeps its zero-dependency claim and the gate that enforces it.

**The one non-negotiable.** The conversion cascade would then exist in two forms, the runtime
expression builder and the generator's C# emitter. This project has already shipped the failure mode
where copies of that logic drift, twice: it is why `PropertyConversion` exists, and 2.0.0 found two
more entry points that had quietly stopped agreeing with it. So the conformance suite is built
before the generator emits its first mapping, not after: one table of conversion cases run through
both the runtime mapper and the generated mapper, asserting identical output on every row. That is
what makes "the option changes performance, never behaviour" true rather than hoped.

**Extension points become configuration, not code.** Custom converters, hooks, ignores, naming
conventions and `[MapFrom]` each modelled as data the delegate builder reads, so the runtime engine
consumes it when compiling and the generator consumes the identical model when emitting. An
extension added once then appears in both lanes and the conformance suite proves they agree. The
highest-value new surface is a pluggable resolver for runtime-shaped inputs, so a third party can
teach Mapsicle a `JsonElement`, an `IDataRecord` or a `DynamicObject` without a core pull request.

**Why this is a window rather than a wall.** AutoMapper would need to invert its configuration model
into static declarations to generate at all, which changes the API every consumer has written
against. Mapperly has no runtime engine, so a dynamic lane means writing one from scratch and then
shipping reflection inside a library whose identity is the absence of it, with no seam in its API to
hide a fallback behind. Mapsicle adds a package and rewrites nothing. None of that physically stops
either of them, so the advantage compounds only if 3.0.0 ships while the AutoMapper licence change
is still moving people.

## [2.0.0] - 2026-08-29

Trust release. An adversarial audit of the core found ten defects under a fully green test suite,
four of them behaviours where AutoMapper returns the trusted answer and Mapsicle did not, one of
which crashed the host process. All ten are fixed. The rest of this release is the gates that stop
them coming back, because every one of these defects lived in a place nothing was looking.

If you map numbers through more than one entry point, map into collections other than `List<T>`,
map dictionaries, or run in more than one locale, read the breaking changes below before upgrading.

### Breaking

- **The dictionary entry point no longer parses values of the wrong type.** `{ "Age": "123" }` into
  an `int Age` now leaves the destination at its default instead of parsing to `123`. The documented
  rule has always been that a value of the wrong type is dropped rather than coerced, and the object
  entry point always honoured it, but the dictionary path ran `Convert.ChangeType` on anything
  `IConvertible`. The two doors disagreed about what "wrong type" meant, and someone controlling a
  dictionary (a parsed form post, a document, a header bag) could push values through conversions
  the object path refuses. Set `Mapper.CoerceDictionaryValues = true` to keep parsing, which is a
  reasonable choice for form posts and now uses the invariant culture.
  ([#27](https://github.com/BaryoDev/Mapsicle/issues/27))
- **Numbers and dates convert to strings using the invariant culture.** `1234.5m` now maps to
  `"1234.5"` everywhere. It previously read the ambient thread culture and produced `"1234,5"` under
  de-DE, so a mapper feeding serialisation or persistence wrote values another region read back as
  different numbers. ([#31](https://github.com/BaryoDev/Mapsicle/issues/31))
- **`MapperOptions.MaxDepth` refuses a value below 1 and keeps the default of 32.** Zero used to be
  accepted and disabled the mapper completely: the first depth check failed before any property was
  read, so every call returned the destination default with nothing logged and nothing thrown.
  `Mapper.MaxDepth` has always guarded this; the two are now consistent.
  ([#34](https://github.com/BaryoDev/Mapsicle/issues/34))
- **`CacheInfo().Hits` and `Misses` report real counts** under `UseLruCache` instead of always
  zero. Anything asserting they are zero under load will now fail, which is the point.
  ([#32](https://github.com/BaryoDev/Mapsicle/issues/32))
- **The package description no longer claims "2x faster than AutoMapper."** The README and the CI
  claims gate both put the typed path at about 1.4x, and an inflated claim on the tin costs more
  trust than it buys. ([#21](https://github.com/BaryoDev/Mapsicle/issues/21))

Four more changes fix silently wrong output. They are listed under Fixed rather than here, but code
written against the broken behaviour will see different results: in-place `Map` now performs
conversions it used to skip, record constructors now receive widened values instead of defaults,
non-`List` collection destinations are now populated instead of empty, and interface-typed source
members are now mapped instead of dropped.

### Fixed

- **A reference cycle through a collection crashed the process.** A type holding a `List` of itself
  with a back edge overflowed the stack and terminated the host with an uncatchable
  `StackOverflowException`. The predicate deciding whether a type needs cycle protection treated any
  `IEnumerable` property as harmless, so a type that recursed only through a collection was judged
  acyclic and depth tracking was skipped entirely. Because the ASP.NET Core helpers map on this
  path, a self-referential request body was a remote unauthenticated crash. A collection is now
  judged by what it holds rather than by being a collection.
  ([#22](https://github.com/BaryoDev/Mapsicle/issues/22))
- **In-place `Map` silently skipped conversions that `MapTo` performed.** Mapping `int 42` onto a
  `long` destination left it unchanged, while the same property pair through `MapTo` gave `42`.
  `Map` hand-rolled a reduced cascade covering only assignable types, nested classes and
  `ToString`, so widening numerics, enum to integer and nullable to non-nullable fell through
  untouched. Both the static and `MapperFactory` versions were affected.
  ([#28](https://github.com/BaryoDev/Mapsicle/issues/28))
- **Constructor and record parameters ignored numeric widening.** An `int 42` mapped into a record
  with a `long` parameter arrived as `0`, because a widening pair is not assignable in the CLR type
  system and the argument fell to `Expression.Default`. Records are the standard modern DTO shape,
  so this hit the most common destination. All three constructor paths now ask the shared cascade.
  ([#29](https://github.com/BaryoDev/Mapsicle/issues/29))
- **Collection destinations other than `List<T>` and arrays came back empty.** A `HashSet<string>`
  destination was constructed, populated with nothing, and returned non-null, so callers saw a
  mapped-looking destination that had silently lost every item. Collections are now built through
  their `IEnumerable<T>` constructor, which covers `HashSet`, `SortedSet`, `Queue`, `Stack`,
  `Collection`, `ObservableCollection` and `Dictionary`.
  ([#30](https://github.com/BaryoDev/Mapsicle/issues/30))
- **An interface-typed source member was dropped.** `IThing Item` into a concrete `Thing Item` was
  never attempted, because the nested-object branch tested `IsClass` and an interface is not a
  class, so the member read downstream as one the source did not have. The recursive map resolves
  the runtime type, so the declared type only has to be able to hold a mappable instance.
  ([#35](https://github.com/BaryoDev/Mapsicle/issues/35))
- **A `MapTo` overload was still selected by reflection ordering.** One of the two sites named in
  [#4](https://github.com/BaryoDev/Mapsicle/issues/4) was fixed and the issue closed; the other, in
  in-place `Map`, kept `GetMethods().First(...)`. Three public overloads satisfy that predicate and
  `Type.GetMethods()` does not guarantee order, so the right one was picked by luck on .NET 8.
  ([#39](https://github.com/BaryoDev/Mapsicle/issues/39))
- **`[MapFrom]` naming a property that does not exist behaved differently per entry point.**
  `[MapFrom("DoesNotExist")]` on a destination property called `Name` mapped the source `Name`
  through `MapTo<T>(object)` and returned null through `MapTo<TSource, TDest>()`. The typed path
  resolved the attribute with its own inline scan that matched only the named property, while every
  other path fell back to the destination member's own name. All paths now fall back, so an
  attribute pointing at a property that is not there degrades to ordinary convention matching
  instead of silently leaving the member unmapped. Found while collapsing the duplicated binding
  loops, not by the audit. ([#41](https://github.com/BaryoDev/Mapsicle/issues/41))
- **`LruCache.Count` drifted upward under concurrent misses.** `ConcurrentDictionary.GetOrAdd` may
  run the factory on several threads and keep one result, and the count was incremented by every
  thread whose factory ran rather than by the one whose value was kept. The count drifted
  permanently above the real size and the cache began evicting below its own capacity. Values are
  now held in a `Lazy<T>` and counted by the wrapper's reference identity, which also stops the
  losing threads compiling an expression tree that is thrown away.
  ([#25](https://github.com/BaryoDev/Mapsicle/issues/25), reported by
  [@ZakariaHogeschoolR](https://github.com/ZakariaHogeschoolR))
- **`CachedMapper.InvalidateAll()` did nothing.** It was an empty method whose only content was a
  comment saying memory caches cannot be cleared, while its documentation said it invalidates all
  cache entries. A caller who invoked it kept receiving stale mappings until they expired, with
  nothing to indicate the call had had no effect. It now removes the entries this mapper created.
  It deliberately does not call `MemoryCache.Clear()`, because the cache is normally resolved from
  the container and shared, so clearing it wholesale would evict entries belonging to components
  that have nothing to do with mapping.
- **The in-place `Map` shallow-copy contract is now documented and pinned by tests.** A
  directly-assignable reference-typed member is shared with the source rather than copied, on every
  entry point, so mutating the source afterwards reaches into the destination. This is deliberate
  and matches AutoMapper, but it was undocumented, which meant a caller pointing `Map` at a
  long-lived entity had no way to know. ([#33](https://github.com/BaryoDev/Mapsicle/issues/33))

### Added

- **`Mapsicle.DependencyInjection`**, a new package. `services.AddMapsicle()` registers a mapper
  with no configuration at all, then inject `IMapperInstance`. The only registration before this
  lived in `Mapsicle.Fluent` and both overloads demanded a configuration callback, so a library
  whose argument is that no configuration is needed could not be registered without writing some.
  It is a separate package because the core declares no dependencies and a CI gate enforces that.
  ([#44](https://github.com/BaryoDev/Mapsicle/issues/44))
- **A package icon.** All twelve packages listed on nuget.org with no mark. `assets/logo.svg` is
  the master; `assets/icon.png` is the 128px raster NuGet requires, downscaled from a 512 render
  because rasterising that detail directly at 128 comes out muddy. `PackageIcon` is wired once in
  `src/Directory.Build.props` and inherited by every package. A pack gate fails if the icon is
  absent from the nupkg or undeclared in the nuspec, because NuGet quietly lists a placeholder
  rather than refusing the push. The listing reads its icon from the newest version, so it appears
  with this release.
- **net10.0 across every package and test project.** net8.0 goes end of life on 10 November 2026 and
  two packages targeted it alone. The suite runs on both runtimes.
  ([#24](https://github.com/BaryoDev/Mapsicle/issues/24))
- **XML documentation in every package.** `GenerateDocumentationFile` was set nowhere, so all twelve
  packages shipped without it and gave consumers bare signatures in IntelliSense. The doc comments
  had been written the whole time and were discarded at build. Turning it on raised 258 warnings
  across 43 public members, all of which are documented rather than suppressed.
  ([#43](https://github.com/BaryoDev/Mapsicle/issues/43))
- `Mapper.CoerceDictionaryValues`, opting the dictionary path back into parsing wrong-typed values,
  invariantly. ([#27](https://github.com/BaryoDev/Mapsicle/issues/27))

### Verification

The defects above all shipped under a green suite, so this release adds the checks that would have
caught them. Each was confirmed capable of failing before being trusted.

- **An entry-point conformance matrix.** One conversion table run through every public door,
  asserting they agree. Three of the ten audit findings were the same shape, one door converting
  where another did not, and no test asked that question because the suite is organised by feature
  rather than by entry point. Six rows failed when it was written.
  ([#38](https://github.com/BaryoDev/Mapsicle/issues/38))
- **The AutoMapper head to head runs in CI.** The claim that Mapsicle can be trusted at least as
  much as AutoMapper had been checked once, by hand, during an audit. It now fails the build if
  Mapsicle is worse on any row, including hostile input.
  ([#37](https://github.com/BaryoDev/Mapsicle/issues/37))
- **The netstandard2.0 assemblies are tested.** They were compiled, packed and published without a
  test ever loading them, because every test project resolved the net8.0 asset. A dedicated project
  forces the netstandard2.0 asset and asserts it really loaded it before asserting anything else.
  ([#23](https://github.com/BaryoDev/Mapsicle/issues/23))
- **A public API baseline.** Section 7 of CLAUDE.md promises no public member changes within a
  major version and nothing checked it. The surface of all thirteen packages is now compared against
  a committed baseline, captured in 2.0.0 because a major version is the window where breaks are
  allowed. ([#40](https://github.com/BaryoDev/Mapsicle/issues/40))
- **A coverage floor at 70%.** Coverage was collected by no CI job. Measuring it also revealed that
  five test projects did not reference `coverlet.collector`, so their suites produced no data at
  all: `Mapsicle.Validation` read 12.7% with twenty-seven passing tests against it purely because
  none of them were counted. The real figure is 74.6%, and overall is 71.7%.
  ([#42](https://github.com/BaryoDev/Mapsicle/issues/42))
- **A pack gate for XML documentation**, so it cannot quietly stop being shipped.

## [1.3.0] - 2026-08-21

Correctness release. Seven defects, two of which corrupted data silently and three of which threw
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
