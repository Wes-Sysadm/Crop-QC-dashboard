# Reviewed initial Truck Receipt adoption — loads 1–15

This plan supersedes the prior proposal to permanently grandfather these 15 loads. It does **not** authorize production execution. PR #252 is being prepared only; no merge, deployment, production configuration/migration/adoption/repair is performed here.

## Mechanism and scope

Normal `RequiresTruckReceipt` creation-time persistence and its init-only C# property remain unchanged. The application gains no conversion endpoint, button, automatic migration conversion or status/flag-based reclassification. Unreviewed historical transfers and loads created with activation OFF remain legacy. Only the separate reviewed release SQL can adopt IDs 1–15.

- `scripts/postgresql/preflight-truck-receipt-initial-adoption.sql`: repeatable-read, explicitly READ ONLY, applies nothing; it works on the pre-feature schema as well as the additive schema.
- `scripts/postgresql/apply-truck-receipt-initial-adoption.sql`: manual psql entry point requiring `actor_user_id`, serializable isolation, 15-second lock timeout, 60-second statement timeout and quiesced writers.
- `scripts/postgresql/truck-receipt-initial-adoption-core.sql`: shared frozen guards, used by both wrappers and integration tests. Direct invocation defaults to validation, not apply. No application startup path calls it.

**No additional schema is required.** Each transfer changes only `RequiresTruckReceipt: false → true` and `ConcurrencyVersion: 1 → 2`. The version advance invalidates stale submissions and follows the project's audited correction pattern. Each receives one `AdoptTruckReceiptWorkflow` AuditLog containing the full before/after row, authorized operator and execution timestamp; key `truck-receipt-initial-adoption-20260924:load:<ID>`. No receipt, receipt number, destination, quantity, allocation or movement is created or changed. There is no UpdatedAt field on InterCrewTransfer to modify.

The 15-row operation is atomic. It validates **every load before any UPDATE**. Each UPDATE must affect exactly one row; the total must be 15. Postconditions/fingerprint mismatch raises an exception and rolls back the complete operation and all audits. A repeated invocation fails safely with specific already-adopted/audit/version exceptions, even if only part of the batch appears adopted; it never silently adopts the remainder. Do not edit guards to force success.

## Exact guards

The checked-in VALUES manifest pins IDs and individual bin counts, full original-header MD5, full linked-movement MD5 and full linked-ledger MD5. UTC timestamp rendering is fixed before comparing. The full header guard covers operation key, creation/load timestamps, source inventory adjustment, source identity, original receipt, BOL/notes, actors, version and all receiving/reversal/review fields. Only additive feature fields are excluded from that original-header hash and are separately required false/null.

For each ID, require its reviewed count; source warehouse **1/EBS**, active room **6/Lamb Street 13**; destination custody **WP_DH**, null destination warehouse/room; **InTransit**, version **1**, null received quantity/time and reversal time; stored flag false and no receiving-receipt link; no previous adoption audit. Source warehouse/room/profile must remain active.

Require the exact reviewed dispatch ledger rows and negative per-load total, all at warehouse 1 / room 6 with InterCrewTransferDispatch type; exact reviewed dispatch movement rows and positive per-load total; no placement destination, reversal reference, non-dispatch movement or reversal linked from any other movement. A changed or additional row fails even if aggregate quantities happen to balance.

Each allocated segment must still exist at the original source, have the unchanged movement identity and treatment signature, header crop/GrowerLot/lot, and FruitProfile **10**. That canonical profile must still be active Apple / Organic Honey Crisp / ORHC / Organic / IsOrganic=true. No active completed identity correction may supersede its crop/GrowerLot/FruitProfile. A mismatched variety allocation therefore fails independently of total bins.

Before/after protection covers every ledger row, movement, lineage segment/application link, receipt, variety line, other transfer, and existing audit row. For IDs 1–15 only flag/version are excluded from the transfer invariant. Exactly 15 new adoption audits are required. Locks cover all queried identity, inventory, receipt, transfer and audit tables during apply.

## Production classification and variety breakdown

Read-only recheck: all **15 / 630 bins** meet the currently inspectable reviewed data guards. No load-specific exception was found. Production has not yet received the additive columns; false/null values must be rechecked after schema deployment before adoption. The exact script also passed preflight on the fresh restored pre-feature database.

