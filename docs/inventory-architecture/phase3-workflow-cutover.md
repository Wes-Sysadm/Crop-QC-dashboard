# Phase 3 — canonical inventory workflow cutover

Status: PR #260 **Ready for Review**. Main now includes the separately merged backup release #261 at `f918ca5350cbf10dc6a8c7212065e08c177be876`; this branch incorporates that merge. Review readiness is separate from production release clearance. Do not merge #260, deploy its inventory cutover, activate production, enter the live 172-bin run, or bulk-repair legacy discrepancies in this release-preparation task.

Phase 2 merged as `4d3cd4b57cb317e335dec77ecf1617182819d17d` on 2026-10-01. Phase 3 initially branched from that commit. The latest read-only Render check still shows web deployment `dep-darf75gjo6nc73fnsnm0` at `71ff57dd7fe7009b8cc56199a46bd4ead43859c2`. Web and backup-worker auto-deploy are OFF. Neither the Phase 2 nor #261 merge deployed production; this preparation made no production inventory, configuration or schema changes.

## Activation and scope

Separate selectors/writers disagreed about authority versus historical projections. Safely proven stale projections could block one screen while another allowed the same bins. One `CanonicalInventoryCommandsEnabled` process configuration, default OFF, now selects the implementation across Web/API.

OFF retains legacy behavior without startup/read normalization. ON uses the Phase 1 resolver and Serializable executor across all ordinary writers, with no fallback arithmetic. Tracked-field guards and raw DML interceptors reject bypasses; only Data infrastructure opens the internal transaction capability. Once a canonical journal exists, OFF cannot reenable legacy physical writes. Post-activation rollback must retain compatible command code and additive schema.

The [32-workflow matrix](phase3-coverage-matrix.md) and [registry](phase3-workflow-registry.json) preserve Phase 0 names/order. Ordinary physical writers are Canonical; reports are non-mutating; exceptional maintenance is explicit. A blocked ordinary writer does not count as migrated.

Migrated paths include room/bulk/same-company movement, Outside Warehouse, processor, inter-crew custody, loss/depletion and their returns/reversals; ActualRun and legacy Bins Run create/correct/cancel; Web/API receiving and receipt activation; receipt quantity/void/location/identity/metadata edits; room/receipt treatment and reversal; manual stock and baseline/import. Operational room/transfer/dump/processor/outside/loss and planning selectors use the resolver. Truck queues and current custody comparisons are batched.

## Evidence, policy and history

Operation intents own the policy in `InventoryCommandPolicy` and the engine coordinators. Ordinary movement/dump requires proven identity, treatment, quantity and route. Receipt correction additionally requires exact ownership and reviewed versions. Custody restoration requires immutable parent/movement coverage. Baseline commit revalidates the full room/master-data forecast; omitted positions cannot change. Unknown manual stock is explicit and never inferred as untreated or receipt-owned.

Receipt identity corrections create audited maps/compensations; restoration follows current identity/treatment while retaining original receipt, dispatch and movement evidence. Metadata-only run edits preserve their consumption revision. Treatment carried into another room belongs to that allocation and cannot reclassify unrelated untreated stock already there. Reversals follow current room/custody allocations without reviving retired rows.

Canonical ledger invariant v3 gives descendant changes their own baseline date; ReceiptAdd follows receiving time. Existing v0–2 history remains unchanged. Normalization retires exact proven projection rows and retains quantities/provenance; historical excess never becomes physical stock.

Truck Receipt preserves persistent RequiresTruckReceipt, one-to-one matching, exact variety checks, InTransit custody, edits/partial returns, completion and reopen. Pausing does not reinterpret existing loads. Completion creates one destination population.

## Coverage and maintenance boundary

Architecture tests verify all 32 entries, intent enums, source/test links, candidate classifications/hashes, the two capability-owning files and absence of repair services in ordinary controllers. The [review manifest](phase3-reviewed-write-candidates.json) tracks 92 conservative source candidates, including read-only/local-collection false positives. Changed/new candidates require explicit review. Regex/content locks do not independently prove control flow; runtime guards and PostgreSQL workflow tests supply that evidence.

