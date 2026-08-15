# Contributing

Contributions are welcome, including small ones. This adds to the
[BaryoDev-wide guide](https://github.com/BaryoDev/.github/blob/main/CONTRIBUTING.md); where the two
disagree, this file wins.

## Getting it running

```bash
git clone https://github.com/BaryoDev/Mapsicle.git
cd Mapsicle
dotnet build Mapsicle.sln
dotnet test Mapsicle.sln
```

That is 487 tests across 15 test projects and it finishes in under ten seconds. If it takes longer
than a minute you are probably restoring packages for the first time, which includes Mapperly's
source generator for the benchmark project.

You need the .NET 8 SDK. `global.json` pins the feature band and allows a later patch, so a newer
SDK is fine as long as .NET 8 targeting packs are installed.

To run the benchmarks:

```bash
cd tests/Mapsicle.Benchmarks
dotnet run -c Release -- --quick   # smoke, a few seconds
dotnet run -c Release              # full BenchmarkDotNet suite, several minutes
```

## What this project is for

Mapsicle is a runtime object mapper that competes with AutoMapper and Mapperly. Two properties
decide most design questions, and both are checkable claims rather than adjectives:

**It is fast on the warm path.** A warm `MapTo<TSource, TDest>()` of a small DTO allocates the
destination object and nothing else, 32 bytes for a five-property type. Mapping compiles an
expression tree once per type pair and caches the delegate; everything after that is a delegate call.
Anything that puts reflection, boxing, a LINQ operator or a closure allocation on the per-call path
is not an optimisation detail, it is a change to what the package is.

**The core has zero dependencies.** `src/Mapsicle` has no `PackageReference` and the README promises
that. Integration code belongs in the extension packages under `src/`, each of which owns its own
third-party dependency. If your change needs a package, it needs a package project.

## Tests

**Every change needs a test that fails without it.** If you cannot write one, say so in the pull
request and explain why. The worked version of this rule, including how to tell a real test from one
that only looks like one, is at
[BaryoDev/.github/TESTING.md](https://github.com/BaryoDev/.github/blob/main/TESTING.md).

The question that decides it: **name the production change that would make this test fail.** If the
answer is "deleting the method" rather than "getting the behaviour wrong", the test is checking
wiring, not behaviour.

For a mapper the concrete form of that is easy, so there is no excuse: assert the mapped **value**,
not that the result is non-null. `Assert.NotNull(dest)` passes for any destination type with a
parameterless constructor even if every single property was skipped. There are tests in this repo
that do exactly that, and at least one region of `TypeEdgeCasesTests.cs` is named for coverage it
does not have. Please do not add more.

A useful sanity check before you open the PR: comment out your fix and run your test. If it still
passes, it is testing something else.

## Things worth knowing before you change something

**The same property-binding logic exists three times.** A conversion rule lives in all of:

- `Mapper.CreatePropertyBinding` in `src/Mapsicle/Mapsicle.cs:1295`, used by `MapTo<T>(object)`
- `Mapper.CreateTypedPropertyBinding<TSource>` in `src/Mapsicle/Mapsicle.cs:578`, used by
  `MapTo<TSource, TDest>()`
- `MapperInstance.CreatePropertyBinding` in `src/Mapsicle/MapperFactory.cs:437`, used by
  `MapperFactory.Create()`

Flattening likewise exists at `Mapsicle.cs:1352`, `Mapsicle.cs:617` and `MapperFactory.cs:492`. They
have already drifted once: version 1.2.3 fixed nested-object mapping that was broken only in the
`MapperFactory` copy. If you fix a conversion, fix all three and write a test that exercises all
three. Consolidating them is welcome and is tracked as its own issue; if you take it on, open the
discussion before the pull request.

**Mapping delegates are cached in static fields, keyed by the source's runtime type.** Two
consequences. First, `MapTo<T>(object)` builds its mapper from `source.GetType()`, so a `Dog` in a
`List<Animal>` maps as a `Dog`, while `MapTo<TSource, TDest>()` builds from `typeof(TSource)` and
maps it as an `Animal`. The two overloads legitimately produce different results for the same object
and both are intended. Second, `Mapper.UseLruCache`, `Mapper.MaxCacheSize`, `Mapper.MaxDepth` and
`Mapper.Logger` are process-global. A test that sets one and does not restore it changes the
behaviour of every test that runs after it in the same assembly.

xUnit runs test classes in parallel. Classes in `tests/Mapsicle.Tests` that touch that static state
carry `[Collection("StaticMapperTests")]`, which serialises them. If your test writes any static
`Mapper` property, join that collection and restore the previous value in a `finally`. A test that
skips this fails intermittently and looks like a mapper bug.

**The core multi-targets `netstandard2.0` and `net8.0`.** `LangVersion` is `latest`, so modern C#
syntax compiles, but a `net8.0`-only BCL API in `src/Mapsicle` or `src/Mapsicle.Fluent` breaks the
`netstandard2.0` build. CI catches it; the error message will not obviously say "you used a .NET 8
API", so check the target framework in the failure before you go looking elsewhere.

`Mapsicle.EntityFramework` and `Mapsicle.AspNetCore` are `net8.0` only, on purpose.

**Circular references return `default` rather than throwing.** `Mapper.MaxDepth` is 32 and
`IncrementDepth` returns `false` past it, which silently yields `null` for that branch of the graph.
That is the documented behaviour and the difference the README claims against AutoMapper, so a change
that makes deep graphs throw is a breaking change, not a bug fix.

**`src/Mapsicle/Mapsicle.csproj` has `InternalsVisibleTo("Mapsicle.Tests")`.** Internals of the core
are testable from that project only.

## Performance changes

The pitch is performance, so a change to `src/Mapsicle` gets held to it.

`tests/Mapsicle.Performance.Tests` asserts allocation budgets on warm mapping paths. It runs as part
of `dotnet test`, so a change that adds an allocation to a hot path turns the suite red rather than
being discovered by a user. If your change moves a budget, move it deliberately in the same commit
and say why in the pull request. Do not delete the assertion.

Wall-clock time is not asserted in CI because hosted runners are too noisy to make that honest. For
anything that could plausibly change throughput, run the benchmarks locally before and after and
paste both tables into the pull request:

```bash
cd tests/Mapsicle.Benchmarks
dotnet run -c Release --filter '*CoreMapperBenchmarks*'
```

## Pull requests

- Branch off `main`.
- Describe what changed and why. Reference the issue.
- Add the entry to `CHANGELOG.md` under the unreleased heading.
- CI runs build, tests, formatting and a dependency audit on Linux and Windows. All of it must pass;
  none of it is advisory.

## Reporting bugs

Open an issue with a minimal reproduction: the source type, the destination type, the mapping call,
what you got, and what you expected. For a mapper that is usually fifteen lines and it is worth more
than any description. For suspected security issues see [SECURITY.md](SECURITY.md).

## Good first issues

Look for [`good first issue`](https://github.com/BaryoDev/Mapsicle/labels/good%20first%20issue). Each
one names the file, the current behaviour and the test that should go from red to green.
