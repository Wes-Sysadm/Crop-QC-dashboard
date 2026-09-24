# PR #252 final release review

This review is for `codex/truck-receipt-reconciliation`, based on `c014528ebab82de8c9ce0e5ef0f5f5e920b5a8ae`. It supersedes the original always-on activation, conversion of existing open loads, and generic rollback guidance. The final grandfathering revision permanently retains legacy behavior for all transfers created while activation was off, including all existing production transfers. No merge, deployment, production configuration change, production migration, backup execution, or inventory repair was performed.

## 1. Exact rollback incompatibility

The additive schema does not make old code fail automatically. The old model ignores `Receipts.IsTransferReceipt`, `TransferCompletedAt`, `ReceiptVarietyLines`, `InterCrewTransfers.RequiresTruckReceipt` and `ReceivingReceiptId`. It knows only the original single-identity transfer header, not the active multi-allocation manifest or `TransitAllocationReturn` / `TransferReceiptReopen` ledger entries. It also lacks the new one-to-one completion, receipt exclusion and protected editing rules. Awaiting Receipt / Reconciliation Required / Reconciled are derived UI states; the underlying old InTransit/Received statuses still look familiar to it.

Actual **c014528 application services**, compiled from unchanged application source in an isolated checkout, were exercised against disposable PostgreSQL records produced by PR #252. Only a local probe test was added to that checkout. Results:

| Case | Observed old application behavior |
|---|---|
| Additive schema, ordinary legacy 70-bin load | Reads and receives correctly; destination becomes 70. |
| New flagged single-variety 70-bin load, no receipt | Old receive succeeds and credits 70 without creating/completing a receiving receipt. The new reconciliation state is broken. |
| Linked pending 70-bin receipt | Old receipt fallback reports 70 current receipt bins while destination ledger is zero. Old receive accepts **68**, credits 68 and sets ReceivedNeedsReview, leaving the linked receipt incomplete at 70. |
| Completed 70-bin receipt | Old receive refuses a second receive because transfer status is Received. This particular endpoint does not double-receive it. However, ordinary receipt edits can change Truck receipt to Lot sample and back. Both edits succeeded and destination ledger grew **70 → 140**, while the transfer remained Received. |
| Multi-variety 70+30 load | Old receive fails visibly with InterCrewDispatchAmountMismatch and rolls back. |
| Original 70 load with 10 returned | Old receive fails visibly with InterCrewDispatchAmountMismatch and rolls back. |

This is a combination of **silent inventory creation, reconciliation bypass, misleading receipt availability, and stranded operations**, not just a missing screen. The authoritative room ledger itself remains correct until a permitted old write changes it; not every old query or movement endpoint is unsafe in the same way. The duplicated destination quantity can then become available for further movement. Old soft-delete/edit paths also lack the new relationship protections. Existing version tokens prevent stale writes, but do not protect against a fresh old-code request that follows the wrong business rules. None of these failures requires a schema downgrade.

## 2. Risk reduction and rollback matrix

Full backward compatibility would require changing pre-252 binaries or disguising/deleting meaningful operational evidence. Neither is appropriate. The chosen outcome is explicit, guarded incompatibility, with a reversible pre-use validation period.

| Stage | Roll back to c014528 with additive schema? | Required handling |
|---|---|---|
| A: schema/new app, no new feature evidence | Yes, after quiescing writes and passing the read-only rollback gate. Ordinary legacy loads do not count as new feature use. | Leave schema intact; deliberate operator removal of the launcher guard is permitted only after this proof. |
| B: a feature-enabled cross-company load or evidence receipt exists, even without completion | No. A simple load can bypass reconciliation; edited loads can fail. | Keep compatible code; pause the feature or forward-fix. |
| C: linked, pending reconciliation | No. The old receive path can accept a mismatch without completing the receipt. | Keep compatible code and preserve the link/history. |
| D: completed reconciliation | No. Old receipt editing can recreate inventory. | Keep compatible code; no pre-feature downgrade or automatic data undo. |

`--verify-pre-truck-receipt-rollback` exits 0 only when **all** receipt flags/completion timestamps, transfer flags/links, variety lines and feature return/reopen ledger evidence are absent. Cancelled, unlinked and soft-deleted records still count. Run it with the candidate application while writes are quiesced; it is a point-in-time check, not a durable lock. It does not authorize a rollback on its own.