Every load originates at EBS / Lamb Street 13, targets WP_DH custody without an assigned destination room, has status InTransit and version 1, and was created September 1, 2026 UTC. Load dates are August 27–29. **Every actual dispatch allocation was inspected**; all 15 are single-variety Organic Honey Crisp, not assumed so from the header.

| Load | Bins | Commodity | Variety / FruitProfile | Variety bins | Lot / GrowerLot | BOL (not Truck Receipt) |
|---|---:|---|---|---:|---|---|
| 1 | 64 | Apple | Organic Honey Crisp / ORHC / 10 | 64 | 9540 / 234 | #26502 |
| 2 | 27 | Apple | Organic Honey Crisp / ORHC / 10 | 27 | 9820 / 250 | #26528 |
| 3 | 45 | Apple | Organic Honey Crisp / ORHC / 10 | 45 | 9401 / 588 | #26528 |
| 4 | 70 | Apple | Organic Honey Crisp / ORHC / 10 | 70 | 9820 / 250 | #26467 |
| 5 | 70 | Apple | Organic Honey Crisp / ORHC / 10 | 70 | 3311 / 526 | #81652 |
| 6 | 66 | Apple | Organic Honey Crisp / ORHC / 10 | 66 | 9691 / 643 | #26507 |
| 7 | 29 | Apple | Organic Honey Crisp / ORHC / 10 | 29 | 9691 / 643 | #26469 |
| 8 | 41 | Apple | Organic Honey Crisp / ORHC / 10 | 41 | 3311 / 526 | #26469 |
| 9 | 59 | Apple | Organic Honey Crisp / ORHC / 10 | 59 | 9541 / 623 | #81654 |
| 10 | 11 | Apple | Organic Honey Crisp / ORHC / 10 | 11 | 9451 / 243 | #81654 |
| 11 | 40 | Apple | Organic Honey Crisp / ORHC / 10 | 40 | 3241 / 520 | #4505 |
| 12 | 34 | Apple | Organic Honey Crisp / ORHC / 10 | 34 | 9691 / 643 | #4505 |
| 13 | 25 | Apple | Organic Honey Crisp / ORHC / 10 | 25 | 9401 / 588 | #4243 |
| 14 | 30 | Apple | Organic Honey Crisp / ORHC / 10 | 30 | 9820 / 250 | #4244 |
| 15 | 19 | Apple | Organic Honey Crisp / ORHC / 10 | 19 | 3311 / 526 | #4245 |

These allocations total **630 Organic Honey Crisp bins**, with one original dispatch movement per load (269–283). All original source identities and accounting balance. Adoption does not subtract EBS inventory again, add an InTransit movement, or credit destination inventory.

Physical arrival has not been verified. The old dates merit operational review, and BOL pairs 2/3, 7/8, 9/10 and 11/12 share a value. A shared BOL is not a unique transfer or a receiving Truck Receipt number. Operators must verify the actual load/receipt relationship and any independently entered ordinary receiving inventory. Never invent a number, auto-match by BOL/count/date, or combine transfers to get around one-transfer/one-receipt rules. Data guards establish ledger transit state, not physical truck location. If business evidence contradicts it, stop adoption and report the affected load rather than loosen guards.

## Release sequence

