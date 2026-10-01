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

These are incremental results from their respective development revisions, not a frozen-candidate full-suite result. All ordinary receipt corrections/overrides, receipt treatment, legacy run/depletion, identity corrections, true-up and baseline/import remain migration work. Coverage registry, remaining races, randomized lifecycle tests, final performance/full-suite/model/format checks and fresh backup rehearsal are outstanding. The standalone API host needs a confirmed authenticated operator identity integration before canonical API receiving is operationally ready. Until then it rejects unauthenticated canonical creation.

The PR remains Draft. A protected legacy writer failing closed is not counted as migrated. No production schema, configuration or data changed.
