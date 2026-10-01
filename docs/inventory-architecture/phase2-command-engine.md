# Phase 2 — transactional canonical inventory command engine

Status: **prepared implementation scope; engine not implemented by this preparation commit**. Phase 1 PR #258 was accepted and merged at `1380f68030d4faa4c8a79be107a8a290e73f73ac`. This draft starts from that merged main, not a stacked branch. Implementation belongs in this PR; operational caller migration belongs in Phase 3.

## Primary acceptance case

The operator is waiting to record a genuine 172-bin Bartlett lot 1372 run. The write engine must execute one business command consuming **68 from WP-4 and 104 from WP-7**. The recreated fixture starts with WP-4 physical 68 and WP-7 physical 1,122. WP-7 raw projections are 1,568: **324 duplicate + 122 already consumed = 446 historical excess**. Those 446 are never physical inventory or new consumption.

Expected committed state: WP-4 physical **0**, WP-7 physical **1,018**, and one 172-bin run/consumption result. Combined authority is **1,190 before → 1,018 after**, a deduction of exactly **172**. Current treatment quantities must reconcile to those remaining physical balances. Preserve the original receipts, prior run records, movements, treatment applications, business dates and actors. A failure after either source is processed must leave **both sources, all projections, all audits and the run unchanged**.

This is a disposable test, not a production run or manual SQL repair. Fixture IDs may label evidence; rules must not branch on lot, room, IDs or these quantities.

## Exact implementation scope

| Work item | Implementation boundary | Required proof |
|---|---|---|
| 1. Transactional executor | Shared command contract in `CropQc.Shared/Inventory`; EF executor in `CropQc.Data/Inventory`. One outer PostgreSQL Serializable transaction across all source/destination positions and business parent/history changes. No helper owns a separately committed normalization transaction. | Multi-source success; a failure between sources leaves zero committed effects. |
| 2. Inside-transaction revalidation | Resolve the Phase 1 canonical contract again inside the write transaction; validate identity, custody, selected treatment, quantity, receipt requirements and expected fingerprint/versions. Caller provides business intent, not replacement quantity arithmetic. | Changed ledger, projection, treatment or identity rejects stale intent; missing-row predicates are protected as well as segment versions. |
| 3. Deterministic normalization plans | Separate pure planner from EF application. Convert only proven evidence into stable, ordered row plans with before/after versions, algorithm, fingerprint and evidence IDs. A Phase 1 aggregate candidate with `ExactRowAllocationProven=false` cannot become arbitrary per-receipt trimming. | WP-7's proof supports one untreated pool while receipt allocation remains unknown. Preserve that uncertainty; exact-receipt operations remain blocked. Unsupported/mixed evidence produces no writes. |
| 4. Atomic audit + normalization + operation | Stage normalization, operation parent, authoritative ledger/custody changes, lineage movement and audit in the same transaction. Record original projection values and proof alongside resulting current representation. Stage external notifications only for post-commit delivery. | Audit failure or operation/history failure rolls back every staged effect; conservation failure also rolls back. No normalization-only public write route. |
| 5. Idempotency | Durable operation key and canonical complete-intent hash, including source/destination identities, all quantities, effective dates, treatment/receipt requirements and business parent intent. Retain a durable committed result. Reuse with different intent must fail. | Same-key retry returns the same result without new ledger rows/audits; conflicting payload fails; an uncertain commit can be safely retried. |
| 6. Concurrency protection | Serializable predicate protection plus existing EF version tokens and operation-key uniqueness. Bounded retry applies to the **whole command** using a fresh context/transaction, or returns a typed refresh/retry result. Revalidation runs on every attempt. | Two real PostgreSQL connections, controlled barriers: simultaneous consumers, transfer/dump, treatment/transfer, correction/edit and import/move. No double consumption or partial reconciliation. |
| 7. Projection lifecycle | Canonical factory for current treatment projections, distinct from retained historical evidence. Explicitly retire/supersede stale representation; do not revive a historical row by matching its key. Reversal creates/credits a valid current epoch using original movement evidence. | Depletion, return/reopen, receipt correction and re-entry preserve history without recreating source stock. Multiple genuine treatments/receipts remain representable. |
| 8. No negative inventory | Enforce `requested <= canonical available` and authoritative nonnegative postconditions for every affected identity. Block legacy negative positions; no admin override in this command policy. | One-bin overdraw, combined-source shortfall and competing final-bin consumers leave inventory unchanged or produce one valid winner. Never clamp a negative balance and call it valid. |
| 9. Rollback and recovery | Own transaction disposal, rollback, tracked-state cleanup and retry classification. No caller receives a successful result before commit. | Inject failure after proof, normalization, first deduction, history, audit, and before commit; prove baseline fingerprints restored and no stranded transactions. |

