# ORAS historical product definition correction

## Problem and scope

ORAS means Organic Asian Pear: `FruitType = Pear`, `ProductionType = Organic`, `IsOrganic = true`. The existing master definition was created as Conventional; receiving and historical snapshots faithfully copied that incorrect classification. The generic Master Data guard correctly refuses an identity change after operational use, but there was no product-definition correction covering historical snapshots.

The requested correction includes current **and historical** references. It is not a new conventional-to-organic business event. Reusing the same FruitProfile preserves receipt, lot, treatment and movement relationships. Creating another profile and leaving old runs Conventional would not satisfy this requirement.

Affected workflows: product configuration, Web/API receiving, Truck Receipt variety rows, receipt edits, canonical definition correction, historical run/expectation reporting, treatment lineage and subsequent reversals. Shared persistence validation is also changed. The user requested full-suite results; focused correctness tests precede that broader run.

## Implementation

- Master Data derives Pear/Organic/true for ORAS. Existing in-use identity protection remains intact.
- Shared persistence validation prevents new/modified ORAS definitions and received/imported receipt identities from silently remaining Conventional. Web/API show an actionable correction message; the canonical receiving executor validates the product definition before writing.
- A variety administrator uses **Master Data → ORAS → Review the audited ORAS correction**. GET only builds a repeatable-read preview. The page lists record IDs and every changed field, and requires a reason and explicit confirmation that history will also be corrected.
- POST invokes `CorrectOrasDefinition` through the existing canonical executor. It requires canonical mode, active administrator authorization checked again in the executor, an exact preview fingerprint and unchanged original Conventional ORAS definition. One Serializable transaction owns profile, snapshot/key updates, per-record before/after audits and the command journal.
- Short NOWAIT table locks precede the executor's first read. A competing writer causes a refresh/retry response. A stale fingerprint rejects the whole correction. No guards are loosened on retry. Replay is idempotent; a different intent using the same key conflicts.
- The command updates classification snapshots and lineage keys, including consumed and retired history. It leaves quantities, locations, timestamps, actors, receipt and movement links, treatment signatures/applications and original audit records intact. Where an old redundant status literally says Conventional, it becomes Organic consistently. Modified versioned records increment their concurrency version.
- No physical effects, compensating ledger amounts, fake movements, stock additions, consumption, projection normalization or new FruitProfiles are created.
- The new optional command payload is omitted when null, preserving the byte representation and idempotency hashes of existing ordinary commands.

No schema migration or installer change is required.

## Reviewed production scope (read-only, 2026-10-06 UTC)

Only FruitProfile **30** has code ORAS; its name already says ORGANIC ASIAN PEAR, commodity Pear, but ProductionType is Conventional and IsOrganic is false. Creation audit **129470** recorded that combination on 2026-09-17 at 15:41:41.455815 UTC. Audits 129506/129507 deactivate/reactivate it without correcting classification. ORAS is not a seeded/hard-coded Compu-Tech translation. Its business definition is the user's explicit rule.

All four receipts use GrowerLot **10**, lot **1125**, crop **2026**, FruitProfile **30**:

| Receipt | Receipt number | Location | Received | Current |
|---|---|---|---:|---:|
| 1809 | TR509222 | DH / DH-1 (warehouse 2, room 33) | 103 | 103 |
| 1826 | TR509228 | DH / DH-1 | 21 | 21 |
| 1864 | TR509244 | DH / DH-1 | 110 | 110 |
| 1891 | TR509248 | WP / WP-8 (warehouse 4, room 5) | 128 | 54 |

Accounting: **362 received − 32 − 42 consumed = 288 current** (DH-1 234, WP-8 54). The current explicit WP-8 segment is 649, quantity 54, version 4. DH-1 is supported by receipt/ledger evidence, with no explicit ORAS segment. There are no ORAS room transfers, intercrew/outside/processor transfers, losses, room-depletion rows, treatment applications, Truck Receipt reconciliation variety rows, prior identity corrections, saved run projections or packout reports. All four are ordinary truck receipts, not transfer-reconciliation receipts. No conflicting ORAS-coded ledger, segment, run or intercrew record referencing another profile was found.

All four receipt identities qualify for the single guarded definition correction, subject to a fresh production preview. The already consumed 74 bins also require historical classification correction; they are never restored to stock.

### Exact proposed changes at the reviewed state

| Table / ID | Fields before → after |
|---|---|
| FruitProfiles / 30 | ProductionType Conventional → Organic; IsOrganic false → true |
| BinsRunEntries / 402 | ProductionTypeSnapshot Conventional → Organic; IsOrganicSnapshot false → true |
| BinsRunEntries / 434 | Same classification fields |
| RunExpectationSources / 326 | Same classification fields |
| RunExpectationSources / 358 | Same classification fields |
| TreatmentLineageSegments / 649 | Same classification fields; IdentityKey changes below; ConcurrencyVersion 4 → 5 |
| TreatmentLineageMovements / 664 | IdentityKey changes below |
| TreatmentLineageMovements / 749 | IdentityKey changes below |

