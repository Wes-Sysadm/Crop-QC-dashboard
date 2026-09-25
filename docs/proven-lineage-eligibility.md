# Evidence-based eligibility for historical shared treatment projections

Base: current post-#252/#253/#254 main `0f4456ea7c4e9d48456240a9f0feeb912c2cb57d`.

## Affected area and safety scope

Change the shared RoomTreatmentService selection/materialization path and its receipt true-up consumer. Direct consumers include room/bulk transfers, outside-warehouse transfers, processor shipments, inter-crew dispatch, bins runs, losses, treatment application, receipt correction and inventory displays. Preserve receipts, inventory ledger quantities, all existing movements/applications/links and audit history. The normal movement itself still performs its existing authorized inventory debit/credit. No destination-specific eligibility rules or UI warnings are added. Full-suite validation is justified both by this shared inventory dependency and the user's explicit request.

Production was queried only through read-only transactions. Nothing was merged, deployed or reconciled in production. This is not a mass repair of the 26 audit findings.

## Evan03: lot 9722

Current location: warehouse 1 (EBS), room 13 (EVANS-3), crop 2026, GrowerLot 648, FruitProfile 2, Gala/GALA, Conventional/non-organic, MFR-D SLUSARENKO CONV.

The previous treated Receipt 986's 44 bins left completely in transfer 232 / movement 128. Its confirmed segment 201 is zero; application 18 and its history remain intact. The current untreated occupancy consists of Receipts 1390 (24), 1396 (17), 1427 (7), plus transfer 361 (10, movement 470, source segment 525 to 200), then outside-warehouse transfers 15 (30, movement 560) and 22 (9, movement 632):

`24 + 17 + 7 + 10 - 30 - 9 = 19`.

Segment 200 holds 10 untreated shared bins under the blank status key. Segment 590 holds 19 untreated shared bins under the redundant CONVENTIONAL status key. Both have version 4 and no Receipt-specific attribution/application links. Segment 590 was materialized as 58 current bins before its 30-bin debit; it already included the earlier 10 represented in segment 200. It then lost 9 more. Thus `10 + (58 - 30 - 9) = 29` is duplicate projection, not 29 physical bins. All 10 excess bins are accounted for by segment 200's duplicate representation of transfer 361.

Projected plan: segment 200 `10 -> 0`, version `4 -> 5`; segment 590 stays `19`, version 4. Keep both original identity keys, all movement rows and receipt/treatment history. The first authorized movement persists the proof audit and then consumes the requested part of the 19 bins.

## Lamb14: lot 9682

Current location: warehouse 1 (EBS), room 7 (LAMB-14), crop 2026, GrowerLot 642, FruitProfile 2, Gala/GALA, Conventional/non-organic, MFR - JWO-WINESAP CONV.

Receipts 1304 (16), 1311 (40), 1392 (53), 1399 (2) supplied 111 bins. Actual run 73 removed 83 (ledger 2420, bins-run entry 242, movement 328, segment 401), leaving 28. Transfer 351 brought in 32 (ledger 2519, movement 366, segment 429). Actual run 88 removed 32 plus 28 (ledger 2918/2919, bins-run entries 304/305, movements 491/492). Both depletion movements reference segment 429, although that segment initially received only 32: the historical alias/materialization behavior recreated the 28 there and left segment 401's original 28 projection behind.

`16 + 40 + 53 + 2 - 83 + 32 - 32 - 28 = 0`.

Receipt 1913 subsequently added 27 (ledger 3349). Those are the current bins. Segment 401 still shows 28; segment 429 is zero. The displayed difference is `28 - 27 = 1`, but the historical explanation is **28 depleted bins projected against a new 27-bin occupancy**, not one arbitrarily identified extra physical bin. No current receipt treatment or room-wide treatment applies to this new occupancy. Older receipt-specific applications 2/3/5 belong to other receipts and are preserved.

Projected plan: reuse shared current-projection segment 401 as `28 -> 27`, version `3 -> 4`, with an audit recording the empty boundary and Receipt 1913 as the current evidence. Original segment metadata, null Receipt link and historical movement records remain. Segment 429 stays zero. This updates a current projection; it does not rewrite the historical run or claim that the original 28 bins survived.

Both examples already appeared in the previous 21-overcount audit. Lot 9682 also has a separate EVANS-05 discrepancy; it is not included in the Lamb14 normalization.

## Root cause and code path

