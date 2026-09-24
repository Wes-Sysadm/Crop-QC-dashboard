# PR #252 final release review

This review is for `codex/truck-receipt-reconciliation`, based on `c014528ebab82de8c9ce0e5ef0f5f5e920b5a8ae`. The [reviewed initial adoption plan](truck-receipt-initial-adoption.md) supersedes the earlier permanent-grandfathering proposal **for loads 1–15 only**. Normal creation-time persistence remains; this deliberate, audited release exception adopts 630 already-InTransit bins without another inventory movement. No merge, deployment or production configuration/schema/data operation was performed.

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
- `InterCrewTransfers.RequiresTruckReceipt` is the persisted creation-time workflow designation, assigned only in dispatch creation from the activation flag and qualifying route. The entity property remains init-only; the separate frozen release SQL may deliberately adopt only reviewed loads 1–15; matching, editing, completion and reopen cannot promote legacy loads. All new transfer mutation paths require the stored flag, candidate searches exclude legacy loads, and direct legacy reconciliation URLs explain the original receiving workflow. Status and current configuration never classify an existing load. A malformed legacy row with a receipt link fails closed rather than enabling legacy receiving.
- Switching the flag off pauses new reconciliation writes and evidence creation, while retaining read semantics and the stored mode of every existing load. Newly created transfers during the pause use legacy behavior, even after prior feature use. Legacy receiving remains available both on and off. Existing feature loads cannot use legacy receive/reverse/review paths. Reactivation changes neither cohort. Normal receiving and WP-side internal movement remain available. The pre-feature rollback check governs application compatibility only; it is no longer used to decide operational workflow mode.
- New web launcher: `sh /app/start-truck-receipt-web.sh`. The file is copied into the new Docker image; the default image entrypoint uses it. **Also set this exact command in the existing Render web service's Docker Command.** That external setting is essential: pre-feature images lack the file and fail to start instead of silently running old code. Do not remove it after first use. A future build retaining the file still needs compatibility review; this is not a cryptographic attestation of arbitrary source changes.
- The launcher runs `--verify-truck-receipt-schema` before opening the web listener. It checks migration evidence, mapped columns/table, PostgreSQL unique/nonunique indexes and restrictive FKs, and fails closed on missing schema/connectivity. No runtime schema mutation is added.
- Admin reopen explicitly checks treatment application source history, including reversed applications. Treatment/reversal can modify segments without transfer-movement rows, so movement checks alone were insufficient. This is conservative for equal timestamps and may require manual review rather than unsafe undo.
- Evidence receipts cannot claim original inventory through receipt-level treatment; use received room inventory and its original lineage. Ordinary receipt treatment is unchanged.
- Grower/Lot progress excludes transfer evidence from original fruit received totals. Receipt-provenance candidates exclude it as an original source. Receiving exports show each canonical receipt variety and its own quantity, retain primary-variety QC attribution, and append an explicit pending/completed transfer receipt status.
- Migration Down now rejects linked/completed/variety-only evidence as well as the feature flags. No additional tables or columns were needed for this review.

There is one feature migration: `20260923202144_AddTruckReceiptReconciliation`. Existing rows get false flags and null links/timestamps; no historical conversion or operational UPDATE is performed. All existing transfers initially default to legacy; only the separate reviewed adoption operation may flag loads 1–15; existing receipts remain normal, and existing inventory does not become transit inventory. The adoption revision adds guarded release SQL, not a schema migration; the existing fields support it. The former unique transfer/adjustment-type ledger index becomes nonunique because multiple allocations/compensations are legitimate. The nullable unique ReceivingReceiptId index plus one scalar receiving-receipt FK on each transfer enforce one-to-one matching. Receipt/profile uniqueness and restrictive FKs protect variety identity and history. Positive quantities and exact totals are validated transactionally by the application.

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

## 6. Production routes and reviewed initial loads

Canonical route codes are EBS, WP, DH and McDougall. Normal qualifying transfers created while enabled are flagged at creation. Existing records default to legacy until the deliberate reviewed adoption of IDs 1–15. All other historical transfers remain legacy.

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

Read-only production confirms all 15 total 630, already InTransit, source EBS/room 6, destination WP_DH without room assignment, created September 1 with August 27–29 load dates. All are Organic Honey Crisp / Apple / FP10; actual movement composition was inspected. Per-load header, dispatch and ledger fingerprints remain reviewed, no identity is superseded, and no placement/reversal evidence exists. No data-specific exception was found. Physical arrival and independently entered receiving remain operator checks. Shared BOL pairs 2/3, 7/8, 9/10 and 11/12 require deliberate load selection. See the [exact adoption guards and first receipt procedure](truck-receipt-initial-adoption.md); no production adoption has run.

## 7. Receiving, reconciliation and reopen findings

