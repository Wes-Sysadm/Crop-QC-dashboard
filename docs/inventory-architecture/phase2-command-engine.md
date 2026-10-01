# Phase 2 — canonical transactional inventory command engine

Phase 1 is accepted at `1380f68030d4faa4c8a79be107a8a290e73f73ac`. PR #259 implements the write engine in `CropQc.Shared/Inventory` and `CropQc.Data/Inventory`. It is deliberately **not registered or called by either operational application**. Phase 3 must migrate all producers and consumers together. The live 172-bin run remains blocked until that later cutover and authorized release.

## Transaction and intent

`InventoryCommandExecutor.ExecuteAsync` owns one PostgreSQL **Serializable** transaction. It uses a fresh `IDbContextFactory` context on each of at most three attempts. Inside that transaction it loads the Phase 1 evidence, invokes the same canonical resolver, checks the supplied fingerprint and entity versions, validates operation policy, proves exact normalization, writes parents/ledger/projections/movements/audits, checks conservation and commits. Helpers cannot independently commit. There is no arbitrary caller callback for writing inventory.

PostgreSQL serialization/deadlock failures and unique-key races are classified through the complete exception chain, including EF wrappers. Retry starts the **entire** intent in a fresh transaction. A competing committed change normally makes the original fingerprint stale. Validation returns typed blocked/stale/conflict results. Other write failures roll back and propagate; no success is returned before commit. Cancellation also rolls back. PostgreSQL sequences can skip IDs on rollback; no business rows or audits remain from a failed attempt. A lost commit response can be retried with the same key.

`InventoryCommands` stores the full serialized intent, SHA-256, committed result, actor, timestamp and optional original-command reversal key. The key covers ordered lines, identities, locations, quantities, treatment selection, receipt/version requirements, effective date, actor, reason, counterparty, pricing and original operation. Exact repeated intent returns the stored result with `Replayed`; changed intent conflicts. The unique reversal key prevents returning/reopening an original command twice. Keys are limited to 60 characters so derived operation keys fit existing parent fields. JSON is compared as well as its hash.

The engine validates an active actor and active, unsealed rooms. Authentication, authorization and external notification delivery remain application adapter responsibilities; no new endpoint is exposed. Serializable protection assumes participating writers. It cannot make uncontrolled legacy writers safe, which is why no partial production cutover is permitted.

## Exact normalization and lifecycle

A Phase 1 candidate is descriptive, **not an executable row allocation**. `InventoryNormalizationPlanner` independently resolves the evidence with projection quantities removed. Only proof of the complete untreated physical pool permits wholesale supersession of identified current rows. It does not choose a proportional survivor or guess which receipt owns remaining bins.

An executable plan records the algorithm/evidence version, read fingerprint, supporting ledger/movement/receipt/application IDs, receipt-confidence result, exact old row IDs/keys/states/quantities/versions/timestamps/application links, resulting historical states and the replacement projection ID/quantity. Application checks those before values again in the transaction. Normalization cannot modify physical ledger quantity. Unknown or contradictory treatment/identity evidence blocks the command.

Migration `20261001144023_CanonicalInventoryCommands` adds:

- the command journal;
- `Disposition` (`Current` default or `Historical`), `RetiredQuantity`, `RetiredAt`, `RetiredByCommandKey` on existing lineage segments;
- current-only versions of the existing receipt/unassigned projection uniqueness indexes.

There is no second physical ledger, destructive rewrite or data repair/backfill. Existing rows receive the compatible `Current` default. Old deployed code remains compatible while the engine is dormant. Downgrade refuses to remove schema when command or retired-projection evidence exists; application rollback must preserve additive schema after activation.

Retirement keeps the original row, receipt links, treatment application links, creation provenance and all movement references. Its current quantity becomes zero; original retired quantity and normalization audit retain the prior representation. The canonical factory only creates/credits current rows, uses canonical identity/status keys, considers unsaved tracked rows, checks receipt/treatment links and never revives historical rows. Destination evidence is also validated and, when independently proven, normalized before credit.

