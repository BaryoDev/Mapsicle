- An enum value with no named member now passes through as its number when mapping into another
  enum, on every lane including the generator, the way `"999"` already did from a string. It used
  to become member 0. A defined member with no counterpart in the destination still maps to the
  default, and so does an undefined value too wide for the destination's underlying type.
- Lossy conversions are no longer treated as widening: `int` or `long` into `float`, `long` into
  `double`, and an enum whose underlying type does not fit into `int` or `long`. Those members are
  now left unmapped, like any other narrowing. `int` into `double` and `decimal` still map.
- A collection member whose elements cannot convert (a `List<long>` into `List<int>`, or text that
  does not parse) is now left unmapped. It used to be filled with a default for every element.
- `MapperOptions.MaxDepth` on `MapperFactory` now means what it means on the static mapper: the
  depth past which circular references are checked. It used to truncate every chain at that depth,
  cycle or not.