## 3. Code, schema and activation changes

- `TruckReceiptReconciliation__Enabled` defaults **false**. Before first use, normal receiving and untouched legacy transfer behavior continue. New receipt intent controls are hidden and new reconciliation writes are rejected server-side.
- `InterCrewTransfers.RequiresTruckReceipt` is the persisted creation-time workflow designation, assigned only in dispatch creation from the activation flag and qualifying route. The entity property is init-only; matching, editing, completion and reopen cannot promote legacy loads. All new transfer mutation paths require the stored flag, candidate searches exclude legacy loads, and direct legacy reconciliation URLs explain the original receiving workflow. Status and current configuration never classify an existing load. A malformed legacy row with a receipt link fails closed rather than enabling legacy receiving.
- Switching the flag off pauses new reconciliation writes and evidence creation, while retaining read semantics and the stored mode of every existing load. Newly created transfers during the pause use legacy behavior, even after prior feature use. Legacy receiving remains available both on and off. Existing feature loads cannot use legacy receive/reverse/review paths. Reactivation changes neither cohort. Normal receiving and WP-side internal movement remain available. The pre-feature rollback check governs application compatibility only; it is no longer used to decide operational workflow mode.
- New web launcher: `sh /app/start-truck-receipt-web.sh`. The file is copied into the new Docker image; the default image entrypoint uses it. **Also set this exact command in the existing Render web service's Docker Command.** That external setting is essential: pre-feature images lack the file and fail to start instead of silently running old code. Do not remove it after first use. A future build retaining the file still needs compatibility review; this is not a cryptographic attestation of arbitrary source changes.
- The launcher runs `--verify-truck-receipt-schema` before opening the web listener. It checks migration evidence, mapped columns/table, PostgreSQL unique/nonunique indexes and restrictive FKs, and fails closed on missing schema/connectivity. No runtime schema mutation is added.
- Admin reopen explicitly checks treatment application source history, including reversed applications. Treatment/reversal can modify segments without transfer-movement rows, so movement checks alone were insufficient. This is conservative for equal timestamps and may require manual review rather than unsafe undo.
- Evidence receipts cannot claim original inventory through receipt-level treatment; use received room inventory and its original lineage. Ordinary receipt treatment is unchanged.
- Grower/Lot progress excludes transfer evidence from original fruit received totals. Receipt-provenance candidates exclude it as an original source. Receiving exports show each canonical receipt variety and its own quantity, retain primary-variety QC attribution, and append an explicit pending/completed transfer receipt status.
- Migration Down now rejects linked/completed/variety-only evidence as well as the feature flags. No additional tables or columns were needed for this review.

There is one feature migration: `20260923202144_AddTruckReceiptReconciliation`. Existing rows get false flags and null links/timestamps; no historical conversion or operational UPDATE is performed. All existing transfers, open or completed, remain grandfathered; existing receipts remain normal, and existing inventory does not become transit inventory. This final revision changes no schema, SQL or migration: the existing false default provides the historical boundary. The former unique transfer/adjustment-type ledger index becomes nonunique because multiple allocations/compensations are legitimate. The nullable unique ReceivingReceiptId index plus one scalar receiving-receipt FK on each transfer enforce one-to-one matching. Receipt/profile uniqueness and restrictive FKs protect variety identity and history. Positive quantities and exact totals are validated transactionally by the application.

## 4. Migration rehearsals and tooling boundaries

Both PR-scoped migration rehearsals pass: a brand-new restore of verified production backup 163, and an empty database initialized from the unchanged c014528 model using EF's database-context schema script, followed by the exact checked-in feature SQL. The backup was independently rechecked for ZIP size/SHA-256, manifest/component hashes and valid nonempty dump. It was captured September 23 at 18:18:05 UTC, before the separately completed Bartlett repair; it is a recent rehearsal backup, **not** the fresh backup required for a future release.

The unused feature migration was reversed successfully on a disposable empty baseline and reapplied successfully. Down was rejected with feature data present. Schema reversal is therefore technically possible before use, but keeping additive schema is the recommended recovery path.

Two pre-existing tooling limitations must not be hidden:

