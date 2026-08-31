# Mapsicle 2.1: an MPL-2.0 object mapper, and honest numbers against AutoMapper

AutoMapper is the default object mapper in .NET and has been for over a decade. Since version 15 it
ships under the Reciprocal Public License 1.5, or a commercial agreement from Lucky Penny Software.
You can read that in the package: `LICENSE.md` inside `AutoMapper.15.1.3.nupkg` says so directly.

RPL-1.5 is strong reciprocal. Its source obligations reach software you only deploy internally, not
just software you distribute. For a lot of teams that is a licence review, a legal conversation, or a
purchase order. There is a free Community License for those who qualify, and if you qualify, this
post is probably not for you.

Mapsicle is MPL-2.0. That is the reason it exists. Everything below is a supporting argument.

## What it is

A convention based object mapper. It maps by matching property names, compiles an expression tree the
first time it sees a type pair, caches the delegate, and invokes it after that. No `CreateMap` per
pair, no profile classes, no configuration to declare a mapping that convention already gets right.

```csharp
var dto = user.MapTo<UserDto>();
var dtos = users.MapTo<UserDto>();
```

The core package has zero dependencies, and that is checked on every build rather than asserted. A CI
job packs `src/Mapsicle` and fails if the generated nuspec declares a single dependency.

## Speed

Measured with BenchmarkDotNet, five warmup iterations and twenty measured, on two architectures
because one is not evidence of anything portable. Against AutoMapper 15.1.3.

**x64, GitHub Actions runner:**

| scenario | Mapsicle | AutoMapper | Mapperly | vs AutoMapper |
| :--- | ---: | ---: | ---: | :--- |
| single object, 5 properties | 57.8 ns | 83.2 ns | 18.8 ns | 1.44x faster |
| flattening | 64.9 ns | 101.2 ns | 21.1 ns | 1.56x faster |
| collection of 100 | 2,175 ns | 2,618 ns | 1,933 ns | 1.20x faster |

**arm64, idle 4 core Ampere VM, median of three runs:**

| scenario | Mapsicle | AutoMapper | vs AutoMapper |
| :--- | ---: | ---: | :--- |
| single object | 101.2 ns | 131.3 ns | 1.30x faster |
| flattening | 110.0 ns | 140.2 ns | 1.28x faster |
| collection of 100 | 4,311 ns | 4,696 ns | 1.09x faster |
| deep nesting, 15 levels | 626 ns | 5,145 ns | 8.22x faster |
| collection of 10,000 | 481 us | 1,134 us | 2.36x faster |

Two of those need explaining rather than celebrating.

Deep nesting at 8.22x is the largest number here and it is about allocation as much as speed. Mapsicle
allocates 600 B for that graph against AutoMapper's 2,096 B.

Ten thousand elements at 2.36x is not a per element win. The per element cost barely changes. At that
size AutoMapper allocates 742 KB against Mapsicle's 560 KB, which is enough to reach generation 2
collections while Mapsicle stays in 0 and 1. The gap is garbage collection, not mapping.

On allocation generally: a single object and a flattening map allocate the destination and nothing
else, 48 B and 56 B, the same as hand written code. A collection of 100 allocates 5,656 B, which is 19
percent less than AutoMapper and identical to source generated Mapperly.

## Size

Two projects, each referencing one mapper and nothing else, `dotnet publish` on net8.0:

| | Mapsicle | AutoMapper 15.1.3 |
| :--- | ---: | ---: |
| the mapper's own assembly | 45.5 KB | 286.0 KB |
| assemblies it brings with it | 0 | 8 |
| total on disk | 45.5 KB | 1,117.4 KB |

More than half of what AutoMapper deploys is not mapping code. `Microsoft.IdentityModel.Tokens`,
`JsonWebTokens`, `Logging` and `Abstractions` come to 599.1 KB, and they are there because AutoMapper
15 validates a signed licence key. Referencing it puts a JWT validation stack into your dependency
closure, larger than the mapper itself, to check that you are allowed to use the mapper.

The remaining 232.4 KB is `Microsoft.Extensions.*` for dependency injection, options and logging.

## What 2.1 fixed

Three defects, and the first is the reason I would upgrade rather than wait.

**A fluent configuration was ignored entirely when mapping a collection.** A collection fell through to
the core mapper, which knows nothing about a fluent configuration, so `ForMember`, `Condition`,
`ResolveUsing` and `Ignore` were all skipped and elements came back mapped by convention. Concretely:

```
single object    PasswordHash=''             Ignore honoured: True
array collection PasswordHash='SECRET-HASH'  Ignore honoured: False
```

`Ignore()` protected one object and not a list of them. Nothing was raised and nothing was logged. If
you used `Ignore()` to keep a field out of a DTO, you had that protection for a single map and lost it
for a collection.

