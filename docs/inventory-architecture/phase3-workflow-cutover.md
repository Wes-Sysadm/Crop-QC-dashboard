# Phase 3 — all-workflow canonical cutover

Work in progress. This PR must remain Draft until every ordinary writer and selector is migrated and all requested gates pass. A blocked legacy writer is a safety barrier, **not** completed migration coverage.

Phase 2 merged as `4d3cd4b57cb317e335dec77ecf1617182819d17d` on 2026-10-01. Production web and backup-worker auto-deploy were confirmed off before merge. The post-merge web deployment remains `dep-darf75gjo6nc73fnsnm0`, application `71ff57dd7fe7009b8cc56199a46bd4ead43859c2`; no production deployment was started.

One immutable process configuration, `CanonicalInventoryCommandsEnabled`, defaults to false and is registered identically in Web and API. ON enforces a DbContext write boundary for protected inventory entities and a SQL interceptor for direct DML. Only the canonical executor opens its internal transaction capability. OFF retains legacy behavior; no startup normalization or repair is added.

The change scope includes the original 32-workflow inventory, ordinary receiving in both applications, every inventory-consuming adapter and its reversal, operational selectors, the command engine, current projections, ledger and parent/history/audit records. Shared contracts and all-workflow cutover justify the requested full suite. Historical receipts, movements, treatment links, old audits and unrelated inventory must remain preserved.

Initial guard tests prove ON rejects a direct projection writer while OFF retains legacy behavior. Coverage and adapter validation will be recorded here as implementation proceeds. Production activation, release and the real 172-bin run are explicitly outside this PR.

## Implementation checkpoint (not acceptance clearance)

Implemented canonical service branches include ActualRun create/correct/cancel, Web/API receipt creation, room/bulk movement and reversal, Outside Warehouse create/return, processor create/return, dropped-bin create/reverse, room treatment and reversal, inter-crew dispatch/receive/reverse, and Truck Receipt completion/reopen/allocation edits/partial returns. Read selectors for these operational consumers use the shared resolver. Exact historical custody parents are supported when immutable movement evidence proves the return; no committed journal is fabricated for legacy history.

Local PostgreSQL checks completed during development:

- Backup #176 normal ActualRun workflow: WP-4 68 → 0; WP-7 1,122 → 1,018; 172 consumed. Correction to 170 and cancellation preserve prior revisions and restore 68/1,122. This existing verified restore is development evidence, not the required fresh final rehearsal.
- Receiving/processor/room move/loss service tests: 6 passed.
- Resolver/treatment focused regressions: 38 passed.
- Engine/concurrency/Outside Warehouse/activation selection: 21 passed.
- Application-service races (dump/dump, move/dump, processor/move, treatment/move): 4 passed using independent PostgreSQL contexts.
- Truck Receipt complete/reopen/re-match/recomplete passed with exactly one destination population; allocation return/edit regressions are in progress.

Further implementation includes exact receipt treatment/reversal, legacy Bins Run create/correct/reverse, receipt depletion/void, and explicit positive manual stock addition. Depletion no longer permits its old overdraw checkbox to create negative stock. Manual additions preserve Unknown treatment and do not invent receipt ownership. Original entries and movements remain immutable evidence when runs are corrected. A mixed-load reopen regression was fixed so reversals already written inside the same transaction are not mistaken for prior reversals.

Additional local checks passed: exact receipt treatment (1), legacy run lifecycle (1), depletion lifecycle/overdraw rejection (1), mixed-load regressions (2), resolver/engine/manual-stock selection (44), and global gate/Truck Receipt/Outside Warehouse selection (8). Some selections overlap; these counts must not be added as a unique-test total. The DI executor is dormant while OFF. After a canonical journal exists, OFF cannot reenable legacy physical writes. Raw DML guards include receipt/custody tables and scalar execution; tracked nonphysical receipt matching and packout locking remain permitted.

These are incremental results from their respective development revisions, not a frozen-candidate full-suite result. Ordinary receipt corrections/overrides, identity corrections, baseline/import and broader treatment reversal cases remain migration work. Coverage registry, remaining races, randomized lifecycle tests, final performance/full-suite/model/format checks and fresh backup rehearsal are outstanding. The standalone API host needs a confirmed authenticated operator identity integration before canonical API receiving is operationally ready. Until then it rejects unauthenticated canonical creation.

The PR remains Draft. A protected legacy writer failing closed is not counted as migrated. No production schema, configuration or data changed.
