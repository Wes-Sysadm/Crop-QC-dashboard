# Crop QC overnight release standard

This standard is mandatory for every production release, including urgent and already-authorized releases. Authorization defines the permitted change; it does not waive a gate. Stop at the first failed or unproven gate.

Availability is the primary release gate. Maintenance is the final bounded execution window, never an open-ended environment for discovering production-shaped defects.

## 1. Freeze the candidate

- Confirm the exact PR, base, head, reviewed diff, mergeability, and required checks.
- Do not stack unrelated changes. Resolve a changed head by reviewing only the additional diff.
- After merge, record and freeze the exact merge/main SHA. Never deploy an unreviewed later commit.
- Keep Render auto-deploy off unless a separately reviewed release explicitly changes that policy.
- Freeze release main when final rehearsal begins. If source or production state materially changes afterward, take a new backup and repeat the affected rehearsal/readiness gates.

## 2. Capture live state read-only

- Record the exact live application SHA, deploy ID, service configuration, maintenance state, persistent disk, instance configuration, Data Protection key storage, schema target, migration-history fingerprint, and protected operational fingerprints.
- Record feature-flag values and the counts/statuses of any new durable workflow records.
- Treat actual live values as authoritative and distinguish concurrent business activity from release writes.

## 3. Take a fresh standard backup

- Immediately before schema or data work, start a new standard production backup. A prior rehearsal or release backup is not the release backup.
- Require: Succeeded, remote upload complete, read-back verification passed, readable ZIP, readable nonempty PostgreSQL dump, manifest/component hashes passed, retention completed, and lease released.
- Record run ID, package, bytes, SHA-256, captured application SHA, and completion time.
- If any verification fails, stop. Do not apply schema, data corrections, or deployment.

## 4. Rehearse from a brand-new restore

- Restore the fresh backup into a brand-new, clearly disposable PostgreSQL instance matching production's supported major version.
- Prove the pre-change failure/blocker where the release is corrective.
- Run the exact schema preflight and bounded compatibility package. Never use `dotnet ef database update` when the reviewed release calls for a compatibility package, and never edit `__EFMigrationsHistory` on that path.
- Run focused schema verification, the full application schema gate, inventory readiness, and the release-specific executable readiness command.
- Apply any bounded data correction only with fresh restore fingerprints and backup evidence. Prove exact post-state, protected-data invariants, and an idempotent AlreadyApplied rerun with zero writes.
- Exercise feature flags both off and on. Default-off functionality must be absent and server-blocked while existing workflows remain usable.
- Run authenticated route, authorization, antiforgery, concurrency, idempotency, lineage, reporting, and rollback rehearsals.
- Exercise every materially changed authenticated route. Synthetic mutation testing belongs only in disposable/restored databases.

## 5. Validate the exact merged candidate

- Run restore, build, the complete test suite, required JavaScript tests, PostgreSQL integration/rehearsal tests, EF pending-model check, exact schema gate, formatting, and `git diff --check`.
- Merge only the one reviewed PR. Then repeat the release-candidate gates from the exact merged main SHA; pre-merge results alone are insufficient.
- Commit a release-candidate manifest containing the immutable target SHA, required feature flags, schema/data commands, rehearsal evidence, deploy sequence, rollback sequence, and explicit stop conditions.

## 6. Production execution order

Unless the reviewed runbook is stricter, use this order:

1. Reconfirm exact live SHA, target SHA, auto-deploy off, and release authorization.
2. Enable maintenance mode only when authorized and immediately before production mutation.
3. Take and verify the fresh production backup.
4. Capture fresh protected fingerprints and run live read-only preflights/readiness.
5. Apply only the reviewed bounded schema package; verify focused schema, full gate, migration history, inventory readiness, and protected data.
6. Deploy the exact frozen merge SHA and verify requested, built, activated, and live SHAs match.
7. Run health endpoints, logs, authenticated routes, role/access, feature-off/on, and release-specific smoke tests.
8. Use fresh live fingerprints for any separately authorized bounded correction; apply once, prove exact effects, and prove AlreadyApplied with zero writes.
9. Recheck schema, readiness, protected fingerprints, reporting, operational records, memory, restarts, and repeating errors.
10. Remove maintenance mode only after every required gate passes. Leave auto-deploy off.

## 7. Stop and rollback rules

- Stop for an unexpected PR head, incompatible schema state, unverified backup, changed correction fingerprint, readiness blocker, non-idempotent result, wrong built SHA, health failure, HTTP 500, persistent DB/authorization error, OOM/restart loop, or unexplained protected-data change.
- Do not improvise production repairs or weaken a preflight/gate. Preserve evidence and report the exact blocker.
- Rehearse rollback before release. Rollback must name the exact prior application SHA and state whether the post-change database shape remains backward-compatible. A code rollback never authorizes reversing business data automatically.
- A critical operational HTTP 500 is a rollback trigger. Post-deploy troubleshooting is limited to 15 minutes; after that, roll back or disable the feature and reopen the PR. There is no "one more fix," and an unresolved release must not extend into business hours.
- Prefer additive/backward-compatible schema and application rollback. Never restore an older backup over newer legitimate production activity solely to undo a software release.
- Give major workflows a default-safe kill switch where practical.

## 8. Required handoff

Report Git/PR SHAs and state, backup evidence, schema and migration-history before/after, readiness counts/blockers, correction state and fingerprints, deployment IDs/SHAs/times, authenticated smoke results, feature-flag state, protected-data comparison, stability, rollback readiness, and any legitimate concurrent activity. Say READY only when every required item is proven.