Key: `2026|10|30|1125|1125|ORAS|CONVENTIONAL|False|` → `2026|10|30|1125|1125|ORAS|ORGANIC|True|`.

The two BinsRun entries retain quantities 32/42, ActualRun IDs 124/135, revision IDs 147/158 and ledger IDs 3552/3890. Historical movements retain those quantities and their source segment 649. Segment quantity remains 54. Receipt-add ledger rows 3224/3249/3296/3327 and both consumption ledger rows are unchanged. Receipts keep their existing IDs, quantities, timestamps and links, and resolve Organic through profile 30.

Expected: **eight updated records**, **eight `OrasHistoricalDefinitionCorrection` before/after audits**, **one `CanonicalInventoryCommand` audit** and **one immutable command-journal entry**. Zero normalization audits or physical command effects. Existing audit history, including the original incorrect values, is retained.

## Execution plan — not executed in production

1. Release the reviewed application through the repository's normal backup/release gates. No migration is needed.
2. An authorized administrator opens the ORAS correction preview. Verify profile 30 and the exact eight-record scope above. A changed production scope requires fresh review; the implementation does not hard-code receipt IDs or quantities.
3. Submit the reviewed fingerprint, stable operation key, timestamp, reason and historical-correction confirmation. The executor repeats all scope checks under locks in its Serializable transaction. Block if canonical mode is off, permission is absent, profile classification changed, scope changed, identity evidence conflicts, or another writer holds an incompatible lock.
4. Verify the eight updates/nine audits/one journal record; compare all physical and protected-history fingerprints. Confirm 234 + 54 = 288, consumed quantities remain 32 + 42 = 74, and historical run/expectation/movement references now show Organic.
5. Validate affected room, receiving and historical run pages and canonical selectors. Existing sealed-room restrictions stay in force; correcting classification does not unseal a room or authorize inventory movement.

The command deliberately blocks conflicting profile/reporting keys, existing identity remaps, saved projection selection keys and packout reports that need a separate reviewed correction/supersession. None exists in the reviewed production scope. It never silently leaves such dependent history Conventional and reports success.

## Data-integrity tests

`OrasDefinitionTests` exercises the normal admin controller on a disposable clone of verified Backup #178. The restore contains the same four ORAS receipts, 74 consumed bins and eight erroneous records observed live. Full-table fingerprints cover every unrelated table/row and all original audits. Separate column fingerprints prove unchanged quantities, locations, timestamps, actors and relationships even within the eight changed records. Both canonical room pools resolve Organic with 234/54 authoritative and eligible bins after correction.

Additional tests cover unauthorized direct execution and UI access, stale previews, complete rollback before commit, replay/conflicting replay, no projection normalization or stock recreation, normal Web/API receiving, Truck Receipt enabled, imported receipts, the generic Master Data blocker and future Conventional reversion prevention. A historical room move is corrected and then reversed through the normal room service; a real application-service Truck Receipt completion/reopen is exercised in disposable test storage.

Backup #178 is an existing development restore, not a new release-window backup. Fresh read-only production diagnostics establish current scope; production correction still requires its own release gate and fresh guarded preview. No production write, deployment, email or real inventory operation is part of this implementation task.

### Recorded verification

- Dependency restore and solution build passed (zero build errors; existing repository warnings remain).
- Final focused suite: **67 passed, 0 failed, 0 skipped**, including nine ORAS tests, master-data protection, canonical architecture, receiving, receipt identity correction, room moves, Truck Receipt and selectors.
- Full suite: **2,214 passed, 0 failed, 0 skipped**. This run preceded the final null-payload serialization compatibility guard, clearer ORAS edit message and two additional tests; the final 67-test focused run covers those final changes. No second broad recertification was needed for those bounded changes.
- The new treatment test preserves a real application-service treatment assignment through definition correction, then reverses it normally without changing physical stock. The command-serialization test preserves existing intent hashes by excluding the new null payload.
- Migration/model check: no pending model changes. Changed-file formatting and `git diff --check` passed.
- Production-shaped restore: eight changed records, unchanged protected row/column fingerprints, eight correction audits, one command audit, one journal record and zero correction-triggered normalization.
- Writer coverage: all 32 original workflows remain classified; zero ordinary writers are classified as bypassing canonical execution. New plan/executor sources are explicitly reviewed in the candidate inventory.
- New UI is compiled and its normal controller path is exercised against the isolated restore. No production browser write or production correction was tested/executed.
