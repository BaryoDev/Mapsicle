# Contributing to Mapsicle

Thanks for your interest in contributing!

## Getting started

1. Fork and clone the repository.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Build and test:

   ```bash
   dotnet build Mapsicle.sln
   dotnet test Mapsicle.sln
   ```

## Making changes

- Create a feature branch off `main`.
- Keep the core `Mapsicle` package dependency-free; integration code belongs in the
  extension packages under `src/`.
- Add or update tests for any behavior change — every package has a matching test
  project under `tests/`.
- Run the full test suite before opening a pull request.
- For performance-sensitive changes to the core mapper, consider running the
  benchmarks in `tests/Mapsicle.Benchmarks`.

## Pull requests

- Describe what the change does and why.
- Reference any related issues.
- Update `CHANGELOG.md` under the unreleased version heading.

## Reporting bugs

Open a GitHub issue with a minimal reproduction (source/destination types and the
mapping call). For suspected security issues, see [SECURITY.md](SECURITY.md).
