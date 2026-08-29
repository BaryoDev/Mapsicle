"""
Fails the build when line coverage drops below a floor.

Nothing measured coverage before this. coverlet.collector was referenced in
Directory.Packages.props and invoked by no CI job, so the number that closed the
Mapsicle.Validation coverage issue could not be reproduced from CI at all.

Worse, five test projects did not reference the collector, so their suites produced no
coverage data even when it was collected by hand. Mapsicle.Validation read 12.7% with
twenty-seven passing tests against it, purely because none of them were being counted.
The real figure was 74.6%. A measurement that quietly excludes most of its input is the
same failure as a gate that cannot fail: it reports a number nobody can act on.

The floor is a measured value with a little headroom below it, the way the allocation
budgets are set, not a round number someone liked.
"""

import glob, sys, xml.etree.ElementTree as ET
from collections import defaultdict

# Cobertura reports one file per test project, and they overlap: several suites cover the same
# assembly. Lines are unioned by (file, line) so a line covered by any suite counts once, which is
# what "is this line exercised" actually means.
covered, total = defaultdict(set), defaultdict(set)

for path in glob.glob("**/coverage.cobertura.xml", recursive=True):
    for pkg in ET.parse(path).getroot().iter("package"):
        name = pkg.get("name") or "?"
        for cls in pkg.iter("class"):
            filename = cls.get("filename") or "?"
            for line in cls.iter("line"):
                key = (filename, line.get("number"))
                total[name].add(key)
                if int(line.get("hits") or 0) > 0:
                    covered[name].add(key)

if not total:
    sys.exit("no coverage data was produced")

rows = []
for name in sorted(total):
    t, c = len(total[name]), len(covered[name])
    rows.append((name, c, t, 100.0 * c / t if t else 0.0))

print(f"{'assembly':<40} {'covered':>8} {'lines':>8} {'pct':>7}")
for name, c, t, pct in rows:
    print(f"{name:<40} {c:>8} {t:>8} {pct:>6.1f}%")

grand_c = sum(len(covered[n]) for n in total)
grand_t = sum(len(total[n]) for n in total)
overall = 100.0 * grand_c / grand_t
print(f"\n{'OVERALL':<40} {grand_c:>8} {grand_t:>8} {overall:>6.1f}%")

if len(sys.argv) > 1:
    floor = float(sys.argv[1])
    if overall < floor:
        sys.exit(f"\ncoverage {overall:.1f}% is below the floor of {floor}%")
    print(f"\ncoverage {overall:.1f}% holds the floor of {floor}%")
