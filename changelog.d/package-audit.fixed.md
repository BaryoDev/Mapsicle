- `ToProblemDetails` responds with `Content-Type: application/problem+json`, as RFC 9457 and
  `Results.ValidationProblem` do. It used `application/json`. The result is now a
  `ProblemHttpResult` rather than `BadRequest<ValidationProblemDetails>`; the body is unchanged.
- `MapCollectionFromJson` and `MapCollectionToJson` keep a null element as null in its position.
  They dropped it, which shifted every later element and broke callers that pair the output with
  the input.
- Dapper: rows from an untyped `Query()` and `QueryAndMap<dynamic, T>` map their columns. They came
  back as empty DTOs, because a `DapperRow` holds its columns behind `IDictionary<string, object>`
  and the extensions took the property path. Rows now go through the dictionary overload.
- EntityFramework: `ProjectTo` widens numbers the way `MapTo` does, so an `int` column into a `long`
  member projects the value instead of `0`. It uses the core's widening table, including the
  nullable forms, and still leaves narrowing unmapped.
- Serilog: `LogCacheStatus = false` leaves `IsCached` off the event, and `MapCollectionWithLogging`
  respects `LogLevel`. `MapWithLogging` no longer allocates more than `MapTo` when there is nothing
  to write: it cost 40 B a call more with no logger and 144 B more with a logger above Information.
