- The single-object performance gate now fails above 0.95x AutoMapper (it allowed 1.10x), and the
  README says which numbers CI enforces. The README no longer calls declared pairs "AOT clean", since
  publishing reports trim and AOT analysis warnings from the runtime fallback. The publish workflow
  requires the tagged commit to be on the default branch and a successful `ci.yml` run for it, and
  the unused `dry_run` input is removed.
