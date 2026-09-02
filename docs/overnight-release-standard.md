# Crop QC overnight release standard

This standard is mandatory for every production release, including urgent and already-authorized releases. Authorization defines the permitted change; it does not waive a safety gate. Stop at the first failed or unproven release-specific gate.

Availability is the primary release gate. Maintenance is the final bounded execution window, never an open-ended environment for discovering production-shaped defects.

All validation under this release standard also follows `docs/change-scoped-testing-standard.md`.

The release standard protects production availability and data. It does **not** require recertifying unrelated Crop QC features for every release.

## 1. Freeze the candidate

- Confirm the exact PR, base, head, reviewed diff, mergeability, and required checks.
- Do not stack unrelated changes. Resolve a changed head by reviewing only the additional diff.
- After merge, record and freeze the exact merge/main SHA. Never deploy an unreviewed later commit.
- Keep Render auto-deploy off unless a separately reviewed release explicitly changes that policy.
- Freeze release main when final rehearsal begins. If source or production state materially changes afterward, repeat only the affected backup/rehearsal/readiness gates required by that change.

## 2. Capture live state read-only

Capture the production state needed to prove the release and rollback safely.

At minimum record:

- exact live application SHA and deploy ID;
- maintenance and auto-deploy state;
- current schema/migration target when the release touches schema;
- release-specific configuration or feature flags when changed;
- protected operational fingerprints for data the release can alter.

Do not collect broad unrelated fingerprints simply because they are available.

Treat actual live values as authoritative and distinguish concurrent business activity from release writes.

## 3. Take a fresh standard backup

Immediately before the first production schema/data mutation or deployment, take a new standard production backup.

A prior rehearsal or release backup is not the release backup.

Require:

- Succeeded;
- remote upload complete;
- independent read-back verification passed;
- readable ZIP;
- readable nonempty PostgreSQL dump;
- manifest/component hashes passed;
- retention completed;
- lease released.

Record run ID, package, bytes, SHA-256, captured application SHA, and completion time.

If any verification fails, stop. Do not apply schema, data corrections, or deployment.

## 4. Rehearse the affected production-shaped workflow

Use a brand-new disposable PostgreSQL restore when production-shaped validation materially improves confidence in the release, such as for migrations, data corrections, production-specific data shapes, inventory/accounting logic, or provider-specific behavior.

On that restore:

- prove the pre-change defect/blocker when the release is corrective;
- run the exact schema preflight/compatibility package when schema changes;
- run the release-specific readiness/invariant checks;
- apply bounded data corrections only with exact pre-state guards and prove exact post-state/idempotency;
- exercise only the materially affected authenticated routes and mutation workflows;
- prove the affected before/after data reconciliation and historical-evidence preservation;
- prove rollback compatibility when the release creates a meaningful rollback risk.

Do not automatically run a complete authenticated route catalog, unrelated authorization matrix, unrelated feature flags, or unrelated workflow mutations.

Synthetic mutation testing belongs only in disposable/restored databases.

## 5. Validate the exact merged candidate

Run the repository's standard change-scoped validation from `AGENTS.md` and `docs/change-scoped-testing-standard.md` against the exact final candidate.

Required baseline checks normally include:

- restore;
- build;
- focused affected-area tests;
- EF pending-model check;
- formatting;
- `git diff --check`;
- schema/provider validation when the change requires it.

A complete application test suite is not automatically required. Run it only when the change has a genuinely broad blast radius or a shared foundational dependency justifies it, and normally run it once near final review/merge rather than repeatedly.

Merge only reviewed work. Then confirm the exact merged SHA still represents the tested change. Repeat only final-candidate checks whose evidence can materially differ because of the merge/base change.

## 6. Production execution order

Unless the reviewed runbook is stricter, use this order:

1. Reconfirm exact live SHA, target SHA, auto-deploy state, and release authorization.
2. Enable maintenance mode only when the release actually requires a bounded outage and it is authorized.
3. Take and verify the fresh production backup before the first mutation/deploy.
4. Capture fresh protected fingerprints and run live read-only preflights/readiness relevant to the changed data.
5. Apply only the reviewed bounded schema/data package when applicable; verify the affected schema/invariants and protected data.
6. Deploy the exact frozen SHA and verify requested, built, activated, and live SHAs match.
7. Run health/database health plus the materially affected authenticated route/workflow smoke tests.
8. Use fresh live fingerprints for any separately authorized bounded correction; apply once, prove exact effects, and prove AlreadyApplied/idempotency where designed.
9. Recheck the release-specific readiness and affected protected data. Inspect logs for repeating errors in the changed workflow.
10. Remove maintenance mode only after every required release-specific gate passes. Leave auto-deploy in its reviewed final state.

Do not add unrelated production testing merely to increase the number of checks.

## 7. Stop and rollback rules

Stop for an unexpected PR head, incompatible schema state, unverified backup, changed correction fingerprint, release-specific readiness blocker, non-idempotent correction result, wrong built SHA, health failure, persistent HTTP 500 in an affected/core dependency, persistent DB/authorization error caused by the release, OOM/restart loop, or unexplained protected-data change.

Do not improvise production repairs or weaken a preflight/gate. Preserve evidence and report the exact blocker.

Rehearse rollback when the change creates a meaningful application/schema/data compatibility risk. Rollback must name the exact prior application SHA and state whether the post-change database shape remains backward-compatible. A code rollback never authorizes reversing legitimate business data automatically.

A critical operational HTTP 500 caused by the release is a rollback trigger. Post-deploy troubleshooting is limited to 15 minutes; after that, roll back or disable the feature and reopen the PR. There is no "one more fix," and an unresolved release must not extend into business hours.

Prefer additive/backward-compatible schema and application rollback. Never restore an older backup over newer legitimate production activity solely to undo a software release.

## 8. Data-integrity release gate

For releases that can change operational data, final success requires proving the affected data is trustworthy, not merely that the application is responsive.

Verify, as applicable:

- expected quantities before/after;
- no loss, duplication, or double deduction;
- correct Grower/Grower Lot/Fruit Profile/Organic identity;
- correct room/facility/custody/reporting attribution;
- treatment consistency;
- valid parent/revision/correction relationships;
- correct current authoritative state;
- preservation of historical evidence;
- no unintended writes outside the release scope.

For metadata-only changes, explicitly prove operational quantities and unrelated records stayed unchanged.

## 9. Required handoff

Report:

- Git/PR SHAs and state;
- backup evidence;
- schema/migration result when applicable;
- release-specific readiness counts/blockers;
- affected before/after data reconciliation;
- historical evidence preserved;
- deployment ID/SHA/time;
- affected authenticated smoke results;
- rollback readiness when required;
- any legitimate concurrent activity;
- unrelated areas intentionally not retested.

If validation was broadened beyond the changed area, state the exact shared dependency or risk that justified it.

Say READY/SUCCESS only when the affected workflow, its data-integrity gates, and required production safety gates are proven.