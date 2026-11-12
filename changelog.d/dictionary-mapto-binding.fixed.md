- `Dictionary<string, object?>.MapTo<T>()` into a positional record or a type with `required`
  members returned a `List<T>`, because the `where T : new()` constraint sent the call to the
  `IEnumerable` overload. The constraint is gone, and a type with no parameterless constructor is
  built through its public constructor with the most parameters, matched to keys by name. The
  approved API listing drops the constraint.
