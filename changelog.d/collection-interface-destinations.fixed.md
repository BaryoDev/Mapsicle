- A `List<T>` or array mapped into a destination member typed `IList<T>`, `ICollection<T>`,
  `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `ImmutableList<T>` is now filled.
  The member stayed null through every entry point while `List<T>` and `T[]` members worked. The
  generated lane fills them too.