Normal grower/Truck receipts keep their ordinary ReceiptAdd, QC, editing and treatment paths and do not require a transfer. Transfer evidence must be explicitly selected when creating a receipt; an already inventory-owning normal receipt cannot be retrofitted by matching. Multi-variety reconciliation uses canonical FruitProfiles, with single-variety initial entry and additional lines in Match Transfer. Existing receipt-backed QC remains associated with its primary variety; this release does not invent per-variety QC on a consolidated truck. Exported additional varieties do not inherit the primary variety's QC readings.

Dispatch removes source availability; active dispatch less returns represents transit. Pending receipt entry and matching produce no destination inventory. Exact per-variety and total equality is checked again on completion. Original source identities, receipt provenance when known and treatment applications survive. Completion posts original allocations and statuses in one serializable transaction; retries do not duplicate entries; savepoint/transaction failures roll back inventory, statuses and audit. Pending returns restore only their original allocation, and source availability/version checks prevent reusing already controlled bins. Unique linkage and both record versions prevent double associations/completion.

Reopen succeeds only with untouched completed inventory, uses compensating ledger/movement entries and preserves audit/history. Non-admin users are rejected. Actual room movement, subsequent dispatch, room treatment, and treatment followed by reversal all block unsafe reopen. Receipt-level treatment of evidence is rejected. No quantity override bypass was introduced.

## 8. Exact eventual release sequence

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

## 9. Authenticated production smoke and first receipt (future release gates)

Before adoption with the feature OFF, verify exact SHA, persistent launcher, health/database health, ordinary receiving/QC, inventory and transfer/history screens plus production logs. The 15 still use legacy behavior at this stage; keep writers quiesced.

After adoption, all 15 must show Awaiting Receipt and retain their exact original quantities/allocations, no destination placement and no receiving link. While OFF, new reconciliation mutations remain paused. After activation, create only a genuine receiving Truck Receipt and inspect the manual compatible candidate list; adopted loads must be eligible, unreviewed legacy loads excluded. Do not create synthetic production receipts to smoke-test selection. Use read-only transfer contexts until a real receiving event exists.

Follow the [first genuine receipt validation procedure](truck-receipt-initial-adoption.md#first-genuine-receipt-validation) for one adopted load. Keep unrelated Bartlett repair/audit discrepancies untouched. An affected critical HTTP 500 or unproven quantity/provenance gate stops the release; after adoption recover only with compatible code or feature pause. No pre-252 downgrade.

## 10. Recovery

Before first use: pause/quiesce, execute the read-only pre-feature rollback command with the compatible candidate, and require exit 0. Leave schema in place. A deliberate guarded rollback to c014528 requires changing the service-level command under maintenance after the no-use proof. Recheck health and affected reads before restoring service.

The exact cutoff is the first committed flagged transfer or transfer-evidence receipt. In the planned release, the atomic adoption commit flags all 15 existing loads while the feature remains OFF and immediately prohibits pre-#252 code. No receipt, matching or completion is required. A prior flagged creation would cross the boundary earlier. Cancelled/unlinked/soft-deleted evidence still counts.

After first use: keep the launcher and schema, disable the flag under a bounded maintenance/configuration change, and deploy a reviewed **feature-aware** fix or compatible release. The first feature-aware candidate itself, with activation off, is the initial recovery baseline; c014528 is not. Disabling operations is not a data repair and does not undo legitimate transfers. Capture failed operation keys, versions, audit and accounting before a reviewed corrective action. Safe Admin reopen is a genuine business correction only, never an automatic software rollback.

An affected critical HTTP 500 is a stop/rollback-or-disable trigger. Unresolved production troubleshooting stops at 15 minutes. Whole-database restore is reserved for separately authorized catastrophic loss, with a plan to preserve/reconcile all legitimate activity since the backup. Never restore an older snapshot merely to undo this release.

## 11. Validation and review status

See [initial adoption validation](truck-receipt-initial-adoption.md#validation-and-baseline-gate) and the delivery report for the final counts and fingerprints. Schema is unchanged; the separate release SQL alone adopts the reviewed cohort. Normal creation-time persistence, manual selection, exact variety reconciliation and no-legacy-bypass protections remain covered.

The known historical lineage baseline test remains visible and unsuppressed. PR #252 stays Draft while the required all-pass gate is unmet. No production release clearance is claimed, no MSI is required, and actual Render/Linux startup plus authenticated production smoke remain release gates. Earlier old-code compatibility and schema/Blueprint evidence remain applicable to the unchanged schema/launcher/configuration.

Final adoption validation: **247 affected-area tests passed**, including 14 adoption cases using the actual SQL and authenticated adopted-load pages. Full suite: **1,935 passed, one known baseline failure, two optional skips (1,938 total)**. Restore/build, default-provider model check, formatting/diff and schema checks passed. The final incremental solution build has zero errors/warnings; earlier compilation emitted existing nullable warnings. The two unrelated optional skips are FruitProfileIdentityGuardPostgreSqlTests and ReceiptDateBaselineTests; all Truck Receipt/adoption PostgreSQL tests ran.
