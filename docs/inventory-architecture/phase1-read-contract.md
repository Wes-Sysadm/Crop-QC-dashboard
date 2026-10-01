# Phase 1: canonical inventory reads and evidence

Phase 0 PR #257 merged at `b4e23a15721ef73a57d889555cd260ab1b5bd59b`. The web and backup services both had automatic deployment disabled. No deployment was triggered. Phase 1 is developed on `codex/canonical-inventory-read-model` from that merge, separately from the documentation branch.

## Scope and unchanged behavior

This implements a **shadow read contract**, not the production fix or release clearance. No transfer, dump, processor, receiving, treatment or other operational writer calls the new resolver. Existing ledger arithmetic was relocated unchanged into Data, with a Web compatibility facade; existing treatment eligibility remains in place. There is no schema migration, data repair, normalization execution, new physical ledger or partial cutover. The 172-bin Bartlett run remains awaiting entry in the live application.

## Shared implementation

- `CropQc.Shared/Inventory/InventoryAvailability.cs`: immutable identity, location/custody, quantities, treatment slices, separate receipt confidence, typed blockers, evidence references, proposed normalization candidates and read watermark.
- `InventoryAvailabilityResolver`: pure resolver behind `IInventoryAvailability.ResolveAsync(scope, requirements, asOf)`; no EF dependency or write capability. Defaults require known treatment. Requirements constrain custody, receipt scope, selected treatment or expected fingerprint; they cannot replace quantity arithmetic.
- `CropQc.Data/Inventory/RoomInventoryLedgerQueryService`: existing ledger/baseline/legacy identity calculation, moved without arithmetic changes. `CropQc.Web.Services.RoomInventoryLedgerQueryService` delegates through inheritance for compatibility.
- `InventoryEvidenceLoader`: no-tracking, bounded batch queries and in-memory indexes; consistent RepeatableRead (or SQLite Serializable) snapshot. Its own PostgreSQL transaction is READ ONLY. It disposes its read transaction without committing a caller transaction. Ambient weak-isolation transactions are rejected.
- `InventoryCustodyEvidence`: existing inter-crew dispatch-minus-return allocations, Outside Warehouse transfers and Processor Shipment lines; checks parent quantities against ledger and movement evidence. These are custody records, not a second room inventory credit. Mixed-load identities come from dispatch allocations.
- Web and API register the same `AddCanonicalInventoryReads` module. Neither receiving writer is replaced.

For operable positions, `0 <= available <= authoritative`. Negative authority remains negative and returns `NegativeAuthoritativeBalance`. Raw projection excess never increases physical stock. Missing projection coverage does not imply Untreated: receipts and exact incoming movements must prove it. Balanced persisted treatment slices retain application references; contradictory/unlinked treatment evidence blocks.

The untreated-pool proof replays exact identity ledger events in **recorded order** to determine current occupancy, preserving effective/business dates and returning typed backdated ledger IDs. It validates arrival quantities against immutable movement parents or unchanged ordinary receipts, checks treatment evidence, and preserves receipt confidence separately. Returns require their original reversal evidence. Unsupported corrections, true-ups, contradictory identity and indistinguishable mixed treatments stay blocked. Normalization candidates are descriptive only; an aggregate candidate with `ExactRowAllocationProven=false` is **not an executable row update plan**.

Current mutable projections cannot reconstruct arbitrary historical snapshots: if relevant evidence changed after `asOf`, the result explicitly blocks with `HistoricalSnapshotUnavailable`. A fingerprint is a stale-input detector, never a database lock or write authorization.

## WP-7 regression and production comparison

Production read-only capture: **2026-10-01T05:05:35.2841246+00:00**. All 24 documented positions were found; none changed authoritative quantity from the Phase 0 assessment. The comparison used the actual existing shared movement treatment gate and ActualRun's new-run treatment gate. These probes deliberately exclude seals, destination rules and user permissions; they are not simulated successful writes. Receipt classifications are additionally obtained from the existing receipt-provenance resolver. The complete CSV records every case and every difference.

WP-7 physical authority is **1,122**; raw projections are **1,568**. The canonical result returns **1,122 available untreated bins**, with **324 duplicate-alias bins + 122 consumed historical bins = 446 excluded**. Ledger entry 3375 is explicitly marked backdated: the run was recorded after arrivals but has an earlier business date. Exact receipt allocation remains ambiguous, so receipt-specific requirements still block. All existing segment quantities remain unchanged.

WP-4 separately remains **68 available**. The operator-confirmed intended run needs **68 from WP-4 + 104 from WP-7 = 172**. This is a fixture/diagnostic result, not authorization to enter the run before later implementation and cutover phases.

