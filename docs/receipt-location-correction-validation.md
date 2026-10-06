# Receipt location correction validation

## Scope and status

Branch: `codex/receipt-location-correction`, based on current main `07db7c0405fd947f17d2897b32d1b751b3f62cbc`.

This change fixes receipt-location correction of current remaining stock after earlier inventory activity. The user clarified that the actual error was "The Receipt's current bins cannot be attributed exactly to its original room. No location correction was made." The earlier EF translation interpretation and its reproduction requirement are withdrawn; they are not release blockers. The remaining-stock regression and safety cases are covered, making this PR ready for human review. No production deployment or mutation is authorized or performed.

Affected area: receipt administrator location correction, canonical command transaction, its projection factory reads, receipt edit UI, correction evidence and error reporting. Affected writes remain executor-owned: receipt current location/version, paired inventory adjustments, current treatment segments, new lineage movements, override record, audit and command journal. Original received time, quantity and fruit identity, old ledger/movements/runs, treatment applications, Truck Receipt records and earlier audits must remain intact.

The full suite is explicitly requested by the user. Focused tests cover the changed workflow and direct receiving, canonical command, provenance, treatment, Truck Receipt, room-transfer and writer-coverage dependencies.

## Findings

- Entry point: `ReceiptsController.AdminInventoryOverride` from `/Receipts/{id}/Edit`, guarded by the existing Receipts Admin policy and antiforgery.
- `ReceiptInventoryOverrideService.CanonicalEditAsync` builds `CorrectReceiptLocation`; `InventoryReceiptAvailability.ReadAsync` supplies canonical receipt-attribution evidence and fingerprints.
- Exact message origin: the legacy branch of `ReceiptInventoryOverrideService.ApplyEditAsync`, under `locationChanged` and `counts.Transfers == 0 && counts.BinsRuns == 0 && counts.ActualRuns == 0`, rejects when `state.Balances.Count != 1 || state.Balances[0].CurrentBins <= 0 || state.Balances[0].WarehouseId != receipt.WarehouseId || state.Balances[0].RoomId != receipt.RoomId`. That guard conflates an original-room-only shape with safe current receipt attribution. Canonical mode dispatches before that legacy branch; this identifies the message's code origin without claiming the legacy branch executed with canonical mode ON.
- The old `InventoryCommandExecutor.CorrectReceiptLocationAsync` used three activity queries (lineage movement, run entry, depletion) to choose whether to relocate. Any subsequent activity selected a metadata-only fallback, even when authoritative stock remained. Thus a successful edit could move **zero** remaining bins. Existing baseline tests explicitly expected this behavior; those expectations are replaced.
- The old canonical relocation guard also required every allocation to belong to the original receiving room/warehouse. PR #263 replaces that whole-receipt/original-room restriction and the history-based fallback with exact surviving attribution at one selected current source. Transfer already uses current canonical positions and selected quantities without this receipt-history fallback.
- The canonical command was introduced by `142d15d` during Phase 3. A legacy metadata-only fallback also exists in the flag-OFF service. The production-ON fix does not reactivate that legacy writer.
- The new command keeps scalar SQL filters, bounded receipt/room evidence and canonical resolver semantics. No query-translation investigation is required for the clarified business error.

## Corrected behavior

1. Re-read canonical receipt state within the executor's PostgreSQL Serializable transaction and require the reviewed receipt version/fingerprint.
2. Require exact surviving receipt provenance. Same-lot inventory cannot substitute for receipt ownership.
3. Resolve a single selected current source room. A sole room can be selected automatically; split rooms require an explicit source. Missing/depleted source blocks instead of fabricating stock or changing metadata alone.
4. Validate active warehouses/rooms and unsealed source/destination. Keep identity, original receipt quantity and receiving time separate from this operation.
5. Normalize only resolver-proven stale evidence using the existing audited engine. Validate destination evidence before merging.
6. Move exactly the selected room's receipt-attributed treatment slices. One balanced ledger pair records physical relocation; separate lineage movements retain treatment state, signature, application links and receipt ownership. Destination projection reads are preloaded once for all slices.
7. Persist original/current receipt snapshots, source and destination, relocated quantity, actor, reason, time, override and canonical journal atomically. The receipt's current location label is updated; its earlier location remains in immutable correction snapshots and the movements/ledger, rather than rewriting prior evidence.
8. Reconcile physical totals, receipt-attributed room/custody totals and unselected allocation slices before commit. Replays remain idempotent.

The 100-bin acceptance case is exercised through normal depletion/transfer/correction services: receive 100 in A, consume 30, transfer 20 to C, select A, relocate 50 to B. Final A=0, B=50, C=20; 30 stay consumed. Historical movement/run/transfer/depletion records compare identically before and after correction.

