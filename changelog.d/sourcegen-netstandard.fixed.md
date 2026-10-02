- **A netstandard2.0 or .NET Framework project with `Mapsicle.SourceGen` installed builds again.**
  The generated file referenced `ModuleInitializerAttribute`, which those targets do not have, so the
  build failed with CS0234. The generator now declares an internal copy when the target has none.
  Below C# 9, which is where those targets start, the generated code failed with CS8370 and CS8627.
  The generator now emits nothing there, reports the new `MSG003` warning once, and every pair maps
  through the engine. Set `<LangVersion>9.0</LangVersion>` or later to generate.