There are **3 quantity-divergent positions** (6 gate observations): Evans-5/3152 = 61 canonical versus 0 legacy, Evans-5/9682 = 252 versus 0, and WP-7/1372 = 1,122 versus 0. The two Evans cases use exact normalized incoming movement evidence and the new pool proof; they remain unrelated to the already completed LAMB-14/9682 movement. **3 additional positions differ in treatment presentation only** (6 gate observations): WP-5/1084, DH-15/2350 and WP-8/2350 remain blocked with positive physical inventory. Legacy returns a `needs-review` placeholder; canonical returns Unknown treatment, no proven current slice and `UnsupportedHistoricalEvidence`. No quantity changes are inferred from those presentation differences.

The new proof explains ten historical pools (including zero-stock cases); nine nonnegative cases remain unsupported and five negative positions remain blocked. Unsupported cases retain their original root-cause evidence in the comparison. No assertion that every unproven case is safe is made. Protected ledger, segments, movements, receipts, treatment applications/links and audit fingerprints matched before/after within the same read-only production snapshot; zero entities were tracked. This does not assert that unrelated operators made no concurrent writes outside that snapshot.

## Admin and shadow diagnostics

`GET /Admin/InventoryShadow?roomId=<id>` returns the same canonical contract and explicit legacy gate observations/divergences. It requires the existing `HistoricalInventoryCleanupAdmin` policy, is GET-only, requires a scope filter and sends `Cache-Control: no-store`. It supports warehouse or custody-record filters and an exact-receipt requirement. It is not linked into normal operator flows. The endpoint has **not been deployed**; production comparison ran locally through an explicitly read-only connection.

The comparison API accepts named workflow observations so development/tests can expose a transfer=19/dump=0 disagreement without changing either workflow. The built-in admin probes identify exactly which treatment gates they measured; they do not claim to reproduce all workflow-specific business checks.

## Evidence corpus and verification

Reusable fixtures cover 25 named shapes: original Room 14, 9722, LAMB-14/9682, WP-7 (including backdated entry), status aliases, receipt correction, return/recreated source, split/partial movement, partial depletion, stale depletion, receipt true-up, depleted transfer, multiple treatments, balanced lineage, ambiguity, exact/unresolved receipts, negatives, unsupported gaps, InTransit, Processor, Outside Warehouse, pack-run/dump and room transfer. Production IDs are labels only; domain code has no lot/room/quantity special cases. A deterministic 1,000-case randomized quantity test checks physical bounds.

Tests cover repeat resolution/persisted and tracked state, SaveChanges rejection, zero audits/timestamp/projection changes, application evidence, reversal proof, operation requirements, stale fingerprints, Web/API host parity, admin HTTP authorization, custody balancing and zero duplicate room credit. PostgreSQL round-trip measurements are provider tests, not InMemory query estimates.

| Positions | Canonical SELECTs | Proof evidence rows | Local elapsed ms | Allocated bytes | Legacy SELECTs | Legacy elapsed ms |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 10 | 4 | 558.6 | 11765528 | 9 | 143.8 |
| 10 | 10 | 31 | 29.2 | 942896 | 54 | 41.0 |
| 100 | 10 | 301 | 47.8 | 3317336 | 504 | 284.5 |
| 1000 | 10 | 3001 | 1248.8 | 27110216 | 5004 | 4783.0 |

Measurements include loader and resolver execution on a disposable local PostgreSQL database. Evidence row counts count proof records materialized by the loader, excluding the ledger adapter's aggregate/metadata result rows. The first sample includes cold query compilation; elapsed times are observations, not an SLA. The baseline is the existing ledger plus batched shared treatment gate with stale shared projections. No fresh production database restore was required or claimed in Phase 1; local recreated data and read-only live diagnostics were used. Full fresh-restore write rehearsal remains a later gate.

Final test/build/model/format results and explicit optional-provider limitations are recorded in [phase1-verification.md](phase1-verification.md). No prior release's test results are reused.

## Exact remaining work for Phase 2

1. Implement the shared transactional command executor with inside-transaction revalidation, idempotent intent validation and conservation checks; do not let a helper commit normalization separately.
2. Turn evidence candidates into reviewed, deterministic normalization plans that preserve applications, receipt evidence and movements. Resolve aggregate-versus-exact receipt allocation without inventing historical consumption.
3. Establish the canonical projection factory and smallest justified current/historical lifecycle representation; validate reversals and occupancy transitions before any additive DDL/backfill.
4. Remove negative-inventory allowances from the new command policy and prove simultaneous-consumer, treatment/transfer, correction/edit and import/move races on PostgreSQL.
5. Add audit-failure, stale-read, serialization retry and all-or-nothing rollback tests. Retain shadow mode until every producer/consumer is migrated in Phase 3 and later release gates pass.

Phase 2 is not included in this PR. Phase 1 does not fix the live blocked run, merge itself, deploy a diagnostic, or authorize production repair.
