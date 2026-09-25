"""
Changelog entries live in changelog.d/, one file per change, and are folded into CHANGELOG.md at
release. Every pull request used to append to the same spot in CHANGELOG.md, so each merge put the
next one in conflict. Separate files never conflict.

  changelog.py check BASE      fail if src/ changed since BASE and no entry was added
  changelog.py release X.Y.Z   move every entry into a dated section and delete the files
"""
import datetime
import glob
import os
import re
import subprocess
import sys

SECTIONS = ["added", "changed", "deprecated", "removed", "fixed", "security"]
NAME = re.compile(r"^changelog\.d/[\w.-]+\.(" + "|".join(SECTIONS) + r")\.md$")


def entries():
    paths = sorted(p for p in glob.glob("changelog.d/*.md") if not p.endswith("README.md"))
    bad = [p for p in paths if not NAME.match(p)]
    if bad:
        sys.exit("::error::changelog.d entries must be named <name>.<section>.md, with section one of "
                 + ", ".join(SECTIONS) + ": " + ", ".join(bad))
    empty = [p for p in paths if not open(p, encoding="utf-8").read().strip().startswith("- ")]
    if empty:
        sys.exit("::error::each changelog.d entry is a markdown list item starting with '- ': " + ", ".join(empty))
    return paths


def check(base):
    entries()
    changed = subprocess.run(["git", "diff", "--name-only", "--diff-filter=AMR", base, "HEAD"],
                             check=True, capture_output=True, text=True).stdout.split()
    if not any(p.startswith("src/") for p in changed):
        print("src/ unchanged, no changelog entry needed")
        return
    if any(NAME.match(p) for p in changed):
        print("changelog entry present")
        return
    sys.exit("::error::this pull request changes src/ but adds no file to changelog.d/. "
             "See changelog.d/README.md, or label the pull request 'no changelog' if users will not notice.")


def release(version, date):
    paths = entries()
    if not paths:
        sys.exit("no entries in changelog.d/, nothing to release")
    grouped = {s: [] for s in SECTIONS}
    for p in paths:
        grouped[NAME.match(p).group(1)].append(open(p, encoding="utf-8").read().strip("\n"))
    body = f"## [{version}] - {date}\n\n"
    for s in SECTIONS:
        if grouped[s]:
            body += f"### {s.capitalize()}\n\n" + "\n".join(grouped[s]) + "\n\n"
    text = open("CHANGELOG.md", encoding="utf-8").read()
    at = text.index("\n## [") + 1
    open("CHANGELOG.md", "w", encoding="utf-8").write(text[:at] + body + text[at:])
    for p in paths:
        os.remove(p)
    print(f"{len(paths)} entries moved under {version}")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "check":
        check(sys.argv[2])
    elif len(sys.argv) in (3, 4) and sys.argv[1] == "release":
        release(sys.argv[2], sys.argv[3] if len(sys.argv) == 4 else datetime.date.today().isoformat())
    else:
        sys.exit(__doc__)
