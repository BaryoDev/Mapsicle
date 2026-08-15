## What this changes

<!-- One or two sentences. What was the behaviour before, what is it now. -->

Fixes #

## The test

**Which test fails without this change?**

<!-- Name it: file and method. -->

**Name the production change that would make this test fail.**

<!-- If the honest answer is "deleting the method", the test is checking wiring, not behaviour.
     See https://github.com/BaryoDev/.github/blob/main/TESTING.md -->

- [ ] I commented out the fix, ran the test, and it failed.
- [ ] The test asserts a mapped **value**, not just that the result is non-null.

## If this touches a conversion or a binding rule

The same logic exists in three places. Tick the ones you changed, or say why only one applies.

- [ ] `Mapper.CreatePropertyBinding` (`src/Mapsicle/Mapsicle.cs`), used by `MapTo<T>(object)`
- [ ] `Mapper.CreateTypedPropertyBinding` (`src/Mapsicle/Mapsicle.cs`), used by `MapTo<TSource, TDest>()`
- [ ] `MapperInstance.CreatePropertyBinding` (`src/Mapsicle/MapperFactory.cs`), used by `MapperFactory.Create()`

## If this touches `src/Mapsicle`

- [ ] `tests/Mapsicle.Performance.Tests` still passes, or the budget change is deliberate and explained.
- [ ] No new `PackageReference` in `src/Mapsicle` (the core ships with zero dependencies).
- [ ] Builds for both `netstandard2.0` and `net8.0`.

## Housekeeping

- [ ] `CHANGELOG.md` updated under the unreleased heading.
