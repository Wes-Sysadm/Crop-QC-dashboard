# PR 271 custody corrections and integration review

PR [271](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/271) now supports audited corrections of partial acknowledgements and proven room placements. Original dispatches, acknowledgements, placements and treatment history remain intact. A correction creates linked compensation; it never assumes that fruit physically returned to its dispatch source. This is a human-review candidate, not production release approval.

Verification ran October 7–8, 2026 UTC. Production access consisted only of schema and receipt 2161 evidence SELECTs. All migrations, receipt mutations, backup regression operations and browser submissions ran on isolated localhost PostgreSQL databases. No production data repair, backup run, deployment or PR merge was performed.

## A Reversal workflow

`ReverseReceiptAcknowledgment` compensates only currently receipt-held quantity. It decreases net acknowledged quantity and increases unresolved quantity on the same original dispatch allocation. It creates no room ledger entry and no source credit. A receiver can then acknowledge the correct original allocation. Under-acknowledgement uses the ordinary acknowledgement workflow for the remaining allocation.

`ReverseReceiptPlacement` requires the exact original placement movement, room debit, current segment, quantity, receipt identity, treatment signature and application membership. It debits that room and restores receipt-held custody. A separate placement command selects an active, unsealed room in the receiving warehouse. Completed receipts can reopen safely through this compensation when their original placement remains untouched.

Both commands require receiving edit access, facility access, antiforgery validation, a reason, positive quantities, exact unique allocation IDs, current receipt/transfer versions and canonical journal ownership. The serialized command, operation key, immutable compensation, movement, ledger and audit commit in one serializable transaction. Duplicate/replayed requests do not repeat the effect; a lost response after commit resolves through the journal.

Later movement, loss/consumption, treatment, identity change, missing lineage or uncertain custody blocks placement compensation. The error directs the operator to the corresponding room movement, treatment reversal or consumption correction workflow. Untouched receipt-held bins remain correctable even when other bins from the load have moved. There is no force override or automatic downstream reversal. Resolving a dependency does not guarantee that the original segment is reusable; retired/reconstructed segments still fail closed and require an explicit current-custody resolution.

The new `ReceiptCustodyReversals` table links each compensation to its acknowledgement and, for a placement compensation, its original placement plus exact debit/movement. Originals cannot be edited or deleted through the canonical persistence boundary. Parent receipt and transfer summaries are updated, with before/after audit quantities.

## B Conservation evidence

For each load, **dispatched = unresolved + receipt-held + net placed**. Net acknowledged is held plus placed. Compensation does not change original dispatch quantity or allocation identity. A load settles only when its observed receipt, acknowledged quantity and placed quantity all equal the original load.

The backup 184 rehearsal created a new, isolated 50-bin receipt and WP-to-EBS dispatch, then persisted this sequence:

| Operation | Quantity | Held | Placed | Unresolved | Sum |
|---|---:|---:|---:|---:|---:|
| Acknowledge | 49 | 49 | 0 | 1 | 50 |
| Place | 48 | 1 | 48 | 1 | 50 |
| Reverse held acknowledgement | 1 | 0 | 48 | 2 | 50 |
| Undo proven placement | 1 | 1 | 47 | 2 | 50 |
| Place again | 1 | 0 | 48 | 2 | 50 |
| Acknowledge remainder | 2 | 2 | 48 | 0 | 50 |
| Place remainder | 2 | 0 | 50 | 0 | 50 |

The dispatch debit stayed at 50 throughout. Final placement was exactly 50. No source restoration, loss, sale or shortage settlement was inferred. Focused PostgreSQL tests additionally compare original dispatch rows, per-room ledger quantities, authoritative held/transit readers, readiness and audit evidence after each operation; rejected operations and read-only readers preserve table fingerprints.

The downstream-loss regression exposed a separate mismatch within the affected dependency path: canonical loss ledger entries used the grower number in the grower-name field while their loss parent used the name. Loss and loss-reversal entries now retain the parent's grower name, allowing strict parent identity validation without changing quantity or relaxing the verifier.

## C Treatment history

Acknowledgements select exact dispatch movement IDs, preserving original receipt, grower lot, variety and treatment evidence. Placement inherits only those source application memberships. Placement compensation uses the same receipt, identity and treatment signature as the original placement; it creates no treatment application.

The mixed-load regression uses the same variety across distinct grower numbers/lots with treated and untreated allocations. It acknowledges the wrong treated allocation, fully compensates it, then acknowledges the correct untreated allocation. Its resulting held inventory remains untreated. This exposed and corrected a reader defect: receipt-held resolution previously passed every treatment application on a load into each identity group. It now passes only that group's source applications, so a fully compensated allocation cannot contaminate another lot's treatment proof.

