"""
Fail when a workflow file has a duplicate mapping key.

GitHub rejects such a file outright: no jobs run, and the run that appears is named after the file
path rather than the workflow, with no logs. It reads like "CI was not triggered" rather than "CI is
broken", which is a slow way to find out.

Most YAML parsers accept a duplicate key silently, taking the last value, so parsing alone does not
catch it. This checks for the thing that actually breaks.

publish.yml is the reason this is worth having. Nothing exercises it until a release tag is pushed,
so a break there would surface at the worst possible moment.
"""
import glob
import re
import sys


def duplicate_keys(path):
    problems, stack = [], {}
    for n, line in enumerate(open(path, encoding="utf-8").read().split("\n"), start=1):
        if not line.strip() or line.strip().startswith("#"):
            continue
        m = re.match(r"^(\s*)(-\s+)?([A-Za-z_][\w-]*):(\s|$)", line)
        if not m:
            continue
        indent = len(m.group(1)) + (len(m.group(2)) if m.group(2) else 0)
        key, is_item = m.group(3), bool(m.group(2))
        for deeper in [k for k in stack if k > indent]:
            del stack[deeper]
        if is_item:
            stack[indent] = {key}  # a new list item starts a fresh mapping
        else:
            seen = stack.setdefault(indent, set())
            if key in seen:
                problems.append(f"{path}:{n}: duplicate key '{key}'")
            seen.add(key)
    return problems


def main():
    files = sorted(glob.glob(".github/workflows/*.yml") + glob.glob(".github/workflows/*.yaml"))
    if not files:
        sys.exit("no workflow files found")

    problems = []
    for f in files:
        found = duplicate_keys(f)
        print(f"  {'FAIL' if found else 'ok  '}  {f}")
        problems += found

    if problems:
        print()
        for p in problems:
            print(f"::error::{p}")
        sys.exit(f"\n{len(problems)} duplicate key(s). GitHub would refuse to run these workflows.")

    print(f"\n{len(files)} workflow files, no duplicate keys")


if __name__ == "__main__":
    main()
