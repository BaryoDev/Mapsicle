# Migrating from AutoMapper

This page is about moving a real codebase, not about why you might want to. If you have forty
profiles and `IMapper` injected in thirty handlers, this is what changes and what does not.

Every code sample here is compiled and executed by `tests/Mapsicle.Docs.Tests`. A sample that stops
working fails the build, because a migration guide whose samples quietly stopped compiling is the
documentation version of a benchmark that prints a number and exits 0.

## The short version

| You have | You get |
| :------- | :------ |
| A `Profile` per area with `CreateMap` per pair | Nothing. Convention handles it. Delete them. |
| `IMapper` injected everywhere | `IMapperInstance` injected everywhere, one package to install |
| `ForMember` with a resolver | `Mapsicle.Fluent`, same shape |
| `ProjectTo<T>()` | `Mapsicle.EntityFramework`, same shape |
| `AssertConfigurationIsValid()` | No direct equivalent, and see below |

The bulk of a migration is deleting configuration, not translating it.

## Registration

AutoMapper:

```csharp
services.AddAutoMapper(typeof(Startup).Assembly);
```

Mapsicle, after installing `Mapsicle.DependencyInjection`:

```csharp
services.AddMapsicle();
```

There is no assembly to scan because there is nothing to find. A type pair that was never
registered still maps.

## Injection and call sites

AutoMapper:

```csharp
public class OrderHandler
{
    private readonly IMapper _mapper;
    public OrderHandler(IMapper mapper) => _mapper = mapper;

    public OrderDto Handle(Order order) => _mapper.Map<OrderDto>(order);
}
```

Mapsicle:

```csharp
public class OrderHandler
{
    private readonly IMapperInstance _mapper;
    public OrderHandler(IMapperInstance mapper) => _mapper = mapper;

    public OrderDto Handle(Order order) => _mapper.MapTo<OrderDto>(order)!;
}
```

Two differences worth knowing. The interface is `IMapperInstance`, and the method is `MapTo` rather
than `Map`. `Map` exists but means something else here: it maps onto an existing destination, which
is AutoMapper's `Map(source, destination)` overload.

The return is nullable, because a null source maps to null rather than throwing.

## Profiles and CreateMap

Most of these disappear. This AutoMapper profile:

```csharp
public class OrderProfile : Profile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderDto>();
        CreateMap<Customer, CustomerDto>();
        CreateMap<Address, AddressDto>();
    }
}
```

has no Mapsicle equivalent. Matching names map by convention, including nested objects and
flattening (`Address.City` fills `AddressCity`). Delete the profile and the maps keep working.

Keep configuration only where convention is wrong:

```csharp
var config = new MapperConfiguration(c =>
    c.CreateMap<Order, OrderDto>()
        .ForMember(d => d.Total, o => o.MapFrom(s => s.Lines.Sum(l => l.Price)))
        .ForMember(d => d.InternalNote, o => o.Ignore()));

var mapper = config.CreateMapper();
```

That needs `Mapsicle.Fluent`, and the shape is close enough that most `ForMember` chains port
directly.

## The validation difference, and it matters

AutoMapper throws when it reaches a pair you never configured, and
`AssertConfigurationIsValid()` tells you at startup which ones you missed. That safety net exists
because forgetting a `CreateMap` is the most common AutoMapper bug.

Mapsicle has no such failure mode, because there is nothing to forget. It also means there is no
startup check that catches a destination property you expected to be filled and which is not.

What it offers instead is per-pair:

```csharp
Mapper.AssertMappingValid<Order, OrderDto>();       // throws, listing unmapped members
var unmapped = Mapper.GetUnmappedProperties<Order, OrderDto>();
```

If your team relied on `AssertConfigurationIsValid()` as a release gate, put
`AssertMappingValid` for the pairs you care about into a test. That is a real change in habit, not
a like-for-like swap.

## Behaviour differences a migration will actually hit

**Cycles.** AutoMapper needs `PreserveReferences()` or `MaxDepth(...)` and an unhandled cycle can
still overflow the stack. Mapsicle returns the destination default once `MaxDepth` (32) is reached,
with no configuration. If you configured `PreserveReferences`, note that Mapsicle does not preserve
identity: two references to the same object become two mapped objects.

**Unconfigured pairs.** AutoMapper throws. Mapsicle maps by convention. Code that relied on the
throw as a signal loses that signal.

**Wrong-typed dictionary values.** Since 2.0.0 they are dropped rather than parsed, matching the
object path. Set `Mapper.CoerceDictionaryValues = true` if you were relying on parsing.

**Shallow copy.** A destination member that can hold the source instance receives that instance
rather than a copy, so mutating the source afterwards reaches into the destination. AutoMapper
behaves the same way, so this is usually not a change, but it is worth knowing if you map onto
long-lived entities.

## Where Mapsicle is the wrong answer

The comparison table in the README is the honest one and it is worth reading before committing to a
migration. In short:

- If every pair is known at compile time and you are willing to declare them, Mapperly is 2.5x to
  3x faster and generates code with no runtime apparatus at all.
- If collection throughput is what your workload is bound by, Mapsicle is about 1.33x slower than
  AutoMapper on collections. That number is measured and it is in the README.
- If you need NativeAOT with no runtime code generation, Mapsicle compiles expression trees at
  first use and is not the tool.

Mapsicle wins where the shapes are not all known when you compile, where the licence has to be
permissive, and where you would rather not write a `CreateMap` per pair.

## Suggested order for a large codebase

1. Install `Mapsicle.DependencyInjection` and call `AddMapsicle()` alongside the AutoMapper
   registration. Both can coexist.
2. Move one handler. Change `IMapper` to `IMapperInstance` and `Map<T>` to `MapTo<T>`.
3. Write `AssertMappingValid` tests for the pairs that handler uses, so you find convention
   mismatches now rather than in production.
4. Repeat by area. Delete each profile once nothing references it.
5. Remove the AutoMapper package last, and let the licence-boundary question go away with it.
