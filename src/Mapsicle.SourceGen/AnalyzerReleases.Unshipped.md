; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MSG001 | Mapsicle | Warning | A declared pair could not be generated and falls back to the runtime engine.
MSG002 | Mapsicle | Info | MapperGenerator
MSG003 | Mapsicle | Warning | The project compiles with a C# version older than 9, so nothing is generated and the engine maps every pair.