Two narrowly exposed read-contract defects are covered: custody allocations remain current evidence even if their source-room projection was retired, and a reversed treatment application does not invalidate a subsequently balanced untreated current projection. No other Phase 1 arithmetic is replaced.

## Central operation policy and participants

All operations require proven identity, custody and selected treatment. The caller cannot turn these requirements off. Receipt-specific corrections additionally require proven exact receipt attribution. An ordinary dump can use a proven untreated pool with ambiguous receipt allocation.

| Intent | Engine-owned records and conservation |
|---|---|
| Room move / warehouse transfer | RoomTransfer, paired ledger entries, exact source/destination lineage movements; decrease equals increase. |
| Dump | One ActualRun/current revision for the command, one BinsRun entry per source, ledger deductions and linked movements; source decreases equal run consumption. |
| Processor sale | Active processor, explicit pricing basis/rate/currency and weight when required, shipment/line, ledger and movements; source decrease equals processor custody. |
| Outside warehouse transfer | Active outside warehouse, transfer parent, ledger and immutable allocations; source decrease equals outside custody. |
| Inter-company dispatch | InTransit parent, exact dispatch allocations and source ledger; new loads retain the Truck Receipt requirement. |
| Receive transfer | Exact complete custody allocation, destination ledger/projection/movements, parent completion. Required Truck Receipts must already be matched and reconcile crop/profile/quantity/route/version with no preexisting receipt-owned inventory. No receipt is fabricated. |
| Loss | Dedicated loss parent, ledger deduction, linked loss movement. |
| Treatment assignment / reversal | Whole occupied room positions, existing application/source history and projection application links; physical inventory unchanged. Downstream movement or stock outside the exact reversal blocks reversal. |
| Receipt correction | Explicit positive or negative quantity delta, receipt version, completed correction record, ledger/movement/projection changes. Existing exact receipt provenance is mandatory. |
| Baseline adjustment | Explicit signed true-up on an already proven position; no blind baseline replacement or inferred gap. |
| Transfer allocation edit | Version-guarded addition to an original unmatched InTransit dispatch; source decrease equals the added custody allocation. |
| Return / reopen | Original durable command, exact complete custody/received allocation and reversal links. Reopen removes destination stock and restores transit; return credits the original source. Original movements/receipts remain retained. |

Quantity is never obtained from projection excess. Every physical source and destination must remain nonnegative. Explicit positive corrections require an already proven position; they are not a way to create arbitrary new lots or cure a legacy negative balance. Unsupported/ambiguous transitions fail closed. Exact complete single-parent custody completion/reversal includes all identities and varieties in a mixed load; splitting a commercial load, receipt matching/editing, initial receiving/import creation, financial corrections and UI orchestration are not silently inferred by this inventory intent. Those adapters must preserve their existing business guards at the all-workflow cutover.

Before committing, the engine runs the same canonical resolver again for every affected room position and requires an operable position with current projection quantity equal to authoritative quantity. Optional test instrumentation receives only a stage and attempt number; it cannot access or commit the engine's transaction.

## Acceptance and evidence

The primary test uses independently downloaded and verified production **Backup #176**, captured 2026-10-01 08:01:24 UTC (snapshot commit `0f4456ea7c4e9d48456240a9f0feeb912c2cb57d`). Verification covers exact size/SHA-256, ZIP manifest/components and local PostgreSQL restore. Rehearsal clones that local restore, applies only the additive schema locally, and never writes production. Its freshness limit is the backup timestamp; subsequent live activity is not represented.

The restore acceptance command consumes **WP-4 68 + WP-7 104 = 172**, yielding **0 and 1,018**. WP-7 starts with physical 1,122 and raw projection 1,568. Complete supersession preserves those six original rows; the replacement pool is 1,122 before consuming 104. The **324 duplicate + 122 previously consumed = 446** excess produces neither inventory nor a second consumption. Receipt provenance remains ambiguous and no receipt attribution is invented.

The harness verifies exact ledger/movement/run counts, original row metadata and append-only history, unchanged fingerprints of every table outside the specified command scope, and rollback of the complete database after injected failures. A downgrade after command history exists is refused and leaves the committed database unchanged.

