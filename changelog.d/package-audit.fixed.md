- `ToProblemDetails` responds with `Content-Type: application/problem+json`, as RFC 9457 and
  `Results.ValidationProblem` do. It used `application/json`. The result is now a
  `ProblemHttpResult` rather than `BadRequest<ValidationProblemDetails>`; the body is unchanged.
- `MapCollectionFromJson` and `MapCollectionToJson` keep a null element as null in its position.
  They dropped it, which shifted every later element and broke callers that pair the output with
  the input.