1. Running the entire historical EF chain from an empty PostgreSQL database fails in `20260902140938_AddInventoryIdentityCorrections` because GrowerLots is absent. The feature migration is later and never executes in that failed path. Do not use full-chain `database update` as this release procedure. The empty-current-model plus feature migration rehearsal passes.
2. The repository's default-provider model/snapshot check passes. The same check with DATABASE_PROVIDER=PostgreSql reports differences on both this branch and unchanged c014528. The repository has a provider-specific historical snapshot, not a clean PostgreSQL migration chain. Production-schema correctness is proved by the bounded PostgreSQL DDL, schema checks and PostgreSQL workflow tests, not by claiming that command passes.

The restored-production path also must not blindly replay the full historical chain: prior rehearsal encountered an already-existing TotalDefectPercentageSnapshot column. Use only the reviewed feature SQL on the supported live pre-state.

## 5. Web/worker compatibility and live deployment configuration

Read-only Render inspection found:

| Service | Current live commit | Deployment | Auto-deploy |
|---|---|---|---|
| Web `srv-d8crvimgvqtc73b9rogg` | c014528 | dep-daq0fntg1s2s73dl2odg | Off |
| Backup cron `crn-d9gp1gm1a83c73f6pvk0` | c014528 | dep-daq09o7avr4c73f6d7rg | On, main commits |

The cron command is `dotnet CropQc.Web.dll --run-backup=scheduled`; it exits before serving web requests. Backups use pg_dump for the entire database, not a model-shaped inventory export. Its database reads for counts/schema/photo metadata do not interpret transfer state or select the new receipt fields. Its writes concern backup leases/run records/retention/notifications, not receipts, transfers, quantities or lineage. A new worker on the old schema may log a pending-migration warning; it does not apply the migration. Old workers continue dumping new tables/data without needing an updated entity model.

The real backup schema-manifest method passed against pre-feature schema in new code and completed feature data in new code. The actual old worker method also passed on all six compatibility scenarios. No backup upload or production worker run was initiated by these tests. Backup-service automated tests remain in the full suite.

Web and this backup cron do **not** require lockstep for inventory safety. Nonetheless, the Blueprint now records auto-deploy off for web and cron, and the release must set that policy on the existing services before merge. Repository YAML changes do not update current Render settings by themselves. Deploy schema, then web, then the same pinned worker release deliberately. No unrelated operational worker was found for this application. The new web launcher must not replace the cron's backup command.

