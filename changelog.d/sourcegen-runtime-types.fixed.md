- **A generated mapper now maps a derived instance the way the engine does.** A member declared
  `Animal` and holding a `Dog`, a `List<Animal>` or `Animal[]` element holding one, and a `Dog` behind
  a variable typed `Animal` were all mapped as `Animal`, so `Breed` came back empty where the engine
  fills it. Generated code now checks the runtime type and hands anything that is not exactly the
  declared type to the engine. A sealed type gets no check. Under NativeAOT, declare the derived pair
  too, or the call throws `NotSupportedException` instead of returning the base members only.
- **An array into a `List<T>` of the same class no longer shares the elements.** `Tag[]` into
  `List<Tag>` put the source instances in the list, where the engine builds a new `Tag` for each. A
  pair whose element class cannot be generated is now refused with `MSG001` and maps through the
  engine.
- **A collection whose destination element is an interface is refused.** An `IThing[]` into a
  `List<IThing>` put the source instances in the list, where the engine leaves each element null.
  The pair now gets `MSG001` and maps through the engine.
