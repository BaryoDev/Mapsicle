- `CachingExtensions.CreateEntityCacheKey` names types by namespace, declaring type and generic
  arguments instead of the short name, so `Billing.Invoice` and `Legacy.Invoice` no longer share a
  key. Every key it returns changes: `mapsicle:User:UserDto:1` is now
  `mapsicle:MyApp.User:MyApp.UserDto:1`. Entries stored under the old keys are missed, not misread,
  and expire on their own.