## Concrete contract and integration decisions

Implement `IInventoryCommandExecutor.ExecuteAsync(command, cancellationToken)` with typed results for committed/replayed, insufficient quantity, unsupported evidence, stale input, idempotency conflict and retryable concurrency. Command intent must support multiple positions so the 172-bin run is one unit, not two calls that can partially succeed. The precise DTO names may follow repository conventions.

Reuse `IInventoryAvailability`, the existing ledger/custody authority, `TreatmentLineageSegment` version tokens, `TreatmentLineageMovement`, authoritative operation keys and `AuditLog`. Do not route the executor through the legacy `RoomTreatmentService.MaterializeAsync`: it can own/commit normalization and synthesize an untreated gap. Phase 3 will replace those operational call paths together.

The operation participant must execute inside the executor-owned context/transaction, preserve existing parent relationships and return verifiable deltas. It must not be an arbitrary controller callback that can commit, substitute arithmetic or create independent side effects. A test adapter can exercise the existing run/ledger parent structure without wiring an operator endpoint during Phase 2.

Choose the smallest durable idempotency/result and projection-disposition representation after testing existing fields against retry and reversal fixtures. If existing zero-balance projections plus audits cannot distinguish retired history from legitimate reversal, propose additive disposition/evidence metadata and an EF migration in this PR. Document NULL/identity/epoch uniqueness semantics; do not introduce a second physical ledger, blind historical backfill, broad production repair or destructive schema reversal. **No schema decision or migration is included in this preparation commit.**

## Exit checklist for the implementation PR

- [ ] Implement all nine engine responsibilities above, shared by Web/API and dormant in operational workflows.
- [ ] Recreated WP-4/WP-7 acceptance: one 172-bin operation, source balances 0/1,018, exactly 172 consumed; historical 446 is never consumed again.
- [ ] Preserve original history and unrelated identities; retain treatment evidence and unresolved receipt attribution accurately.
- [ ] Same-key replay and different-payload rejection; no duplicate audits, movements, parents or deductions.
- [ ] Real PostgreSQL concurrency, rollback, audit-failure and retry tests; check no leaked connections/transactions.
- [ ] Test balanced, treated, ambiguous, negative, depletion, return/reopen and all supported custody transitions using the existing corpus. Unsupported transition adapters fail closed.
- [ ] Restore/build/model/format/diff and directly affected regression gates pass. An additive schema change, if justified, gets its own scoped compatibility checks.
- [ ] Document the exact Phase 3 caller migration list and verify **zero operational callers** have been switched in Phase 2.

Serializable commands cannot protect against every legacy writer performing work outside this boundary. The concurrency guarantee must state its participating-writer assumption until Phase 3 migrates every producer/consumer and later release gates pass. No staged one-workflow activation is authorized.

## Deliberately outside this work

No production deployment, fake receipt/transfer/run, manual SQL repair, historical negative-position repair, broad ambiguous-case investigation, old Truck Receipt release-fixture reconstruction, zero-skip test campaign, or cold-start tuning. The optional legacy receipt-date PostgreSQL stall is a separate nonblocking follow-up: focused canonical provider tests completed and the acceptance run found no lingering PostgreSQL sessions/transactions. Reopen it only if direct evidence ties it to this engine.

Phase 1 is closed. Phase 2 implementation is the next work in this draft; this preparation commit itself does not implement the executor or clear the live run. Phase 3 cutover and separately authorized production rollout remain later gates.