RoomInventoryLedgerQueryService calculates authoritative inventory from normalized RoomInventoryAdjustments, accounting for the established baseline and identity rules. RoomTreatmentService.ProjectSelectionsBatchAsync previously summed all nonzero matching segment CurrentBins before any historical reconciliation. A negative implicit remainder produced one unavailable NeedsReview choice. DashboardDataService surfaced that choice as unavailable; ProcessorShipmentService and OutsideWarehouseTransferService used the same selection method. MaterializeAsync independently rejected an explicit overcount on writes.

PR #251 stopped new status-alias omission and split-allocation recreation, but did not automatically reconcile existing overcounts. PR #253 fixed a different path: explicitly reviewed Grower Lot identity reclassification with stale status replacement and proven untreated backfill. Ordinary room/processor selections never invoked that reviewed identity-correction path. Calling it automatically would perform unrelated identity/ledger corrections, so this change reuses its audited transactional reconciliation principles without inventing a Grower Lot correction or adjusting inventory.

## Systemic behavior

1. Read authoritative current inventory using the existing shared ledger service.
2. Only for an overcount, require exact crop/grower-lot/lot/profile/variety/production-type/organic/room/warehouse identity and shared untreated segments with no application links. Redundant production-type status aliases use the existing normalization. Malformed keys, Hold/conflicting statuses, negative segments, receipt-specific allocation and mixed/unknown treatment remain blocked.
3. Replay the exact ledger position without negative intermediate balances. Refuse imported baselines or missing/inferred identities. The final sum must equal the supplied authoritative snapshot. Locate the current occupancy after the last zero boundary.
4. Prove every arrival in that occupancy: exact ordinary receipt and quantity, or exactly matching untreated incoming room-transfer movement evidence. Refuse transfer-evidence receipts, deleted receipts, unsupported adjustments/returns, missing or inconsistent incoming evidence and applicable treatment history. An overcount or a uniform segment signature alone is never proof.
5. Project one stable retained shared segment at authoritative quantity; other historical shared projections project as zero. Read-only GET/selection does not save any changes.
6. On an authorized write, re-prove and persist within the existing movement/correction transaction (serializable fallback for standalone materialization). Increment changed segment versions and create NormalizeHistoricalTreatmentLineage audit with before values, after values, authoritative quantity, ledger IDs, receipt IDs, incoming movement IDs and occupancy boundary. Audit failure aborts normalization; the processor rehearsal proves the entire parent shipment rolls back.
7. Preserve historical segments at zero rather than deleting them. No existing movement, application, receipt, ledger or audit is changed by normalization. Repeating an already-normalized operation adds no reconciliation audit.

Tree Top is an Outside Warehouse in restored production configuration. Its actual outside-transfer path and the separate Processor Sale service both use this shared rule. No Treetop/EBS/WP/DH/McDougall special case is present. Receiving-related treatment correction and true-up also materialize the read-only proof before writing. Existing Truck Receipt logic is unchanged.

## Read-only classification

8 of 21 historical overcounts satisfy the deliberately bounded shared-untreated proof. Two have positive current inventory (the reported examples); six are depleted positions. Read-only classification does not retire any production row, and the normal positive-inventory selector does not perform a mass cleanup of depleted positions. Thirteen require separate review under these rules; that is not proof that all thirteen are intrinsically unsafe or one common defect. Three include receipt-specific segments (EVANS-10 lots 9541/3290, WP-7 lot 1372). Two include deleted arrival receipts (WP-5 lot 1084, Receipt 1339; DH-15 lot 2350, Receipt 1373), which the proof refuses to infer through. The other eight fail the stricter ledger/arrival provenance proof. No blanket repair is proposed.

| Site / room | Lot | Explicit | Authoritative | Classification |
| --- | --- | ---: | ---: | --- |
| WP / WP-5 | 1084 | 130 | 10 | Requires separate review |
| EBS / EVANS-10 | 3050 | 48 | 0 | Proven shared untreated |
| EBS / LAMB-14 | 9040 | 228 | 0 | Requires separate review |
| EBS / LAMB-14 | 9100 | 6 | 0 | Proven shared untreated |
| EBS / LAMB-15 | 9380 | 40 | 0 | Requires separate review |
| EBS / EVANS-3 | 9722 | 29 | 19 | Proven shared untreated |
| McDougall / MCD-08 | 1600 | 252 | 0 | Proven shared untreated |
| EBS / LAMB-14 | 9380 | 3 | 0 | Requires separate review |
| EBS / LAMB-14 | 9682 | 28 | 27 | Proven shared untreated |
| DH / DH-15 | 2350 | 598 | 202 | Requires separate review |
| EBS / EVANS-05 | 3152 | 162 | 61 | Requires separate review |
| EBS / EVANS-05 | 9682 | 536 | 252 | Requires separate review |
| EBS / EVANS-11 | 9092 | 10 | 0 | Proven shared untreated |
| EBS / EVANS-05 | 9401 | 8 | 0 | Requires separate review |
| EBS / EVANS-6 | 9691 | 1 | 0 | Proven shared untreated |
| EBS / EVANS-10 | 9541 | 4 | 0 | Requires separate review |
| EBS / EVANS-10 | 3290 | 35 | 0 | Requires separate review |
| EBS / EVANS-10 | 9414 | 6 | 0 | Proven shared untreated |
| WP / WP-8 | 2350 | 362 | 170 | Requires separate review |
| EBS / EVANS-10 | 9820 | 3 | 0 | Requires separate review |
| WP / WP-7 | 1372 | 1568 | 1122 | Requires separate review |

