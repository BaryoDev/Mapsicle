- A property with a private getter (`public string Secret { private get; set; }`) was read and
  copied by every runtime lane and written into `ToDictionary`. Only a public getter is read now.
- Flattening (`CustomerName` from `Customer.Name`) now works in `Map(existing)`, static and factory.
  A path through a null writes the default.
- `MapperFactory` mapped an acyclic chain deeper than `MaxDepth` as truncated, never mapped a
  dictionary member, threw on a collection type it could not build, and skipped getter-only
  collections, fields and flattening in `Map(existing)`. It now follows the static mapper's rules
  for all of them.
- A struct member now maps into a class member, and `Map(existing)` into a struct returns the mapped
  copy instead of the unmapped one.
- **Fluent**: a pair with no `CreateMap` now maps exactly as the core mapper does. A boxed `5` into
  `int` gave 0, flattening was skipped, and a `HashSet`, a `Dictionary` or a struct destination came
  back empty. `HashSet`, `ISet`, `Dictionary`, `IDictionary` and `IReadOnlyDictionary` destinations
  are filled element by element through the configuration, so an `Ignore` on the element pair holds.
- **Fluent**: in-place `Map(source, destination)` copied a member only when both types matched
  exactly, so an `int` into a `long` and nested objects were skipped. It now uses the core
  conversion rules. An ignored member and a failed condition leave the destination's value alone.
- **Fluent**: `Include<TDerivedSource, TDerivedDest>()` now returns the derived destination. A
  `Dog` mapped to `AnimalDto` came back as a plain `AnimalDto`, and a derived `AfterMap` threw
  `InvalidCastException`. List elements dispatch the same way.
- **Fluent**: `CreateConverter` now applies to members, not only to the top-level pair, on the
  constructing and in-place paths. A `Money` member into a `decimal` stayed at its default, and into
  a `string` got `Money.ToString()`.
- **Fluent**: calling `AddMapsicle` more than once replaced the earlier configuration, dropping its
  maps and ignores. The calls now merge into one `MapperConfiguration`, and validation covers the
  merged result.
