- The `Mapsicle.Dapper` overloads taking a `MapperConfiguration` reuse one mapper per configuration
  instead of calling `CreateMapper()` on every query, which rebuilt every mapping plan each time.
  Every query and map extension now fills a pre-sized list by index, so it allocates no more than
  the same query and loop written by hand.
