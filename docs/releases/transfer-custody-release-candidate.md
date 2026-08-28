# Transfer Custody release candidate

Status: **validated pre-merge; freeze the GitHub merge commit as `RELEASE_CANDIDATE_SHA` and repeat this manifest's gates from that exact commit before declaring release-ready**. A Git commit cannot contain its own hash; the immutable merge/main SHA is therefore recorded in the PR merge metadata and release handoff, while this document is the versioned manifest carried by that commit.

## Immutable targets

- Production baseline and application rollback target: `919b0117f0230c44e829279085ef5201178b3289`
- Release candidate: exact merge commit of the single focused `codex/harden-transfer-release` PR; no later commit may be substituted.
- Schema migration target: `20260828033737_AddTransferCustodyWorkflow`
- Application schema gate: `836 objects PASS`
- Production feature flag before release: `TransferCustody__Enabled=false`
- Auto-deploy: Off
- Expected synthetic production records: zero. Never create a production transfer, outside warehouse, or inventory movement for release testing.

## Rehearsal backup and restore

- Backup run: `114` (manual rehearsal backup; not the future deployment backup)
- Package: `cropqc-production-manual-20260828-180606.zip`
- Bytes: `7,863,054`
- SHA-256: `6dfccc2dc660718477c765d25e8a9cc1dac136fd6419afbcb7a4e94fb2f867b4`
- Captured application SHA: `919b0117f0230c44e829279085ef5201178b3289`
- Started: `2026-08-28T18:06:06.874484Z`
- Verified/completed: `2026-08-28T18:23:53.835901Z` / `2026-08-28T18:23:53.892346Z`
- Durable upload/read-back, ZIP, nonempty PostgreSQL dump, manifest/component hashes, retention, and lease release: PASS
- Restore: brand-new PostgreSQL `18.4 (Debian 18.4-1.pgdg13+1)` database `cropqc_disposable_run114`

## Root cause and correction evidence

- Deployed `919b0117` reproduced authenticated HTTP 500 on `/BinsRun?Section=Transfer`: room 8 / 9100 / GALA had 224 explicit lineage bins versus 92 authoritative bins.
- Actual Run #56 processed 132 untreated bins and then 1 MCP bin. The second treatment movement received the stale pre-run balance 225 instead of running balance 93 and rematerialized 132 untreated bins in segment #144.
- Candidate passes the running `canonicalPrevious` balance for every sequential line. Uncorrected reads return HTTP 200 and mark only that identity `Needs Review`; all lineage-consuming writes remain fail-closed.
- Pre-correction readiness: FAIL with code `TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY`, EBS / room 8 / 2026 / grower and lot 9100 / GALA / authoritative 92 / explicit 224 / difference 132.
- Correction dry run: State A `Ready`; target fingerprint `2bcee8f03f7de0cfbbe93962f98420dd8dca4ebf3ba095f4057d7b960ee31214`; protected fingerprint `0b4a3761f584cf0ea99c181b68bc9e96e611bdcbb81537a09ed2188832d92cf3`.
- Disposable apply changed only segment #144 `CurrentBins 132 -> 0`, `ConcurrencyVersion 12 -> 13`, and wrote one bounded audit. Inventory, Receipts, Actual Run/Bins Run entries, movements #203/#204, and remaining segments were unchanged.
- Rerun: State B `AlreadyApplied`, zero writes. Any guard mismatch is State C/refused.
- Post-correction readiness: PASS; 236 current identities, zero treatment-lineage blockers, zero inventory blockers, valid custody topology.
- Production correction applied during preparation: **NO**.

## Validation evidence

- Uncorrected candidate: Transfer and EBS Transfer routes HTTP 200, malformed identity visible/unavailable; default-off new custody UI and mutations blocked.
- Corrected restored route matrix: 24 authenticated core/operational/health routes PASS; enabled custody queues and routes PASS; no LINQ/Npgsql translation error or HTTP 500.
- Feature toggle: OFF preserves Internal Room Transfer and blocks Inter-Crew/Outside creation server-side; ON exposes the intended custody workflow.
- Disposable workflow: outside partial outbound/idempotency/reversal; inter-crew exact receipt, variance, admin review, pre/post-receipt reversal, treatment readiness, and no-negative-inventory assertions PASS. Existing full-suite coverage proves same-crew behavior, all cross-crew directions, McDougall outbound-only, authorization, antiforgery, concurrency, idempotency, processor shipment, loss, Actual Run revision, and lineage invariants.
- Pre-merge automated validation: focused 177/177 PASS; complete 1,551/1,551 PASS; JavaScript 31/31 PASS; PostgreSQL 18 route/workflow tests PASS; 836-object gate PASS; EF pending model NONE; format PASS; `git diff --check` PASS.
- Final merged-main validation and repeated fresh restored acceptance must be recorded in the immutable GitHub merge handoff before status changes to READY.