1. Resolve the required baseline test gate and obtain human review and explicit merge/release/adoption authorization. Keep PR Draft until the required gate passes.
2. Run and independently verify a **fresh** production predeployment backup (durable upload/read-back, size, SHA-256, archive, manifest/dump, retention and lease release). Record both live commits. Do not use the rehearsal archive as the eventual release backup.
3. Disable auto-deploy on the existing backup cron and verify web auto-deploy remains off **before merge**. Keep the backup command unchanged. Freeze the reviewed merge SHA and rehearse that exact candidate on the fresh backup. Quiesce all transfer/receiving and related inventory writers before the bounded execution window.
4. Apply only the reviewed bounded feature schema SQL; run schema verification and protected-history fingerprints. The schema itself defaults every existing transfer to legacy and performs no adoption.
5. Set the persistent web Docker Command `sh /app/start-truck-receipt-web.sh`; deploy the exact compatible candidate with `TruckReceiptReconciliation__Enabled=false`. Retain the existing predeploy check. Confirm actual live SHA, health/database health and the compatible recovery artifact. Deliberately update the backup worker when appropriate; inventory correctness does not require lockstep.
6. Perform authenticated production smoke with the flag OFF and writers still quiesced. Ordinary receiving/history must remain correct; before adoption these 15 still appear legacy. Inspect logs. No fake receipt or transfer.
7. Run `preflight-truck-receipt-initial-adoption.sql`, review every load against the frozen conditions and confirm operators understand the physical/receipt status. Recheck no-use rollback eligibility before committing adoption. Any exception stops the entire batch; never loosen a predicate.
8. Execute `apply-truck-receipt-initial-adoption.sql` with an authorized active operator ID. It repeats all guards under serializable isolation/table locks, then updates exactly 15 rows and writes exactly 15 audits atomically. **Successful commit crosses the pre-#252 rollback boundary, even though the feature is OFF.**
9. Verify all 15 flags true, versions 2, InTransit, exact per-load counts/630 total, null receiving links/destination locations, and original allocations/history unchanged. Check per-load before/after audit JSON and all protected fingerprints; no receipts or movement created. Read-only rollback gate must now return exit 2.
10. Enable the feature consistently on all compatible web instances while writers remain quiesced. Reconfirm the pinned SHA, launcher and flag after any configuration-triggered restart. Inspect Match Transfer and transfer contexts read-only: adopted loads are Awaiting Receipt and manual compatible candidates, never automatically linked; source/destination availability is unchanged.
11. Re-enable normal operations only after all gates pass. The other historical transfers remain legacy; future qualifying transfers created while enabled use the persisted new workflow. A later pause never reclassifies adopted or newly flagged loads.
12. Use an actual Computech Truck Receipt to reconcile one of these adopted loads, following the first-live procedure below. No synthetic production transaction, automatic matching or additional repair.

## First genuine receipt validation

1. Identify one adopted load actually being received and the real Computech number. Verify physical quantity, Organic Honey Crisp / FP10, crop 2026, source lot/lineage and intended receiving room. Confirm no ordinary receipt has independently credited the same fruit. Resolve shared-BOL ambiguity with the sending/receiving operators; no invented identifier.
2. In normal Receiving, create a Truck receipt with the actual number and **Await transfer reconciliation**. This is evidence only and must produce no ReceiptAdd or destination availability.
3. Open Receipt → Match Transfer. Confirm correct custody/crop/variety; manually select the specific Crop QC load. Candidates show load ID, source, destination group, load date, count, variety breakdown and BOL. No automatic selection or quantity/date-based match.
4. Reconcile every canonical variety. For load 4, a 68-bin receipt against 70 must remain Reconciliation Required/InTransit with zero destination credit. Equal totals with wrong varieties also fail. Correct the actual underlying receipt or permitted transfer allocation with its audit/reason; no override. Do not manufacture a discrepancy to test production.
5. Complete only when the real receipt and all allocation quantities match exactly. Read back one association, receipt completion and transfer Received, destination credit equal to original allocations, source dispatch unchanged, original grower/lot/organic/treatment provenance retained, no evidence ReceiptAdd, and expected match/completion audits.
6. Confirm transit availability clears only because completion occurred, destination becomes available normally, other 14 loads retain their actual states, ordinary inventory is unchanged, and production logs contain no new unexpected errors. Do not exercise artificial retry, reopen, cancellation or transfers on production.

## Rollback and feature pause

Before adoption, pre-#252 rollback may be allowed only with quiesced writes and an exit-0 `--verify-pre-truck-receipt-rollback`; leave additive schema intact. Flag enablement alone is not first use.

The commit adopting these 15 loads sets `RequiresTruckReceipt=true` and crosses the boundary immediately, **while the flag is still OFF**. If any new flagged transfer or transfer-evidence receipt was committed earlier, that earlier commit already crossed it. Matching/receipt completion is unnecessary for incompatibility. Cancel/unlink/soft-delete cannot reset the boundary.

After adoption retain compatible #252-era code and the persistent launcher; recover through feature pause, a reviewed compatible release or forward-fix. Disabling the feature leaves adopted loads flagged and InTransit while reconciliation mutations pause. Re-enabling resumes them without reclassification. Ordinary legacy loads are unaffected. Never unset the adopted flags or restore an old database snapshot as software rollback.

## Validation and baseline gate

