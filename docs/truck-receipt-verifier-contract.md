# Truck Receipt deployment verifier contract

## Scope and cause

This correction changes read-only schema validation only. It changes no migration, model, inventory operation, audit, feature flag, deployment command, or release/adoption guard. The affected paths are the retained deployment verifier and the Truck Receipt schema/rollback CLI. Full-suite validation was explicitly requested for this release correction.

PR #252's migration `20260923202144_AddTruckReceiptReconciliation`, bounded PostgreSQL script, EF model and ledger entity agree. The old deployment verifier's `RequireUnique: true` was obsolete; the migration was intentional.

The required PostgreSQL index is:

```sql
CREATE INDEX "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType"
ON public."RoomInventoryAdjustments" USING btree
("InterCrewTransferId", "AdjustmentType")
WHERE "InterCrewTransferId" IS NOT NULL;
```

The key order is InterCrewTransferId, then AdjustmentType, both ascending with default null ordering, default operator classes and each column's collation. It has exactly two key columns, no included columns, no expressions, and is explicitly NON-UNIQUE. It must be valid, ready and live. The old definition was otherwise the same but UNIQUE. PostgreSQL renders the predicate as `("InterCrewTransferId" IS NOT NULL)`.

## Why repeated index keys are legitimate

A transfer can now have multiple canonical fruit allocations. Completion can write one destination credit per allocation, all sharing the same transfer ID and adjustment type. Reopening and completing again must retain compensating history rather than overwrite the earlier credits. Transit allocation changes also preserve distinct evidence. A uniqueness constraint on this pair rejects valid ledger history.

Removing that uniqueness does not make arbitrary duplicate ledger inserts safe. Ledger primary keys distinguish rows but do not independently prevent semantic double credits; InventoryOperationKey on the ledger is not claimed to be unique. The application protects the business operation using serializable reconciliation transactions/savepoints, transfer and receipt concurrency versions, eligible state and exact per-variety quantities, allocation identity and inventory invariants, atomic ledger/movement/audit/state writes, unique transfer operation keys and lineage movement operation keys, a unique receiving-receipt relationship and unique receipt/fruit-profile lines. Retry, multi-variety receive/reopen, stale-version and failure-rollback tests exercise these safeguards. Direct unguarded SQL remains outside that protection.

## Correction and regression coverage

Both schema gates share one exact ledger-index predicate. Other required columns, primary keys, indexes and foreign-key checks remain in force. The retained CLI argument stays supported; it selects the existing cumulative deployment contract, whose ledger entry now matches the merged application. No broad exception or verifier bypass was added.

Nineteen focused tests cover model shape; valid schema through both gates in a read-only transaction; old unique/missing/wrong-column/reversed-column/wrong-filter/unfiltered/included-column/extra-key/descending/expression/BRIN/nondefault-operator-class/nondefault-collation definitions; and continued enforcement of a separate unique index, primary key, required column, foreign key and unknown migration rejection. Malformed definitions exist only inside rolled-back disposable PostgreSQL transactions.

The operator-class negative test caught that PostgreSQL's per-column index display omits the operator class; explicit catalog checks now enforce operator class and collation too.

## Validation on 2026-09-25 UTC

- Restore/build pass (initial solution build: 63 existing warnings, zero errors; subsequent incremental build: 57 warnings, zero errors). No unrelated warning cleanup.
- Focused verifier tests: 19 passed, zero skipped/failed.
- Explicitly requested full suite: 1,967 passed, two optional skips, zero failed, 1,969 total. Its Truck Receipt subset contains 112 passing cases, including the 19 new cases.
- Optional skips: FruitProfileIdentityGuardPostgreSqlTests and ReceiptDateBaselineTests environment-specific PostgreSQL cases. Required Truck Receipt PostgreSQL, adoption and schema test connections were supplied.
- Changed-file formatting, `git diff --check`, repository-pinned EF 9.0.9 pending-model check and Render Blueprint validation pass. No model changes.
- Fresh backup #167 restore before #252: retained verifier rejects exactly the old unique ledger index. After the exact approved bounded #252 migration: retained and feature schema verifiers pass.
- Empty database: initialized from a schema-only export of the pre-feature backup, then migrated with the exact approved #252 script. Both schema gates pass in a read-only transaction. The full retained CLI additionally requires its ordinary default crop settings: its first invocation with writes prohibited attempted that settings initialization and failed; initializing those defaults locally and rerunning read-only passes. No production initialization or schema adjustment was performed. This fixture uses the reviewed bounded migration, not a claim that the entire historical EF migration chain can bootstrap an empty database.
- Rollback CLI: exit 0 on the unadopted restore, exit 2 on the previously adopted disposable rehearsal; both expected.
- Current production index catalog matches the intended contract. Corrected retained deployment, feature schema and pre-adoption rollback CLIs all pass with PostgreSQL `default_transaction_read_only=on`, startup schema creation/seeding disabled. All 11 protected historical fingerprints match the post-migration snapshot.
- SQL Server catalog support is retained, but no live SQL Server instance was exercised; production and release validation use PostgreSQL.

The approved schema was already applied in production and must not be reapplied for this fix. Adoption and feature activation remain separate guarded stages. After adoption commits, only #252-compatible application code is a valid rollback target.
