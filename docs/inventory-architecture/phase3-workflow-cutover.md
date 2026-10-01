# Phase 3 — canonical inventory workflow cutover

Status: draft PR #260; **not Ready for Review** until the final gates pass. Do not merge, deploy, activate production, enter the live 172-bin run, or bulk-repair legacy discrepancies as part of this PR.

Phase 2 merged as `4d3cd4b57cb317e335dec77ecf1617182819d17d` on 2026-10-01. Phase 3 branches from that main commit. The latest read-only Render check still shows web deployment `dep-darf75gjo6nc73fnsnm0` at `71ff57dd7fe7009b8cc56199a46bd4ead43859c2`. Web and backup-worker auto-deploy are OFF. No deployment followed the Phase 2 merge; no production inventory, configuration or schema changed.

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

Production's older provider history requires reviewed bounded migration scripts. The local restore harness applies only Truck Receipt-to-Phase2/3, or Phase2-to-Phase3 when already present, and checks every existing table with canonical mode OFF. Replaying all old migrations is unsafe because some older columns already exist despite history gaps. The normal model check remains against the repository's SQL Server design snapshot. No production migration is run.

## Verification

- Affected resolver/historical/identity/treatment/Truck selection: 60 passed.
- Three normal-service random seeds 255, 1372, 9722 passed before final-suite execution. Every step checks conservation, current projection/authority equality, immutable ledger/movement evidence, no duplicate positive projection and no historical revival.
- Fifteen synthetic historical shapes exercise six selectors and normal movement for proven cases; ambiguity/negative cases remain blocked. WP-7 uses restored history; Truck InTransit has dedicated normal-service coverage.
- Seven real PostgreSQL application races cover dump/dump, move/dump, processor/move, treatment/move, receipt correction/move, Truck completion/partial return and baseline/move.
- Existing verified restore: normal ActualRun 172 passed, WP-4 68→0 and WP-7 1,122→1,018. Assertions separately account for 324 duplicate + 122 consumed historical = 446 excluded bins, preserve unrelated fingerprints, verify replay, correction to 170 and cancellation. Eight failures through normal submission roll back all tables.
- Wider restored normal workflows and two restored concurrency cases use explicit isolated fixture identities, protect original-row fingerprints and compare unrelated room/custody consistency.
- Six selectors retain constant one-versus 100-lot query counts: room detail 78, transfer 19, dump 24, processor 18, outside 14, loss 15. Room detail includes other page sections and remains a latency optimization opportunity. Queue proof remains bounded across one versus ten loads.

Final combined suite: **2,196 passed, 0 failed, 0 skipped** (7m45s), with canonical PostgreSQL, date-accounting, legacy normalization, identity, Truck receipt/schema/adoption/worker fixtures enabled. Solution restore/build, model consistency, changed-file format verification and git diff checks passed. An unrestricted run exhausted the local PostgreSQL lock pool during parallel schema creation; the final run limits test workers to 4 while each race still uses independent connections.

**Fresh backup gate failed; PR remains draft.** Independently verified Backup 176 (2026-10-01 08:01:24 UTC; 15,738,099 bytes; SHA-256 `74ecb8db10290b44b04366513b6ec295a56141b2b2c51c009abd50ea85a20208`) is development evidence only. Fresh Backup #177 started through the standard production command at 2026-10-01 22:06:13 UTC but at 22:50 UTC its process was absent while the database record still said Running. Package, checksum, verification, completion and lease-release fields remained null; successful command exit was not observed. The reason for termination is unknown. Work stopped at this gate without a retry, lease reset or production correction. Independent archive verification and a fresh restore rehearsal remain required before Ready for Review.

Legitimate blockers remain for ambiguous treatment, receipt ownership, incomplete/conflicting identity, negative authority, invalid custody and unsafe reversal after subsequent activity. No mass historical repair is included.

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