Additional tests cover treated fruit held while a room is sealed, original application inheritance after placement/replacement, and rejection after later treatment. Quantity-only receipt increases retain the receiver correction policy already implemented in PR 271: new additional fruit is untreated and does not gain historical treatment membership.

## D Company and facility boundaries

The canonical workflow verifies the matched receipt, configured inter-company route, destination custody group, crop year, observed variety quantities and exact dispatch allocations. Placement must remain in the receiving receipt's warehouse. The new wrong-company destination regression rejects the operation without writes. WP-to-EBS dispatch/partial settlement is also exercised on the restored snapshot. Existing Truck Receipt provider tests exercise EBS with WP, DH and McDougall in both directions.

Custody compensation never alters the source grower/lot or treats a destination room selection as authorization to change company ownership. Cross-warehouse movement remains a separate explicit workflow.

## E PR 270 integration

The starting PR 271 head was `eb8c3d8cbe22f099eb302bf48481218d2936bdf7`, based on `95518dec131d44b01e2dc8fcfcbb82f508a8f399`. PR 270 was already merged by another action at `2026-10-07T23:47:52Z`; its main merge commit is `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. This current main was integrated into the existing `codex/authoritative-partial-custody` branch. No dependency merge remains for PR 270.

Overlaps were the deduction invariant service, its writer-review hash, and receipt override fixture expectations. The invariant conflict was resolved semantically: canonical identity corrections require `InventoryIdentityCorrection` and the complete strict PR 270 contract; separate location corrections retain their legitimate type. The verifier still requires the canonical command/hash/journal, exact linked ledger and movement IDs, timestamps, source positions, treatment memberships, receipt snapshots and before/after identity proof. Its 28 strict regression cases are retained. Custody net quantities are validated separately rather than weakening canonical identity validation.

Fresh read-only production evidence for receipt 2161 showed 64 bins, version 1, override `d835a348-5f86-43c6-874f-d8c82c2036e9`, correction `91293a15-3514-48f5-9c37-6bcf40c91e65`, adjustments 4172/4173 (-64/+64 in room 4), and movements 855/856. The exact persisted override, correction, command, ledger and movement payloads were replayed offline through the integrated verifier: one negative adjustment, zero blocking issues. This proves this contract accepts the legitimate 64-bin correction; it is not a new full-production readiness run.

PRs 268 and 269 remain overlapping work already incorporated into PR 271. They must not be independently merged without resolving duplication. Their legacy arrival proof, transaction retry and historical treatment protections remain in this candidate. The reviewed writer inventory now covers 97 source candidates, 32 workflow families and 36 receiving/custody writer entries.

## F Rehearsal on backup 184

The existing verified backup package was revalidated locally before restoration: 16,429,674 bytes; SHA-256 `443eb7def03a0d815c3b31e16512c8e9385b644ec6200823555ac02ffed72eac`; snapshot timestamp `2026-10-05T08:00:54.881257Z`. ZIP CRC, all four component hashes, manifest and full gzip SQL readability passed; the SQL expanded to 302,728,591 bytes. The package contained 11,527 photo references. Live photo object availability was not rechecked remotely.

The snapshot was restored to isolated PostgreSQL 18.6. Because it predates canonical command storage, the reviewed `CanonicalInventoryCommands` and `CanonicalIdentityRestorationSides` prerequisites were applied before the three custody migrations. Historical row fingerprints stayed unchanged across the custody upgrade and the new 50-bin workflow. The comparison includes original record keys and original column values; additive canonical metadata defaults are excluded from the legacy segment fingerprint. New rehearsal rows are allowed and separately reconciled.

Full deduction readiness read 1,063 negative adjustments before the workflow (93 historical, 970 new-format) and 1,065 afterward (93 historical, 972 new-format), with zero blockers both times. The same six legacy `NoParent` advisories, adjustment IDs 2–7, remained. Readiness passing does not mean historical inventory is discrepancy-free.

| Historical evidence preserved | Snapshot result |
|---|---|
| In-transit transfers | 20: IDs 1–15 and 38–42 |
| Segment status values | NULL 624; Organic 36; Conventional 54 |
| Authority versus raw projection differences | 296 positions, retained without repair |
| Negative authoritative positions | Five, detailed below |
| Receipt 2161 | 64 bins, version 0; predates its October 7 correction |
| TR110059 | Absent; snapshot cannot reproduce its actual production timeline |

All negative positions are crop 2026, warehouse 3. Room 60: grower lot 141/profile 21/lot 1270/ORDA -40; grower lot 477/profile 29/lot 1538/RDAN -18; grower lot 502/profile 18/lot 2822/DANJ -5. Room 61: grower lot 476/profile 18/lot 1537/DANJ -20; grower lot 478/profile 18/lot 1539/DANJ -44. These are snapshot observations, not claims of current production corruption.

Evidence-grounded synthetic regressions and receipt 2161's read-only current evidence supplement the snapshot's date limitation. The October 7 receiving investigation remains in [receiving-placement-investigation-2026-10-07.md](receiving-placement-investigation-2026-10-07.md); this rehearsal does not retrospectively identify TR110059's unavailable original exception or authorize historical repairs.

## G Browser and operator verification

The actual Razor receiving controller/view ran through an ASP.NET test host backed by disposable PostgreSQL, with outbound backup/email work disabled. The supported Codex in-app browser exercised viewports 1440×1000, 768×1024 and 390×844.

Desktop: selected an exact treated allocation, acknowledged 18 of 19 bins and placed 17. The view displayed original receipt/lot, treatment product/date, dispatched/acknowledged/placed/held/unresolved quantities and correction controls. Tablet: compensated one held bin, yielding 17 acknowledged, 17 placed and two unresolved. Mobile: compensated one proven placement, yielding 16 placed, one held and two unresolved. A zero-allocation submission showed “Select each proven allocation once with a positive quantity.” Tables scroll within their container on mobile; the page had no horizontal overflow. A text-encoding defect found during inspection was corrected before these final checks.

These are viewport checks in one supported browser, not physical device or Safari certification. Browser navigation after a POST retained current custody without repeating the operation. Exact duplicate POST replay, stale versions, denied receiving permission (403), and missing antiforgery tokens (400) are verified by the HTTP integration test; no claim is made of a browser-level double-click timing test.

## H Migration safety

The original `20261007212154_ReceiptHeldTransferCustody` remains additive. New migrations are `20261007235352_ReceiptCustodyCompensations` and `20261007235822_ReceiptCustodyGuardrails`. They add reversal evidence, restrictive foreign keys, unique movement/debit links, operation uniqueness, positive-quantity constraints and the rule that placement reversals require both physical evidence links while held reversals require neither.

A PostgreSQL partial unique index also protects held compensation by operation/acknowledgement where placement is NULL. There is no historical backfill. Tracked writes and raw SQL custody writes stay behind the canonical write guard. Startup schema checks include the new reversal table and columns. EF reports no pending model changes.

Read-only production inspection at `2026-10-08T00:10:30.698042Z` confirmed PostgreSQL 18.4, canonical command storage and canonical segment metadata, with no custody acknowledgement table yet. Local upgrade/rollback tests and restored-snapshot rehearsal validate the relevant schema shape. They do not constitute an execution of migrations on current production. Failed operations prove atomic rollback; unknown commit outcomes prove exactly-once replay.

## I Rollback boundary

After the first acknowledgement, preserve all additive custody schema. The guardrail migration refuses downgrade when acknowledgement history exists; the compensation migration also refuses removal when reversals exist. Empty-schema rollback and upgrade were tested.

On PostgreSQL, four physical inventory tables have a transaction-scoped writer fence: room adjustments, lineage segments, lineage movements and treatment applications. Once custody history exists, writers must identify the compensation-aware canonical executor. An older writer's attempted no-op update is rejected in the migration test; the current executor still succeeds. Backups and read-only queries do not require the writer capability.

The old PR 271 head `eb8c3d8cbe22f099eb302bf48481218d2936bdf7`, PR 270/main, and older applications are **not safe rollback targets after first use**. The rollback target must include this candidate's net reversal readers, commands and writer capability, or a future version proven compatible with them. The SQL fence blocks physical writes, not every stale read or metadata-only operation an old binary might attempt. The release process must prohibit deployment of older binaries; disabling the feature flag is not a compatible rollback. SQL Server was not the production provider exercised for this database fence.

## J Verification results

**Final complete run: 2,359 passed, zero failed, 12 skipped; 2,371 total, 5 minutes 18 seconds.** The final source also passed solution build, EF model consistency, formatting and diff checks. Evidence is retained in the local `completion-full-final.trx` test artifact; the exact skips are committed alongside this report.

The complete suite was explicitly requested. Shared canonical execution, custody readers, readiness and persistence guards also justify its breadth. No WinForms or QC Station code changed, so no MSI was built.

Focused custody/correction/HTTP/strict-readiness verification passed **61 tests**, zero failed or skipped. Writer architecture verification passed **9 tests**. These include PostgreSQL persistence, both correction races, both correction replay/unknown-commit paths, precommit rollback, invalid compensation quantities, migration upgrade/empty downgrade/blocked downgrade, wrong-company destination, exact mixed allocations and downstream dependency refusal.

`dotnet restore CropQc.sln`, solution build, EF pending-model check, changed-file whitespace verification and `git diff --check` passed. The initial rebuild reported 58 warnings and zero errors; the final incremental build reported zero warnings and zero errors. Backup package verification and local dump/restore tests ran without contacting the production backup destination. GitHub Actions status is recorded on the final PR; an empty checks list is not a CI pass.

The initial full run reported 2,323 passed, 36 failed and 12 skipped. Failures included missing local administrator database, fixtures created without migration history and an outdated optional legacy override expectation. The next run reported 2,354 passed, five failed and 12 skipped: two sandbox-blocked child processes, two reused data fixtures and a restored-data fixture incompatible with an empty pre-feature assertion. The two backup cases passed when local child-process execution was permitted. Clean fixtures preserve the relevant schema while removing unrelated historical feature-use state. A legacy override test now explicitly asserts that an acknowledged negative-inventory checkbox cannot reduce stock beyond proven current custody, and verifies unchanged quantity/version/ledger on rejection.

The explicit skip list is [pr271-completion-skipped-tests.csv](inventory-architecture/pr271-completion-skipped-tests.csv). Twelve cases require separately shaped historical restores, a pre-feature database, or an optional normalization fixture. Backup 184's successful custody rehearsal does not substitute for those cases' distinct preconditions. The older 34-skip manifest belongs to the earlier PR 271 run.

Additionally, 55 optional test methods return early when their external fixture settings are absent; these appear as passed to xUnit but are **not provider, restored-data, PDF or workbook coverage**. Their 61 guard/settings locations are listed in [pr271-completion-optional-fixtures.csv](inventory-architecture/pr271-completion-optional-fixtures.csv). Relevant canonical PostgreSQL workflows ran through self-contained disposable fixtures; this distinction prevents overstating the broader suite.

## K Policy decisions and remaining limits

No user policy question remains: any receiver may correct a mistaken receipt count, and partial mixed loads require exact allocation selection. Receiving edit/facility permissions remain enforced. A reason provides an audit explanation; it is not external proof of extra stock. A transfer acknowledgement cannot exceed its dispatch or silently credit an overage.

Historical missing bins, negative positions, stale projections and ambiguous treatments require separately reviewed, evidence-backed resolution. None was repaired. Dependency-blocked placement corrections deliberately require explicit current-custody resolution and may need an administrator's investigation when existing operations cannot prove a safe path. Room capacity and effective seal-time questions identified in the prior investigation remain separate policies; this work does not invent commodity segregation rules.

## L Release recommendation

Prepare PR 271 for human code/data-integrity review and keep it unmerged. Release approval must include an exact compatible rollback artifact, the repository's verified predeployment backup gate, current production readiness, migration review, and an operator check of the affected receiving/correction forms. This task does not approve production release or recertify current nightly backup/deployment health.

## Read-only production SQL evidence

Every Render request explicitly selected workspace `tea-d7uc4ippo60c73ebn4mg` and PostgreSQL `dpg-d8cru6reo5us73a2bgt0-a`. Schema inspection used this read-only query (results in section H):

```sql
SELECT current_timestamp AS observed_at, current_setting('server_version') AS server_version,
 (SELECT json_agg(x ORDER BY "MigrationId") FROM (SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" >= '20261001') x) AS recent_migrations,
 to_regclass('public."InventoryCommands"')::text AS command_table,
 to_regclass('public."ReceiptCustodyAcknowledgments"')::text AS custody_table,
 (SELECT json_agg(x ORDER BY column_name) FROM (SELECT column_name,data_type,is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name='TreatmentLineageSegments' AND column_name IN ('Id','Disposition','RetiredAt','RetiredByCommandKey','RetiredQuantity')) x) AS lineage_columns,
 (SELECT json_agg(x ORDER BY "Id") FROM (SELECT "Id","BinCount","ConcurrencyVersion" FROM "Receipts" WHERE "Id"=2161) x) AS receipt2161;
```

The exact canonical evidence extraction was:

```sql
SELECT row_to_json(o) AS receipt_override,
       row_to_json(c) AS correction,
       row_to_json(cmd) AS command,
       (SELECT json_agg(a ORDER BY a."Id")
          FROM "RoomInventoryAdjustments" a
         WHERE a."ReceiptInventoryOverrideId" = o."Id") AS adjustments,
       (SELECT json_agg(m ORDER BY m."Id")
          FROM "TreatmentLineageMovements" m
         WHERE m."InventoryIdentityCorrectionId" = c."Id") AS movements
FROM "ReceiptInventoryOverrides" o
JOIN "InventoryIdentityCorrections" c ON c."ReceiptInventoryOverrideId" = o."Id"
JOIN "InventoryCommands" cmd ON cmd."OperationKey" = o."OperationKey"
WHERE o."ReceiptId" = 2161 AND o."ActionType" = 'InventoryReclassification'
  AND c."IsActive" AND c."IsComplete";
```

Result: one complete correction with two ledger rows and two movements, identified in section E. Raw application intents and receipt snapshots stay outside source control; the report records only the bounded integrity evidence needed for review.