Tests include mixed-variety dispatch/receive/reopen/return, blocked direct-route bypass, matched receipt unlink on return, whole-room treatment across lots, and all Phase 1 corpus shapes, real independent PostgreSQL connections for dump/dump, transfer/dump, treatment/transfer, correction/transfer edit, return/reopen and baseline/movement, simultaneous same-key replay, nested serialization retries, real SaveChanges interception for audit/history/parent failures, stale fingerprints/versions, overdraw, positive corrections, custody return and treatment reversal. Run the fresh-restore test only with a separately verified disposable local backup, never a production connection.

```powershell
# Variables must point to disposable LOCAL databases; tests enforce disposable names.
$env:CANONICAL_INVENTORY_TEST_POSTGRES = '<local disposable PostgreSQL connection>'
$env:CANONICAL_INVENTORY_RESTORE_POSTGRES = '<verified local restored database, migration applied>'
$env:CANONICAL_INVENTORY_RESTORE_REPORT = '<local report path>'
dotnet test tests/CropQc.Api.Tests --filter 'FullyQualifiedName~InventoryCommand|FullyQualifiedName~InventoryAvailability'
```

The required full-suite run is justified by the shared canonical contract, common EF model and explicit user instruction. Legacy optional provider/environment tests remain separately reported; their absence is not presented as completed coverage. Exact gate results and the final commit belong in the PR validation record.

### Validation record — 2026-10-01

- Restore and solution build passed; build reported 0 errors and 63 existing warnings.
- Final full suite: **2,039 passed, 0 failed, 14 optional environment skips**. Within that run, **48 Phase 2 tests**, **40 Phase 1 tests**, and all **six PostgreSQL races plus simultaneous idempotency** passed. The restored acceptance test and all eight rollback stages were enabled.
- Regression groups in that run: 69 Truck Receipt, 51 Processor and 128 ActualRun tests passed. These are suite-reported counts, not a claim that every optional legacy provider branch was enabled.
- Supplemental PostgreSQL Truck Receipt tests: **9 passed** on an isolated migrated backup clone (six routing cases, stale version, rollback and backup manifest/schema compatibility), plus **1 passed** on an empty current-schema fixture (grandfathering lifecycle). No skips or failures in those supplemental runs.
- Five skipped test methods in the full run are covered by those supplemental ten cases. Nine optional methods remain unexecuted: three release-schema contract, two historical 15-load adoption, old-schema worker compatibility, legacy GrowerLot provider reconciliation, receipt-date provider accounting and FruitProfile provider concurrency. They require distinct legacy/provider fixtures, not the Phase 2 backup/command fixtures. Their existing ordinary regression coverage ran; no zero-skip recertification is claimed.
- An earlier extended run used the migrated production-shaped fixture for grandfathering, whose premise is no adopted loads; another used an empty generated schema for the worker migration-history gate. Both fixture mismatches were resolved by the separate suitable fixtures above without relaxing a guard or changing those tests.
- EF pending-model check, changed-file whitespace verification and `git diff --check` passed. Local additive migration passed; an unused-schema down/up rehearsal preserved all 684 original segment defaults and quantities; downgrade after command history was correctly refused.
- Verified Backup #176: 15,738,099 bytes, SHA-256 `74ecb8db10290b44b04366513b6ec295a56141b2b2c51c009abd50ea85a20208`. Full backup and command evidence remains in local verification storage, not source control.
- No GitHub Actions checks were configured/reported on #259 at validation time. No WinForms changes or MSI requirement. No production mutation or workflow cutover.

## Phase 3 boundary

No Web/API controller/service registration, receiving workflow, dump workflow, transfer workflow, background producer or operational feature flag calls this executor. Phase 3 must migrate receipt/import producers, ActualRun edits/reversals, room/warehouse/outside/inter-company transfers, processor workflows, treatment services, corrections and returns together with authorization, matching, commercial metadata and post-commit notification adapters. No merge, production migration, deployment, inventory repair or fake business operation is part of Phase 2.