The exact regression is `ReceiptLocationCorrectionTests.Partial_consumption_and_transfer_move_only_selected_fifty_and_preserve_all_history`. It first proves a split receipt blocks without explicit source selection, then selects A and asserts successful correction, 70 authoritative bins before/after, the 50-bin correction audit, unchanged historical records and idempotent retry. Companion tests retain genuine ambiguous-provenance blocking, fully depleted no-write behavior, stale preview rejection and transaction-time concurrency rollback. Attribution errors in the canonical path remain for genuinely unprovable ownership; historical activity alone no longer prevents correction.

## UI and error handling

The existing administrator review now distinguishes location correction from fruit identity correction, offers an explicit current-source selection, shows remaining receipt bins and treatment states, labels the destination and states the quantity relocating. A dedicated button opens this review even when the desired destination is the receipt's existing metadata room. Split allocations cannot silently collapse. Zero-current stock blocks.

Stale submissions receive a refresh instruction. Unexpected InvalidOperationException during canonical location execution is logged with the full exception and receipt/operation identifiers after rollback, while the user receives a safe business message. An unconfirmed post-commit read-back directs the operator to receipt history instead of falsely claiming rollback; retry uses the original idempotent command. Omitted source-room intent remains omitted in serialized old-style requests to preserve their replay identity. Existing admin/antiforgery protections remain; a real PostgreSQL HTTP test exercises GET, forbidden POST, missing-antiforgery POST, successful POST and audit read-back.

## Regression evidence

- Focused suite: **79 passed, 0 failed, 0 skipped**.
- Final business-guard verification: **16 receipt-location tests passed, 0 failed, 0 skipped**, including the reported remaining-stock scenario and all retained safety cases. No application or test code changed during this clarification.
- Final candidate full suite: **2,228 passed, 1 failed, 0 skipped (2,229 total)** in the sandbox. The sole failure was the unchanged backup snapshot test being unable to launch `pg_dump`; it reproduced in the sandbox, then passed unchanged in the host environment (**1 passed, 0 failed, 0 skipped**). All 2,229 tests have passing evidence across the full run and isolated rerun, but the full invocation itself was not a clean pass. The earlier implementation full run passed 2,228/2,228 before the final read-back regression was added.
- Evidence acceptance: `AGENTS.md` and `docs/change-scoped-testing-standard.md` require affected-area validation and disclosure of missing disposable tools; they do not require every result to come from one clean full-suite invocation. The unchanged provider test's separately passing local-host run resolves the sandbox launch limitation. It is not a receipt-location correctness failure. This clarification changes documentation only, so existing build/model/format and full-suite evidence remains applicable without another broad run.
- Restore and build: passed. Final candidate solution build: 0 errors and 58 existing repository warnings.
- EF model: no pending changes; no migration/schema change.
- Changed-file formatting and diff checks: passed.
- Writer inventory: 32 classified workflows; zero ordinary bypasses. Updated hashes only for reviewed changed canonical infrastructure and the existing dispatcher; no new bypass exemptions.

Coverage includes full/partial receipt quantity; partial transfers; depleted and split cases; untreated, treated and mixed slices; exact ownership and ambiguous pools; stale preview and transaction-time concurrency; audits and conservation; historical movement preservation; completed Truck Receipt linkage; selectors; direct-writer guard; 100 versus 100,000 bins with constant query count; transfer/reversal regressions; and rollback at Movement, OperationAudit and BeforeCommit boundaries.

A local browser check of actual fixture-rendered HTML verified opening the review, selecting the destination, source/quantity display and layout, with no client console warnings/errors. This static preview did not submit; real PostgreSQL HTTP tests covered POST/read-back. Mobile/responsive variants were not tested. The withdrawn EF-exception reproduction requirement is not outstanding acceptance coverage.

## Read-only production investigation

Earlier read-only production log searches followed the now-corrected EF interpretation and found no matching exception; a wider search timed out. These searches do not establish a query defect. No further EF investigation is needed for this acceptance requirement.

Read-only production audit queries found no `CanonicalReceiptLocationCorrected` entries since October 3. The latest `ReceiptInventoryOverrides` location corrections are receipts 1614 (September 8, two adjustments), 1581 (September 7, two adjustments), 1484, 1443, 1421 and 1386 (September 2–4, no inventory adjustments under the legacy policy). These historical metadata-only records are not proof of corruption and are not repair targets. No production repair was identified or performed; the specific reported receipt is still needed to determine whether it needs further investigation.

No backup, migration, deployment, inventory update, Gmail action or email was performed. No WinForms change or installer is involved.