Retained OFF bodies are classified. Exceptional maintenance includes ActualRun3 reporting correction, BinsRun reversal1820, Evans11/3152, TR508901 identity repair, July27 normalization, July28 backfill, legacy grower reconciliation, TR108859 loss correction and treatment144 correction. None receives a canonical capability exemption. Evans11 also has an existing explicit configuration-triggered hook (`CROPQC_EVANS11_3152_REPAIR_MODE`), not an automatic backfill introduced here. Ordinary receipt soft deletion refuses inventory history; crop purge remains separately authorized maintenance.

## Schema and rollback

Additional migration `20261001203105_CanonicalIdentityRestorationSides` changes four parent/adjustment-side unique indexes to retain legacy v0–2 constraints while canonical v3 uses unique operation keys and the atomic journal. One historical parent can therefore restore distinct current receipt identities. No rows/columns are deleted or backfilled; Down refuses once commands exist.

Production's older provider history requires reviewed bounded migration scripts. The local restore harness applies only Truck Receipt-to-Phase2/3, or Phase2-to-Phase3 when already present, and checks every existing table with canonical mode OFF. It separately applies the backup migration only if absent, supporting a backup-only release before Phase 2/3 without replaying its columns. Replaying all old migrations is unsafe because some older columns already exist despite history gaps. The normal model check remains against the repository's SQL Server design snapshot. No production migration is run.

The migration IDs remain in their original order: `20261001144023_CanonicalInventoryCommands`, `20261001203105_CanonicalIdentityRestorationSides`, `20261002031709_BoundedBackupSnapshotProgress`. The latest migration target model carries forward the four Phase 3 index filters, matching the combined model snapshot. No migration Up/Down operation or runtime inventory behavior changed during reconciliation. The reviewed-source manifest refresh covers only #261's reviewed changes in DbContext backup metadata, host backup-recovery dispatch and BackupService; no writer exemption was added.

## Verification

- After integrating #261: **414 focused tests passed, 0 failed, 0 skipped** (6m50s), covering canonical Phase 1/2/3, the normal 172-bin ActualRun, restored representative lifecycle, Truck Receipt regressions, backup compatibility and all nine writer-coverage checks. Restore/build, EF model, formatting and diff checks passed. No full-suite rerun: reconciliation changes migration metadata, the local restore fixture and reviewed hashes; inventory runtime behavior is unchanged. Backup #176 is the source of these regression restores, not fresh-release evidence.
- Separate release-order checks also passed: #261's exact backup-only migration on the raw #176 schema preserved every original table fingerprint (1 test); Phase 2/3 applied after that backup-only schema, followed by the normal 172-bin ActualRun and protected-history/rollback assertions (1 test). Neither test applied schema to production or represents a newly captured backup.
- Affected resolver/historical/identity/treatment/Truck selection: 60 passed.
- Three normal-service random seeds 255, 1372, 9722 passed before final-suite execution. Every step checks conservation, current projection/authority equality, immutable ledger/movement evidence, no duplicate positive projection and no historical revival.
- Fifteen synthetic historical shapes exercise six selectors and normal movement for proven cases; ambiguity/negative cases remain blocked. WP-7 uses restored history; Truck InTransit has dedicated normal-service coverage.
- Seven real PostgreSQL application races cover dump/dump, move/dump, processor/move, treatment/move, receipt correction/move, Truck completion/partial return and baseline/move.
- Existing verified restore: normal ActualRun 172 passed, WP-4 68→0 and WP-7 1,122→1,018. Assertions separately account for 324 duplicate + 122 consumed historical = 446 excluded bins, preserve unrelated fingerprints, verify replay, correction to 170 and cancellation. Eight failures through normal submission roll back all tables.
- Wider restored normal workflows and two restored concurrency cases use explicit isolated fixture identities, protect original-row fingerprints and compare unrelated room/custody consistency.
- Six selectors retain constant one-versus 100-lot query counts: room detail 78, transfer 19, dump 24, processor 18, outside 14, loss 15. Room detail includes other page sections and remains a latency optimization opportunity. Queue proof remains bounded across one versus ten loads.

Final combined suite: **2,196 passed, 0 failed, 0 skipped** (7m45s), with canonical PostgreSQL, date-accounting, legacy normalization, identity, Truck receipt/schema/adoption/worker fixtures enabled. Solution restore/build, model consistency, changed-file format verification and git diff checks passed. An unrestricted run exhausted the local PostgreSQL lock pool during parallel schema creation; the final run limits test workers to 4 while each race still uses independent connections.

