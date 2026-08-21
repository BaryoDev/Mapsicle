# Mapsicle

An object mapper for .NET. A zero-dependency core, with capability added as opt-in packages.

Human-facing contribution rules live in `CONTRIBUTING.md`. This file is the working agreement for
anyone, person or agent, changing code here.

---

## 1. What the project is selling

Two claims, and everything else is downstream of them:

1. **MPL 2.0, where AutoMapper is not.** AutoMapper 15 is RPL-1.5 or a paid licence from Lucky
   Penny Software. RPL-1.5 is strong reciprocal: it obliges you to publish source of software built
   with it, including software only deployed internally.
2. **Faster than AutoMapper, and it allocates almost nothing.**

Both are gated in CI, because a claim nothing checks is a claim that quietly stops being true. The
README once said "2x faster" on a path its own benchmark measured at parity, and that survived
because the benchmark job printed a number and exited 0.

- `licence-boundary` fails if anything under `src/` references AutoMapper. Compare against it in
  `tests/` as much as you like; `tests/Mapsicle.Benchmarks` is never packed.
- `core-has-no-dependencies` packs `src/Mapsicle` and fails if the nuspec declares a dependency.
- `claims` runs the comparison and fails if a ratio moves outside its bound.

If you change a claim in the README, change its gate in the same pull request.

## 2. Layout

```
src/Mapsicle/            The core. Zero package dependencies, and it stays that way.
src/Mapsicle.*/          Optional packages, each its own NuGet package
tests/Mapsicle.Tests/    Core tests, including regression, load, fault and untrusted input
tests/Mapsicle.Performance.Tests/  Allocation budgets, run by dotnet test
tests/Mapsicle.Benchmarks/         BenchmarkDotNet, plus the claim check
```

Optional packages depend on the core, never the reverse.

## 3. One rule stated once

The conversion cascade deciding how one property maps to another lives in
`src/Mapsicle/PropertyConversion.cs` and nowhere else.

It used to be written out three times, once per entry point. The copies drifted, and the drift
shipped: two defects lived in all three, and a third lived in one, which is how 1.2.3 released a
mapper that dropped nested objects only when built by `MapperFactory`.

If you are adding a conversion rule and find yourself editing more than one file, stop. Add it to
`PropertyConversion` and let the call sites ask.

## 4. Verification

```bash
dotnet test Mapsicle.sln -c Release                      # everything
dotnet run -c Release --project tests/Mapsicle.Benchmarks -- --quick   # the claim check
```

### A fix without a failing test is not a fix

Either write the failing test first, or revert the production change and confirm the test goes red
before re-applying it. A test that passes both ways proves nothing, and this codebase has already
shipped one gate that passed on an empty measurement.

To revert for a mutation check, use `git checkout <base> -- src/`, not `git stash`: if the change is
already committed there is nothing to stash and the check silently passes.

### Every guard needs a positive control

A test asserting that something is refused passes just as well when the thing does not exist. Pair
it with a test that the legitimate case still works. In `IssueRegressionTests` the controls are the
tests that pass both before and after a fix, and that is correct: narrowing must stay unmapped,
non-null must still convert, genuine flattening must still map.

### Assert the exact status, not "not the good one"

`Assert.Throws<InvalidOperationException>` rather than "an exception was thrown". Exactly `401`
rather than "not 200", since a 404 satisfies the loose form while meaning the thing is simply
absent.

### Beware the coincidental pass

Mapping delegates are cached in static fields keyed by (source runtime type, destination type), so a
test mapping a pair another test already used exercises a delegate compiled earlier and proves
nothing about your change. Give test types names unique to your file, and call `Mapper.ClearCache()`.
Classes touching static `Mapper` state carry `[Collection("StaticMapperTests")]`; join it.

## 5. Performance is a correctness property here

`tests/Mapsicle.Performance.Tests` asserts allocation budgets and runs under `dotnet test`.
Allocation rather than time, because allocation is deterministic and a shared runner's wall clock is
not. A time-based gate on hosted CI produces flaky failures, which teaches everyone to rerun until
green.

The budgets are measured values plus a little headroom, not round numbers. If you move one, say in
the pull request what you measured and on what.

Warm paths must not allocate per call beyond the destination object. A boxed value, a closure, a
`params` array or a LINQ enumerator on a mapping path is a defect, not a style preference.

## 5a. Local tooling

`.claude/hooks/allocation-guard.sh` runs the allocation budgets when a core mapping file changes,
which turns a budget failure from a CI result into an immediate one.

It is **opt in**, and stays that way:

```bash
cp .claude/settings.local.json.example .claude/settings.local.json
```

There is deliberately no committed `settings.json` wiring it up. A hook referenced from committed
configuration executes automatically on any contributor's machine the moment they edit a matching
file, which makes it somewhere to hide code in a pull request. The budgets are already enforced for
everyone by CI, so committing the wiring would put automatic execution on every contributor to save
the maintainer a few seconds.

## 6. Security posture, stated rather than implied

A mapper copies every matching property it can. Pointed at a request body it will set anything whose
name lines up, including a field the caller had no business setting. That is true of every
convention mapper and it is not a defect, but it must never be papered over:

- the safe pattern is to map untrusted input into a DTO holding only the settable fields, never
  straight into a domain entity;
- `[IgnoreMap]` is a real control and is honoured on every entry point;
- values are copied, never interpreted or sanitised;
- a value of the wrong type is dropped rather than coerced or thrown.

`UntrustedInputTests` pins all of this. A failure there means a documented security property
changed.

## 7. Public API stability

Mapsicle ships as NuGet packages, so external code compiles against these types. Within a major
version do not remove or change the signature of a public member. Add an overload, mark the old one
`[Obsolete]` with a removal version at least one full major away, and have the old one call the new.

## 8. Comments

Default to none. Names and small methods carry the meaning. A comment earns its place when it
explains a non-obvious *why*, an invariant the types cannot express, or a deliberate edge case.

Where a comment explains a past defect, say what the wrong behaviour was. "Guarded so a null source
yields null" is worth less than "a null source threw NullReferenceException from inside the compiled
delegate, with a stack trace naming lambda_method and not the property".

No provenance noise: no `// fix for X`, no `// see PR #123`.

## 9. Commits and pull requests

- Branch: `{type}/{short-description}`, type one of `feature | bugfix | improvement | chore`
- PR body: `Closes #123` on its own line, since GitHub only auto-closes from the body
- Commit messages short and human. No AI attribution trailers, no `Co-Authored-By`.
- No em dashes in code, comments, commits or documentation.

## 10. Writing about the project

State what was measured, on what, and let the number be whatever it is. The typed path is about
1.6x AutoMapper and the untyped path is roughly at parity; write that, not a rounder number that
reads better. An evaluator who finds one inflated claim stops believing the rest of the page, and
this project's entire pitch is that it can be trusted more than the alternative.
