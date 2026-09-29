- `ToProblemDetails` responds with `Content-Type: application/problem+json`, as RFC 9457 and
  `Results.ValidationProblem` do. It used `application/json`. The result is now a
  `ProblemHttpResult` rather than `BadRequest<ValidationProblemDetails>`; the body is unchanged.
- `MapCollectionFromJson` and `MapCollectionToJson` keep a null element in its position, as null,
  or as `default` for a non-nullable value type. They dropped it, which shifted
  every later element and broke callers that pair the output with the input.
- Dapper: rows from an untyped `Query()` and `QueryAndMap<dynamic, T>` map their columns. They came
  back as empty DTOs, because a `DapperRow` holds its columns behind `IDictionary<string, object>`
  and the extensions took the property path. Rows now go through the dictionary overload.
- EntityFramework: `ProjectTo` widens numbers the way `MapTo` does, so an `int` column into a `long`
  member projects the value instead of `0`. It uses the core's widening table, including the
  nullable forms, and still leaves narrowing unmapped.
- Serilog: `LogCacheStatus = false` leaves `IsCached` off the event, and `MapCollectionWithLogging`
  respects `LogLevel`. `MapWithLogging` no longer allocates more than `MapTo` when there is nothing
  to write: it cost 40 B a call more with no logger, 144 B more with a logger above Information,
  and 95 B more for a slow-mapping warning the logger filters out.
- Audit: `MapWithAudit` reports a member as mapped exactly when the mapper fills it, so a flattened
  `CustomerName`, a `[MapFrom]` member or a member filled from a public field reads as mapped with
  its source path, and a `long` into an
  `int` reads as unmapped. `Diff` and `WouldChangeOnMap` compare collections by their elements, skip
  indexers instead of throwing `TargetParameterCountException`, and no longer read private getters.
- `AssertMappingValid`, `GetUnmappedProperties` and Fluent's `AssertConfigurationIsValid` ask the
  mapper's own per-member binding instead of keeping their own matching rules. They accept a
  flattened member at any depth the mapper fills (`OuterMiddleLeafIso`), and reject a member the
  mapper drops: a narrowing `long` into `int`, or a name that only shares a prefix with a source
  member (`IdentityNumber` next to `Id`).