**`Map<List<T>>` returned an empty list.** Not null, not an exception, an empty list, from the call
shape people arriving from AutoMapper write first. `Map<T[]>` returned the elements, so the two
collection forms disagreed and the quiet one was the common one.

**Mapping into a type with a parameterized constructor dropped every other member.** The constructor
parameters were matched and filled and the mapping stopped there, so everything else kept its
initialiser. That is the shape of most immutable DTOs and every positional record. All four entry
points did it, from three separate copies of the same code, and they share one now.

Performance work in the same release: a nested reference costs 26 ns per member per item instead of 66,
a `List<T>` is mapped by a loop compiled for its element type, and a complex object through
`Mapsicle.Fluent` went from 3.49x AutoMapper to parity.

## Where Mapsicle loses

Mapperly wins on throughput and it is not close. At 18.8 ns against hand written code's 18.6 ns on a
single object, it is indistinguishable from writing the assignments yourself, because a source
generator emits ordinary C# at compile time and leaves no delegate, no cache lookup and no indirection.
Mapsicle and AutoMapper both build an expression tree, compile it, cache it, look it up and invoke
through it. That apparatus is the whole gap and no runtime mapper closes it.

What you buy with it: Mapperly needs a partial class, a `[Mapper]` attribute and a declared method for
every pair, all known at compile time. It cannot map a `Dictionary<string, object>` into a type chosen
at runtime, or a collection whose items turn out to have different runtime types. If all your mappings
are known at compile time and you are willing to declare them, use Mapperly.

`Mapsicle.Fluent` is slower on collections. A single complex object through the fluent configuration
API is at parity with AutoMapper, 298 ns against 289. A hundred of them is 1.22x, because the fluent
path maps element by element rather than through the compiled loop. That is in the changelog under a
heading saying it is not fixed.

Arrays keep the older loop. So do lists whose element type is `object`, an interface or abstract. The
compiled loop is built for a list's declared element type, and for those it would be built for a type
no element actually is, which measured 10.9x worse before it was guarded.

No NativeAOT. Expression trees compile at runtime. That is a real constraint and it is the reason 3.0
exists.

## What is in the box

Thirteen packages at 2.1.0. The core, then opt in:

`Mapsicle.Fluent` for configuration and profiles. `Mapsicle.DependencyInjection` and
`Mapsicle.AspNetCore` for registration and minimal API helpers. `Mapsicle.EntityFramework` for
`ProjectTo<T>()`. `Mapsicle.Validation` for FluentValidation and `Mapsicle.DataAnnotations` for the
attribute kind, both mapping and validating in one call. `Mapsicle.Json`, `Mapsicle.Dapper`,
`Mapsicle.Caching`, `Mapsicle.Audit` for a trail of what changed, `Mapsicle.NamingConventions` for
snake_case and friends, and `Mapsicle.Serilog`.

Optional packages depend on the core, never the reverse.

A few things the core does without configuration: cycles return the default at `MaxDepth` rather than
overflowing the stack, where AutoMapper needs `PreserveReferences()` or `MaxDepth(...)`. There is an
LRU cache option for long running processes that map types they discover at runtime. `CacheInfo()`
reports what has been compiled.

## What is next

3.0 absorbs a source generator as an opt in lane behind the same API. Not to beat Mapperly, which
cannot be beaten at being Mapperly, but so that pairs known at compile time stop paying for the
apparatus while pairs discovered at runtime still work. The engine already separates how a mapper is
made from how it is used, so the generator emits the same delegate as plain C# at build time and
registers it into the cache the engine already reads.

## A note on the numbers

Every figure here is reproducible with
`dotnet run -c Release --project tests/Mapsicle.Benchmarks -- --core`, the same job the CI gate runs.
If a claim in the README moves, the gate defending it moves in the same pull request.

That policy exists because the README once said 2x faster on a path its own benchmark measured at
parity, and it survived because CI printed a number and exited zero regardless. The performance gate
used to run three warmup iterations, which on a hosted runner produced a 99.9 percent interval of plus
or minus 43 percent of the mean on a claimed 7 percent difference. It could not have failed for its own
reason. It runs a job that can resolve what it reports now.

The arm64 figures are medians of repeated runs rather than single samples, because repeating an
identical commit on that machine moved an untouched Mapperly row by 36 percent. A benchmark's error
column tells you how consistent the iterations were inside one process, not whether the run reproduces.

AutoMapper's current release is 16.2.0. These numbers are against 15.1.3, which is what the benchmark
suite pins.

`dotnet add package Mapsicle`

Source, benchmarks and the migration guide: github.com/BaryoDev/Mapsicle