The five negative ledger positions remain separate: MCD-08 lots 1270 (-40), 1538 (-18), 2822 (-5); MCD-09 lots 1537 (-20), 1539 (-44). No evidence here authorizes or implements their repair.

## Restore and verification scope

The user chose the existing verified Backup 167 instead of approving a new full production export. A fresh disposable PostgreSQL restore reverified the 14,574,476-byte package, SHA-256 e7446e2c60089f0a0304ea4839229983309919e42fb47b36f9fa0537fa69da06, archive and component hashes, and successfully restored its SQL. Snapshot began 2026-09-25 01:55:21 UTC and predates Truck Receipt activation. The already-reviewed Truck Receipt schema and 15-load adoption were applied only to the disposable rehearsal copy.

All inspected two-lot records match current read-only production exactly after timestamp normalization: 134 ledger rows, 59 receipts, 34 segments, 30 movements, 24 transfers, 8 applications, 11 application links. This establishes freshness for these cases, not for every production table.

The unchanged release binary at 0f4456e reproduced 29/19 and 28/27 NeedsReview states on a separate fresh restore. The candidate shows all 19 and 27 available without writes. Real service calls on the disposable copy exercise Tree Top outside transfers, exact reversals/returns, subsequent Processor Sale consumption, idempotency and injected audit failure. Current-source balances become zero after consuming 46 bins; no phantom lineage remains. Original receipt/treatment/movement/ledger history and unrelated segment fingerprints are unchanged. The 15 adopted Awaiting Receipt loads, 630 bins, are protected by full-row fingerprints.

Final validation: 1,988 passed, zero failed, two optional PostgreSQL tests skipped, total 1,990. The two optional skips are FruitProfileIdentityGuardPostgreSqlTests and ReceiptDateBaselineTests; the configured room-transfer, legacy normalization, Truck Receipt schema/adoption/pre-feature and restored-example PostgreSQL tests actually ran. The focused provider/receiving run passed 90 tests; the production-restore test was exercised in the final full run. Restore, solution build, formatting verification, model consistency and diff checks passed. Existing nullable warnings remain; no migration is needed.

Twenty new reconciliation cases and one receiving true-up case cover shared untreated overcounts, site/organic identity, count-only rejection, mixed/unknown/receipt-specific treatment, malformed/conflicting keys, identity mismatch, unsupported return/transfer evidence, application links, unprojected treatment applications, contradictory movement treatment evidence, negative lineage, balanced inventory, unrelated lots, audit atomicity and both actual movement services. Existing transfer, receiving, identity, Truck Receipt, PostgreSQL concurrency and schema tests provide the adjacent coverage. Earlier bulk and receiving UI-binding fixtures used proven shared overcounts as a blanket blocker; the bulk assertion now requires that inventory to move, while genuinely receipt-specific ambiguity still blocks and remains unchanged. A SQLite query translation failure found during validation was fixed by fetching bounded applicable application rows and comparing DateTimeOffset values in memory.

The deleted-receipt and contradictory-movement guards are independent. Both reported lots pass them; the two deleted-receipt exclusions above explain why the final classification is eight rather than the preliminary ten. No authenticated production browser or new production transfer was used as a test in this development task.

## Deployment and remaining work

No schema migration or WinForms/MSI change. No one-time SQL repair is required for the two examples when their proof still holds: read-only eligibility projects the safe quantity and the next genuine authorized movement persists the audited normalization naturally. Conditions are evaluated against then-current data; changes or ambiguity fail closed.

Prepare/review this PR before any deployment. A release requires a fresh verified standard production backup and affected-workflow smoke checks; do not merge or deploy automatically. Keep Truck Receipt ON and retain the post-#252 schema/launcher/index contract. Never deploy pre-#252 application code. The completed Bartlett repair/audit 145038 and all 26 discrepancies were untouched in production. Further review of the thirteen unproven overcounts and five negative positions is separate work, not an automatic follow-on repair.
