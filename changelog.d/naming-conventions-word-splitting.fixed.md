- `Mapsicle.NamingConventions`: an acronym is one word, so `HTTPServerID` converts to
  `http_server_id` instead of `h_t_t_p_server_i_d`, and `user_id` fills `UserID`.
- `Mapsicle.NamingConventions`: names match on their letters and digits wherever the words break,
  so `address_line_1` fills `AddressLine1`. Non-ASCII letters are part of a word, so `straße_name`
  fills `StraßeName`.
- `Mapsicle.NamingConventions`: the `IMapper` overload of `MapWithConvention` fills a member that
  still holds its initial value. It used to test for `default(T)`, so a `string` initialised to
  `""` or a `bool` initialised to `true` was never filled.
