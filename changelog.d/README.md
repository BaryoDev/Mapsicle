# Changelog entries

Add one file here per change a user would notice. Do not edit `CHANGELOG.md` directly; entries are
moved into it when a release is cut.

Name the file `<anything>.<section>.md`, where section is one of `added`, `changed`, `deprecated`,
`removed`, `fixed` or `security`. The branch name works well for the first part:
`fix-lru-race.fixed.md`.

The content is one or more list items, written as they should read in the changelog:

```markdown
- `CachedMapper` no longer throws on a cyclic source. A source too deep to describe is mapped
  without the cache.
```

CI fails a pull request that changes `src/` without adding an entry. Label it `no changelog` when
the change is invisible to users, such as a refactor or a comment.

To cut a release: `python3 .github/scripts/changelog.py release 2.4.0`, then bump `<Version>` in
`src/Directory.Build.props`.