The updated Blueprint validates with zero errors against [Render's published JSON schema](https://render.com/schema/render.yaml.json). Validation also exposed pre-existing unquoted boolean environment values and plural `disks` syntax. Those are corrected to string values and the supported singular `disk` object, retaining every environment value, disk name, mount and size; the staging template receives only those syntax corrections. No live Blueprint sync was run.

## 6. Production routes and open loads

Read-only production IDs/codes: EBS **1/EBS**, WP DH **2/DH**, McDougal **3/McDougall**, WP **4/WP**. Routing uses canonical warehouse codes and custody groups, not these numeric IDs or display labels. For qualifying transfers created while the flag is on, it applies exactly to 1→4, 1→2, 1→3, 4→1, 2→1, 3→1. WP/DH/McDougall internal pairs stay outside this requirement; their existing routing restrictions are preserved, not expanded.

Read-only production recheck on September 23 Pacific / September 24 UTC confirmed **15 open loads, 630 bins**. Production still has the pre-feature schema, so the marker column is not yet present. Grandfathering below is the result of the reviewed migration's false default, verified on a fresh restore, not a claim that production was migrated.

| Load | Source | Destination custody | Bins | Created UTC | Current status | Grandfathered |
|---|---|---|---:|---|---|---|
| 1 | EBS / Lamb Street 13 | WP_DH | 64 | 2026-09-01 15:35:21 | InTransit | Yes |
| 2 | EBS / Lamb Street 13 | WP_DH | 27 | 2026-09-01 16:16:26 | InTransit | Yes |
| 3 | EBS / Lamb Street 13 | WP_DH | 45 | 2026-09-01 16:17:31 | InTransit | Yes |
| 4 | EBS / Lamb Street 13 | WP_DH | 70 | 2026-09-01 16:19:33 | InTransit | Yes |
| 5 | EBS / Lamb Street 13 | WP_DH | 70 | 2026-09-01 16:20:43 | InTransit | Yes |
| 6 | EBS / Lamb Street 13 | WP_DH | 66 | 2026-09-01 16:22:08 | InTransit | Yes |
| 7 | EBS / Lamb Street 13 | WP_DH | 29 | 2026-09-01 16:23:15 | InTransit | Yes |
| 8 | EBS / Lamb Street 13 | WP_DH | 41 | 2026-09-01 16:26:16 | InTransit | Yes |
| 9 | EBS / Lamb Street 13 | WP_DH | 59 | 2026-09-01 16:27:24 | InTransit | Yes |
| 10 | EBS / Lamb Street 13 | WP_DH | 11 | 2026-09-01 16:30:25 | InTransit | Yes |
| 11 | EBS / Lamb Street 13 | WP_DH | 40 | 2026-09-01 16:32:33 | InTransit | Yes |
| 12 | EBS / Lamb Street 13 | WP_DH | 34 | 2026-09-01 16:33:39 | InTransit | Yes |
| 13 | EBS / Lamb Street 13 | WP_DH | 25 | 2026-09-01 16:34:32 | InTransit | Yes |
| 14 | EBS / Lamb Street 13 | WP_DH | 30 | 2026-09-01 16:35:37 | InTransit | Yes |
| 15 | EBS / Lamb Street 13 | WP_DH | 19 | 2026-09-01 16:37:18 | InTransit | Yes |

Every destination is the WP_DH custody group; no destination warehouse or room has been assigned. All 15 already have legacy `InTransit` status, null BinsReceived, and version 1. Activation leaves that status and existing behavior unchanged; it does not convert them to the new reconciliation workflow or require a Truck Receipt. Each source debit equals negative BinsLoaded, each dispatch lineage quantity equals BinsLoaded, and these loads have zero non-dispatch ledger rows and zero non-dispatch movement rows. Transfers 16–22 remain completed legacy internal history. Existing destination inventory is not removed or reclassified by activation.

**Operational review:** the loads were entered September 1 but LoadedAt is August 27–29. Remaining open for roughly four weeks from load date is potentially stale. The entries balance numerically, but database evidence does not establish physical truck location or whether ordinary receiving was independently entered elsewhere. Operations should review that aging under the existing legacy process; no closing, matching, repair or historical rewrite is included here. Existing 15 are never eligible for the new matching workflow, regardless of later activation or status changes.

## 7. Receiving, reconciliation and reopen findings

Normal grower/Truck receipts keep their ordinary ReceiptAdd, QC, editing and treatment paths and do not require a transfer. Transfer evidence must be explicitly selected when creating a receipt; an already inventory-owning normal receipt cannot be retrofitted by matching. Multi-variety reconciliation uses canonical FruitProfiles, with single-variety initial entry and additional lines in Match Transfer. Existing receipt-backed QC remains associated with its primary variety; this release does not invent per-variety QC on a consolidated truck. Exported additional varieties do not inherit the primary variety's QC readings.

Dispatch removes source availability; active dispatch less returns represents transit. Pending receipt entry and matching produce no destination inventory. Exact per-variety and total equality is checked again on completion. Original source identities, receipt provenance when known and treatment applications survive. Completion posts original allocations and statuses in one serializable transaction; retries do not duplicate entries; savepoint/transaction failures roll back inventory, statuses and audit. Pending returns restore only their original allocation, and source availability/version checks prevent reusing already controlled bins. Unique linkage and both record versions prevent double associations/completion.

Reopen succeeds only with untouched completed inventory, uses compensating ledger/movement entries and preserves audit/history. Non-admin users are rejected. Actual room movement, subsequent dispatch, room treatment, and treatment followed by reversal all block unsafe reopen. Receipt-level treatment of evidence is rejected. No quantity override bypass was introduced.

## 8. Exact eventual release sequence

1. Obtain explicit merge/release authorization and human review of the final PR head, these findings and the known baseline failures. Do not merge until step 3 has disabled auto-deploy. Record and freeze the exact merge SHA before the rehearsal in step 4; do not deploy an unreviewed later main.
2. Before the first production mutation/configuration/deployment, run and fully verify a **new** standard predeployment backup: durable upload, independent read-back, bytes/hash/archive/manifest/dump, retention and lease release. Record run/package/SHA and both live commits. Schedule outside a backup run and use a bounded maintenance window.
3. On the **existing service IDs**, turn backup cron auto-deploy off and confirm web remains off before merge. Leave the cron command unchanged. Quiesce operational writes. Do not apply the entire Blueprint blindly to create duplicate services. If the release is delayed after the policy change, refresh the backup immediately before schema/application deployment.
4. Refresh route/open-load diagnostics and protected receipt/transfer/ledger/lineage fingerprints. Check the prior schema/index state. Rehearse the frozen candidate and feature SQL on that fresh restore; stop for incompatible state.
5. Apply only `scripts/postgresql/truck-receipt-reconciliation.sql`; run the companion verification SQL and prove protected historical values unchanged. No inventory conversion or correction accompanies it.
6. Set the web's persistent Docker Command to `sh /app/start-truck-receipt-web.sh`, and flag `TruckReceiptReconciliation__Enabled=false`. Retain the existing schema predeploy check. Deploy the pinned compatible web SHA. Verify requested, built, activated and live SHA, launcher setting and `/health` / `/health/db`. Do not serve new web code before schema exists.
7. Perform the read-only authenticated checklist below with activation off. Preserve a tested compatible release artifact/SHA for post-use recovery. Deliberately deploy the same pinned backup worker build and confirm its backup command and disabled auto-deploy policy.
8. After schema, app, operations handoff and rollback guard are verified, activate the flag on the same pinned release while writes are quiesced. Verify all web instances use that configuration/commit; do not overlap old and feature-enabled web writers. Re-run read-only screens, then leave maintenance only after required gates pass.
9. Use a genuine qualifying transfer newly created while activation is on, followed by its actual receiving receipt, for the first live workflow. The existing 15 loads are excluded permanently. Operators remain responsible for both sides. Record reconciliation/accounting afterward. No fake fruit, invented transfer, automatic historic conversion or repair is allowed.

Configuration changes may restart/deploy Render services, so keep the candidate pinned and reverify the actual SHA each time.

## 9. Authenticated production smoke checklist (not performed in this review)

- Verify Render deployment SHA and runtime RENDER_GIT_COMMIT equal the frozen candidate; confirm maintenance/auto-deploy/launcher/flag values explicitly. `/health` and `/health/db` must return success.
- Sign in with a normal authorized operator. GET `/Receipts`, an existing ordinary receipt `/Receipts/{id}`, its edit screen without saving, its QC sample and receiving treatment views. Confirm existing quantity/identity and normal controls.
- GET `/BinsRun?Section=Transfer&TransferType=InterCrew`, each selected open transfer detail `/BinsRun/InterCrewTransfers/{id}`, and `/BinsRun/InterCrewTransfers/{id}/Reconciliation`. Confirm the 15 loads remain at the verified counts with legacy Receive controls, no reconciliation requirement and unchanged ledger/history. A direct reconciliation URL must explain that the load uses legacy receiving, without edit/match controls.
- With flag off, receipt intent controls are absent and existing new-workflow contexts are visibly paused. Newly created loads use legacy behavior. Do not submit production forms merely to test denial. With flag on, only qualifying loads created while on show Awaiting Receipt; the 15 grandfathered loads still use legacy Receive. The new receipt intent control appears, but candidates exclude all legacy loads. No transfer is automatically selected.
- GET source and destination room detail `/Rooms/{id}` for an affected load and Room 66; verify authoritative quantities, available inventory, original treatment lineage and unrelated lots. Review a completed internal McDougall→WP transfer and its original records. Do not change the Bartlett repair or the 26 audit discrepancies.
- Read affected received/Grower-Lot progress reports and the receiving export. Evidence must not increase original grower receipts or appear as original-source provenance; multi-variety evidence must show separate variety quantities and pending/completed status.
- Inspect production logs from deployment onward for missing-column/index errors, inventory invariant failures, repeated 500s, authorization failures, OOM/restarts or treatment/provenance errors. No release success while unexpected failures remain.
- First live operation: choose a genuine new qualifying load created after activation, with an actual Computech receipt, proven source allocation and no independently posted receiving inventory. Do not use transfers 1–15, including transfer 10: they are permanently legacy. Do not manufacture a test movement.
- Enter that actual receipt with Await transfer reconciliation, explicitly select its load, check identity/crop/organic status, actual total and each FruitProfile, and complete once only when exact. Read back: source debit unchanged; active transit cleared by completed status; destination credit equals actual allocations; transfer Received and receipt completed; one association; expected audit; no ReceiptAdd for the evidence; original source receipt/treatment history preserved. Do not exercise retries, edits or reopen on live inventory as synthetic tests.

## 10. Recovery

Before first use: pause/quiesce, execute the read-only pre-feature rollback command with the compatible candidate, and require exit 0. Leave schema in place. A deliberate guarded rollback to c014528 requires changing the service-level command under maintenance after the no-use proof. Recheck health and affected reads before restoring service.

The exact pre-feature rollback cutoff is the first committed creation of either a qualifying transfer with `RequiresTruckReceipt=true` or a receipt with `IsTransferReceipt=true` (which also creates variety evidence), whichever happens first. Merely enabling the flag is not that cutoff; matching and receiving completion are not required. A cancelled, unlinked or soft-deleted record still prohibits rollback. Existing and paused-period legacy loads alone do not cross the cutoff.

After first use: keep the launcher and schema, disable the flag under a bounded maintenance/configuration change, and deploy a reviewed **feature-aware** fix or compatible release. The first feature-aware candidate itself, with activation off, is the initial recovery baseline; c014528 is not. Disabling operations is not a data repair and does not undo legitimate transfers. Capture failed operation keys, versions, audit and accounting before a reviewed corrective action. Safe Admin reopen is a genuine business correction only, never an automatic software rollback.

An affected critical HTTP 500 is a stop/rollback-or-disable trigger. Unresolved production troubleshooting stops at 15 minutes. Whole-database restore is reserved for separately authorized catastrophic loss, with a plan to preserve/reconcile all legitimate activity since the backup. Never restore an older snapshot merely to undo this release.

## 11. Validation and review status

Final grandfathering validation: **233 affected-area tests passed**. The full suite has **1,921 passed, one pre-existing failure, two optional skips (1,924 total)**. Full-suite testing was explicitly requested. Three new lifecycle/HTTP regression cases replace the obsolete pause-blocks-legacy test: SQLite and PostgreSQL OFF → ON → OFF → ON lifecycles, plus an authenticated forged legacy match. The full run also caught a stale literal assertion expecting unquoted YAML booleans; it now requires the quoted false strings used by the reviewed Render configuration, preserving the privacy assertions. The subsequent full run has only the independently reproduced baseline failure.

Restore/build, default-provider EF model check, scoped whitespace verification and diff checks passed. The build has six existing nullable warnings in QcSummaryEmailComposerTests and no errors. A fresh disposable restore of verified backup run 163 rechecked all archive/component hashes, applied the unchanged feature SQL, and verified all 22 original transfers retain false/null workflow defaults. All 15 open restored loads remain legacy, total 630, with versions/history untouched. Read-only candidate schema and pre-feature rollback CLI gates both exit 0. The original SQL, schema objects, migration history and ledger/index semantics are unchanged by the grandfathering revision.

Earlier review evidence for empty-schema compatibility, used/unused Down behavior, actual old-code corruption probes and the Render Blueprint remains applicable because those migration/launcher/configuration files did not change. Fresh tests cover PostgreSQL workflow, pause/resume, new and legacy receiving, no conversion through direct new endpoints, inventory conservation, worker compatibility and authenticated HTTP.

The protected original-column fingerprints remain unchanged: 3,482 ledger rows, 624 segments, 635 movements, 22 transfers and 2,049 receipts. Added schema fields are checked separately for false/null defaults.

The known LegacyGrowerLotReconciliation test failure remains reproducible on unchanged main; its historical safety guard was not weakened. The two optional integration skips remain explicit. PostgreSQL workflows, actual old-code compatibility probes, authenticated TestServer requests, schema reversal/refusal, launcher missing-file rejection and CLI gates were tested locally. A Linux Docker image/start was not executed because the local Docker daemon is unavailable; actual Render launcher/configuration, Google-authenticated production smoke and the first real transaction remain release gates.

PR #252 remains **Draft**, not Ready for Review, because the requested full-suite gate still fails on the independently reproduced historical reconciliation test. No production release clearance is claimed. Resolve that baseline gate separately before advancing the PR under the requested all-pass rule. Leave it unmerged and do not activate production during this review. No WinForms changes or MSI rebuild are required.
