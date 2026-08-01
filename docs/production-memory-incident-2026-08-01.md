# Production memory incident — 2026-08-01

## Finding

Render terminated the 512 MiB web process after it exceeded the instance memory
limit. This was a platform kill, not a managed `OutOfMemoryException`. The same
failure occurred on July 30 under an earlier deployment, so the evidence does not
support attributing the recurrence to PR #167.

The August 1 service events record memory-limit terminations at 11:13 and 11:15 AM
Pacific, followed by automatic recovery. The separately reported 2:56 PM time did
not correspond to a Render service event and is treated as notification timing,
not the authoritative restart time.

Render metrics showed a low-CPU, rising-memory pattern followed by abrupt process
resets. After recovery, the working set settled around 384–420 MiB, leaving little
headroom on the existing 512 MiB Starter instance. The plan, instance count, and
autoscaling settings were not changed.

## Dominant allocation path

Production request logs repeatedly showed Rooms/Current Inventory condition queries
materializing the complete QC sample, fruit-reading, and defect entity graph. A
verified production backup was restored into disposable PostgreSQL 18 bound only to
localhost. A Release/Production route benchmark against that restore measured about
24 MiB of process allocation per Rooms or Current Inventory request.

The field-sample page also issued an accidental refresh every three seconds in
addition to its configured 15–300 second polling interval. A refresh allocated only
about 0.26 MiB in the same benchmark, so it was a request amplifier rather than the
dominant per-request source.

## Remediation

- Replace the Rooms/Current Inventory entity graph with three no-tracking compact
  projections: sample headers, required fruit scalars, and defect names.
- Preserve the existing condition, defect, sample-link, room-count, and ledger
  calculations over compact immutable records.
- Remove the redundant hard-coded three-second field-sample refresh. The existing
  server-configured poll remains, with its 15–300 second bounds.
- Disable production EF command logging at Information level. Request diagnostics
  retain counts and timings without SQL text, parameters, identities, or content.
- Add a once-per-minute bounded runtime memory summary with working/private bytes,
  GC heap/load/fragmentation, collection counts, active/peak request concurrency,
  thread-pool state, and warning/critical pressure classification.

## Restored-production benchmark

The before and after runs used the same Release build mode, Production environment,
localhost-only restored database, authenticated read-only route matrix, request
counts, and concurrency levels.

| Phase | Before allocation | After allocation | Change | Before peak | After peak |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rooms, 100 sequential | 2.419 GB | 1.008 GB | -58.3% | 286 MB | 262 MB |
| Current Inventory, 100 sequential | 2.401 GB | 0.986 GB | -58.9% | 276 MB | 270 MB |
| Sample refresh, 100 sequential | 25.9 MB | 25.9 MB | unchanged | 244 MB | 247 MB |
| Mixed routes, concurrency 8 | 495.6 MB | 255.7 MB | -48.4% | 304 MB | 252 MB |

Every phase completed successfully. Total response bytes were identical before and
after for each phase. The benchmark test enforces allocation-per-request ceilings
for Rooms, Current Inventory, sample refresh, and mixed concurrency, plus a 384 MiB
peak-working-set ceiling for the concurrency-8 run.

## Safety and verification

The investigation used Render events, metrics, bounded logs, and local benchmarks;
no live memory dump was taken. The restore was read-only and verified migration and
core record counts before benchmarking. No production inventory, receipts, QC data,
corrections, backups, plan settings, or credentials were changed during diagnosis.