For a repeatable local rehearsal, restore a new disposable copy of the recent verified backup, run the preflight wrapper, and apply only the feature schema SQL. Set `CROPQC_TRUCK_ADOPTION_TEST_CONNECTION` to that **unadopted** local restore; set `CROPQC_TRUCK_RECEIPT_TEST_CONNECTION` to the same disposable database for the ordinary workflow tests, and `CROPQC_TRUCK_PREFEATURE_TEST_CONNECTION` to a separate pre-feature disposable database for worker compatibility. Run `dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --filter FullyQualifiedName~TruckReceipt`. The adoption cases use serializable transactions and roll back every synthetic record and adoption. Only after the tests finish should the manual apply wrapper be committed locally to prove the release entry point. Once that local commit occurs, repeat adoption tests require another fresh unadopted restore; do not reset flags or remove audits to reuse it.

The adoption integration suite runs the **actual checked-in SQL** against a fresh production-shaped PostgreSQL restore. It tests all 15/630, unchanged inventory/receipts/history, 15 audited versioned updates, manual candidate/UI visibility, 68-vs-70 rejection, equal-total/wrong-variety rejection, exact completion, pause/resume, rollback-boundary detection, preserved non-adopted legacy loads, safe repeat refusal and thirteen adverse guard cases. The ordinary lifecycle and forged-user-match tests continue proving that application code cannot adopt arbitrary legacy loads.

The known baseline failure was investigated without altering production logic. The proven-untreated historical backfill path calls GetOrCreateSegmentAsync while a positive stale destination identity still exists; the shared destination safety guard rejects it before normalization can complete. Changing this path affects historical identity consolidation and audit/movement construction, beyond a workflow-flag adoption. It should receive a separate, scoped correction with provenance tests; no guard was weakened or test suppressed here. Keep Draft while the required gate remains unmet. Final counts and before/after fingerprints are recorded in the delivery report.

## Final validation evidence

Final adoption validation: **247 affected-area tests passed**, including 14 adoption cases using the actual SQL and authenticated adopted-load pages. Full suite: **1,935 passed, one known baseline failure, two optional skips (1,938 total)**. Restore/build, default-provider model check, formatting/diff and schema checks passed. The final incremental solution build has zero errors/warnings; earlier compilation emitted existing nullable warnings. The two unrelated optional skips are FruitProfileIdentityGuardPostgreSqlTests and ReceiptDateBaselineTests; all Truck Receipt/adoption PostgreSQL tests ran.

A fresh disposable restore of verified backup run 163 (captured September 23 at 18:18:05 UTC, c014528, 13,901,101 bytes, SHA-256 `082d27f2c746ade8bdc7b16e7e40067bd837c7273280886fd1c797dfad621b3c`) reverified package/component hashes and applied the unchanged feature schema. After rollback-scoped workflow tests, the actual manual psql entry point was committed on that local restore: exactly 15 flags false→true, versions 1→2 and 15 audits; all 630 bins stayed InTransit. No receipt/receipt number, ledger movement, lineage or destination inventory was created by adoption. A repeated entry-point invocation exited 3 safely with unchanged state/audits. Post-adoption schema CLI exits 0 and pre-feature rollback CLI exits 2. This is a newly restored recent verified archive, not a new production backup or production adoption.

Ten protected before/after fingerprints matched. Transfer fingerprints exclude only the intended flag/version fields for IDs 1–15; existing audit fingerprints exclude only the new batch audit keys. Full audit before/after JSON was compared and only those two transfer fields changed. Timestamps are normalized to UTC, so do not compare these hash strings with earlier reports computed in a different session time zone.

| Protected area | Rows before = after | MD5 before = after |
|---|---:|---|
| ledger | 3,482 | `92e89aa8ed8057682cf869803db06834` |
| segments | 624 | `dfae924cb07555b35140494e07f74958` |
| movements | 635 | `cb1ea919fb54f8e4d30b5bb5c9fc2224` |
| applications | 55 | `0030d4af20300aa2b788bd649b8104ca` |
| receipts | 2,049 | `d6b47af5429f1e7c6c147b226de4196f` |
| receiptLines | 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| protectedTransferFields | 22 | `460dffd8e16de9b782c0ebb51fbcb6fe` |
| existingAudit | 144,789 | `9bcb29011540910fd6d07848268693ce` |
| rooms | 70 | `24f38f7841c0e638ca876157e5f955b5` |
| warehouses | 4 | `d2f74e2c82eb164355b7c58d323cf7b3` |
