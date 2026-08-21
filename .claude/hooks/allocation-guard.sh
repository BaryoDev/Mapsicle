#!/usr/bin/env bash
# Runs the allocation budgets when a core mapping file changes.
#
# OPT IN ONLY. This is deliberately not wired up by a committed settings.json. A hook referenced
# from committed configuration executes automatically on any contributor's machine the moment they
# edit a matching file, which makes it a place to hide code in a pull request. The allocation
# budgets are already enforced for everyone by CI, so committing the wiring would put automatic
# execution on every contributor to save the maintainer a few seconds. That is the wrong trade.
#
# To use it: cp .claude/settings.local.json.example .claude/settings.local.json
# That file is gitignored, so it runs for you and for nobody else.
#
# These budgets are the reason a warm map still allocates only its destination. The failure they
# catch is silent: a boxed value or a LINQ operator on a mapping path compiles, keeps every
# functional test green, and only shows up as a number. Catching it at the edit is worth the two
# seconds; catching it in CI means it is already in a commit, and catching it in a profiler means
# it is already in a release.
set -uo pipefail

payload=$(cat)
file=$(printf '%s' "$payload" | python3 -c 'import json,sys;print(json.load(sys.stdin).get("tool_input",{}).get("file_path",""))' 2>/dev/null)

case "$file" in
  */src/Mapsicle/*.cs|*/src/Mapsicle.Fluent/*.cs) ;;
  *) exit 0 ;;
esac

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
out=$(cd "$repo" && dotnet test tests/Mapsicle.Performance.Tests/Mapsicle.Performance.Tests.csproj \
        -c Release --nologo 2>&1)

if printf '%s' "$out" | grep -q "Failed!"; then
  echo "Allocation budget failed after editing $(basename "$file"):" >&2
  printf '%s\n' "$out" | grep -E "allocated|Failed " | head -5 >&2
  exit 2
fi
exit 0