## Executable gates

- `dotnet CropQc.Web.dll --verify-schema=20260828033737_AddTransferCustodyWorkflow`
- `dotnet CropQc.Web.dll --verify-release-readiness`
- `dotnet CropQc.Web.dll --correct-treatment-lineage-segment-144` (dry run first; apply only with fresh live backup evidence and fingerprints)

## Stop conditions

- Unexpected release-candidate or live SHA; auto-deploy not Off; unverified backup; materially changed production data without a new restore/recheck.
- Segment #144 is neither exact State A (`Ready`) nor exact State B (`AlreadyApplied`), or target/protected fingerprints differ from fresh live values.
- Schema incompatibility, 836-object gate failure, migration-history change on the compatibility path, inventory/release-readiness blocker, or unexplained protected-data change.
- The default-off feature exposes or accepts new custody creation; an inconsistent identity remains selectable; any critical route returns HTTP 500.
- Requested/built/activated/live deployment SHA differs from the frozen release candidate.
- Any critical failure not resolved inside 15 minutes: disable Transfer Custody, roll back to `919b0117f0230c44e829279085ef5201178b3289`, reopen the PR, and end the release attempt.

## Exact future overnight sequence

1. Confirm the immutable release-candidate SHA, live SHA, authorization, maintenance Off, auto-deploy Off, feature Off, and zero production custody records.
2. Run live read-only baseline/readiness and compare with this rehearsal. If production materially changed, require a new restore/recheck before maintenance.
3. Take a fresh standard **PreDeployment** backup and require complete verification, retention, and lease release.
4. Capture fresh migration/protected fingerprints; run live schema/correction preflights.
5. Apply only the reviewed additive compatibility package if required; verify exact schema, 836-object gate, unchanged migration history, inventory readiness, and protected data.
6. Turn maintenance On only after every prior gate is green.
7. Dry-run segment #144 correction with the fresh backup and fingerprints; require exact State A `Ready` (or prove exact State B and make zero writes).
8. Apply once; require `132 -> 0`, version +1, one audit, no operational quantity/history change; rerun and require `AlreadyApplied`/zero writes.
9. Run `--verify-release-readiness`; require schema, inventory, lineage, topology, outside-master, and active-transfer PASS.
10. Deploy the exact release-candidate SHA with `TransferCustody__Enabled=false`; require requested, built, activated, and live SHA equality.
11. Verify `/health`, `/health/db`, `/health/master-data`, logs, and authenticated core routes: Dashboard, Receipts/detail/create shell, Daily QC/receipt QC, Field Samples, Rooms/detail, Inventory by Variety, Current Room Inventory/Reconciliation, Current Grower Lots, Run Planner, Actual Run/detail, Run Totals, Transfer and EBS Transfer, Internal Transfer, Processor Shipments, Master Data, Outside Warehouse read-only admin/history, and role/access pages.
12. On any core failure, roll back/reopen immediately. Do not enable the feature.
13. Enable `TransferCustody__Enabled=true`; verify Inter-Crew queues/details and Outside Warehouse workflow authorization/read-only surfaces without synthetic production movements.
14. On any new-feature failure, immediately set the feature Off. If unresolved within 15 minutes, roll back/reopen.
15. Recheck readiness, protected fingerprints, reporting, memory, DB connections, restarts, and repeating errors.
16. Turn maintenance Off only after every gate passes; leave auto-deploy Off.

## Rollback

1. Set `TransferCustody__Enabled=false` to stop new custody operations.
2. Deploy exact application SHA `919b0117f0230c44e829279085ef5201178b3289`.
3. Leave the additive transfer-custody schema and migration history in place; the old application was rehearsed against that shape.
4. Do not reverse correction #144 automatically. It removes only proven rematerialized lineage and preserves operational inventory/history.
5. Re-run health, 836-object schema gate, inventory readiness, and protected fingerprints. Never overwrite newer legitimate production activity with the rehearsal/predeployment backup merely to undo the code release.