**Release backup gate remains unproven.** At 2026-10-02 03:54 UTC the authoritative latest run remained #177, Running since 2026-10-01 22:06:13.539320 UTC, with no terminal outcome or package/hash/verification. The compatibility lease is still populated and expired at 00:06:13.539320 UTC. This is not affirmative failure evidence. No newer run exists. No lease reset, competing backup, recovery, migration or deployment was performed.

The mandatory AGENTS.md and overnight release standard require a fresh independently verified standard backup before the first production mutation/deployment. #261 is merged with both auto-deploy settings OFF, but cannot pass its deployment gate on this evidence. Backup #176 remains valid development-regression evidence, not a replacement fresh release backup. The supported #177 recovery dry run is still pending deployment and independently confirmed worker-termination evidence. A participating new runner will refuse an unresolved legacy Running record.

The final Phase 3 rehearsal source is the next independently verified backup produced by the corrected #261 runner, not necessarily #177. Fresh release backup/final restore verification remains required before production activation. Earlier engineering evidence and subsequent #176 regression checks do not certify this remaining freshness gate.

## Shared operator session (user decision: existing Web Google sign-in)

The Web app remains the Google sign-in authority. API operator requests consume its protected cookie using the same `Cookies` scheme, default `.AspNetCore.Cookies` name and `CropQcDashboard` data-protection application name. Both hosts use the same cookie configuration and active-user validation. API canonical receiving additionally checks current database role/page permissions and an antiforgery header; it does not trust cached role claims. API authentication does not change QC Station routes or enrollments. No new Google OAuth flow, credentials or production configuration was introduced.

Before standalone API activation, verify the two hosts actually access the same restricted persisted data-protection key ring (`DataProtection:PersistKeysToFileSystem`, `DataProtection:KeysPath`, `DataProtection:ApplicationName`). Identical directory names on different machines are not shared storage. The browser must reach both through the same cookie origin, or reviewed sibling custom domains with the same `Authentication:CookieDomain`; unrelated default hosting domains cannot share this session. Optional `Authentication:CookieName` must match. The cookie stays HttpOnly, SameSite=Lax and Secure outside development. Infrastructure/session validation remains a release gate; local checks do not claim production session sharing.

After signing in through the existing Web login, a receiving client calls authenticated `GET /api/operator-session`, retains the returned antiforgery cookie and sends the returned request token in `X-CSRF-TOKEN` on receipt writes. Unauthenticated API requests return 401; absent receiving permission returns 403. No cross-origin credential policy was added.

Local PostgreSQL/HTTP validation: 3 checks passed, including a Web-protected session creating/replaying an API receipt with the correct audited actor, rejection of missing CSRF/foreign keys/expired sessions and live permission/account revocation. The Google network login itself was not simulated as production evidence. This follows [ASP.NET Core cookie sharing](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-9.0).


## Remaining boundaries

All nine architecture checks passed in the combined suite. Supplemental Truck coverage passed 45 checks using its correct pre-adoption fixture where required. Fresh backup/restore remains required. Actual shared-session infrastructure validation is a release gate. No WinForms/QC Station changes; no MSI required. Phase 4/5 owns production deployment, activation and the genuine 172-bin operation.

## Reproducing the PostgreSQL gate

Set `CANONICAL_INVENTORY_TEST_POSTGRES` to a clearly disposable local PostgreSQL administration database. Set `CANONICAL_INVENTORY_RESTORE_POSTGRES` to the separately verified local backup template; tests clone it and apply the bounded schema only to their disposable clones. The helper does not itself certify archive freshness.

Run the full test project with `--settings tests/CropQc.Api.Tests/canonical-postgres.runsettings`. The four-worker limit prevents simultaneous disposable schema creation exhausting a small local PostgreSQL lock pool; application race tests still coordinate independent connections. Enable the existing Truck Receipt connection settings with their appropriate pre-feature, unadopted and adopted isolated fixtures. Do not point test connection settings at production. Legacy grandfathering specifically needs an unadopted fixture; an already-adopted production copy cannot satisfy its initial pre-feature rollback assumption.
