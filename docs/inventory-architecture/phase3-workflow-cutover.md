# Phase 3 — all-workflow canonical cutover

Work in progress. This PR must remain Draft until every ordinary writer and selector is migrated and all requested gates pass. A blocked legacy writer is a safety barrier, **not** completed migration coverage.

Phase 2 merged as `4d3cd4b57cb317e335dec77ecf1617182819d17d` on 2026-10-01. Production web and backup-worker auto-deploy were confirmed off before merge. The post-merge web deployment remains `dep-darf75gjo6nc73fnsnm0`, application `71ff57dd7fe7009b8cc56199a46bd4ead43859c2`; no production deployment was started.

One immutable process configuration, `CanonicalInventoryCommandsEnabled`, defaults to false and is registered identically in Web and API. ON enforces a DbContext write boundary for protected inventory entities and a SQL interceptor for direct DML. Only the canonical executor opens its internal transaction capability. OFF retains legacy behavior; no startup normalization or repair is added.

The change scope includes the original 32-workflow inventory, ordinary receiving in both applications, every inventory-consuming adapter and its reversal, operational selectors, the command engine, current projections, ledger and parent/history/audit records. Shared contracts and all-workflow cutover justify the requested full suite. Historical receipts, movements, treatment links, old audits and unrelated inventory must remain preserved.

Initial guard tests prove ON rejects a direct projection writer while OFF retains legacy behavior. Coverage and adapter validation will be recorded here as implementation proceeds. Production activation, release and the real 172-bin run are explicitly outside this PR.
