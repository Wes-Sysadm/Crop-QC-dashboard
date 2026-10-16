# Transfer eligibility capability review

Implementation review against `df977d041dae2d449cf6fe5f8581e1f5cae41947`.
This is an implementation repair under specification 1.2.0, not a policy amendment.

## Cross-facility receipt-origin dependency review (2026-10-16)

Reviewed the focused diff from `176eaddee066ff9b9939e4c7807887260ae5bb8d`:
only `InventoryEvidenceLoader.cs` changes application behavior. Both ordinary and
historical reads now use one bounded, no-tracking receipt-ID lookup
of compact original ReceiptAdd fields, compared to already-loaded receipts. It is constrained
by already-selected receipt IDs, not the current facility/room/lot. Existing
ReceiptId indexes support it. It neither imports those rows into room quantity
accounting nor loads unrelated receipt entities. The mismatch predicate, earliest
revision ordering and snapshot interpretation are unchanged. Watermarks additionally
bind sorted original rows and the selected revision's interpretation inputs, so a
changed contradictory original stales proof even if its interpreted boolean stays
false. Future recorded dependencies retain HistoricalSnapshotUnavailable. Transaction
ownership, aggregate reuse, origin guard and every writer are unchanged. The SQL
tag identifies this dependency lookup for measured query-plan evidence. The first
SQL-join implementation was rejected after a measured 1.8-second nested-loop
regression; the final query performs no receipt join and no per-receipt query.

This supersedes the earlier review's acceptance of an original-room restriction;
the cross-scope failure is documented in
[the production-shaped report](pr284-production-shaped-reverification-2026-10-16.md).
The content-lock update records this inspected read-only capability, not a waiver
of an architecture contract. No policy, schema, migration, production data or
readiness change. Synthetic verification and remaining protected-data gates are
reported separately; this review alone is not release approval.

The writer registry is a reviewed-content lock. The following entries were inspected
before updating their hashes; no classification is an exemption from the runtime guard.

| Path | Reviewed change and capability conclusion |
| --- | --- |
| `InventoryEvidenceLoader.cs` | Calls the bounded historical custody loader inside the existing consistent read transaction. No writer capability or persistence call added. |
| `InventoryEvidenceLoader.RecordedCustody.cs` | `AsNoTracking` parent, ledger, revision and audit reads; local link collection only. Validates exact parent/debit/credit identity, effective time, recorded order and audited same-identity location corrections. No SQL mutation, `SaveChanges`, new origin, or movement-row insertion. |
| `InventoryRecordedCustodyReplay.cs` | Pure immutable evidence input/output. Local memo/visited sets and lists; no database dependency. No projection count supplies custody or treatment. |
| `RoomInventoryLedgerQueryService.cs` | Normalizes grower text with Trim/ToUpper before SQL distinct-count and representative selection, matching the existing canonical case-insensitive grouping. Fixes false ambiguity when a canonical move appends a case/space alias. No quantity, baseline, parent selection, write or guard changed. |
| `InventoryAvailabilityResolver.cs` | Includes recorded arrival evidence references. Does not change any resolver admission condition. |
| `InventoryEventReplay.cs` | Consumes proven recorded arrivals only when no related modern movement exists. Contradictory modern movements cannot fall back to the legacy path. Does not persist evidence. |
| `OutsideWarehouseTransferService.cs` | Keeps disabled zero-transferable options with positive authoritative quantity. Existing unavailable, seal, quantity and canonical dispatch checks remain. |
| `ProcessorShipmentService.cs` | Same diagnostic visibility; adds rejection of disabled options before line resolution. Physical execution remains canonical. |
| `DashboardDataService.Canonical.cs` | Adds a disabled diagnostic entry for the unresolved remainder of independently proven slices. No mutation path changed. |
| `RoomTreatmentService.cs` | **Only changed line:** the `TreatmentSegmentSelection` record gains optional `int? AuthoritativeBins = null`. All method bodies, canonical dispatch branches, existing persistence calls and writer capabilities are unchanged. The unchanged canonical guards occur at lines 215, 267, 342, 505, 681, 777, 787 and 2489 in the reviewed working source. This additive display field is not a new writer. |

The first mandatory local run passed 152 cases but failed ten checks: eight HTTP
tests could not resolve a Windows build content root inside Linux, the provider
contract could not open the uncreated disposable base database, and the content
lock correctly detected the unregistered `RoomTreatmentService` record-field change.
These results are not a pass. Correct the local runner and rerun the affected gates.

An automatic approval review rejected a combined hash-update/test command because
the capability-review evidence was insufficient. The subsequent explicit diff
review above establishes that the service's only change is a display record field;
no physical method was modified. Updating its registry entry after this review
records the actual source rather than waiving the guard. Final validation belongs
in the impact report; this review is not itself test or release approval.

Final review also verified canonical lot normalization in the historical closure,
fail-closed malformed numeric snapshot handling, and per-identity fingerprints.
The pure replay carries original application events through intermediate legacy
hops so a committed treatment reversal is not lost. These remain read-only proof
changes; no physical writer capability is added.

## Authorized internal route correction (2026-10-16)

Reviewed against e67a4167b979a545cf3e03321e228d261c640597 under the owner's
"Correct McDougall Internal Inter-Crew Route" instruction. No company grouping
or source-origin policy changes. These are existing canonical capabilities:

| Path | Reviewed change and capability conclusion |
| --- | --- |
| `TruckReceiptRoutes.cs` | Pure shared dispatch matrix; existing company group and receipt predicates unchanged. Unknown routes fail closed. |
| `InventoryCommandCustody.cs` | Dispatch accepts the established internal route separately from receipt policy; persisted receipt requirement remains conditional on cross-company status and existing activation metadata. Internal received returns retain the historical receive snapshot as the legacy writer and strict ledger contract require. Truck Receipt-required reopen/return behavior unchanged. No fence, quantity, origin, seal, concurrency, source allocation, or treatment guard removed. |
| `InterCrewTransferService.cs` | Destination options delegate to the shared pure matrix. Existing legacy receipt-activation calculation unchanged. |
| `InterCrewTopologyReadiness.cs` | Adds canonical route/persisted-flag validation; historical noncanonical transfers retain their contract. Adds a blocking check, no readiness exemption. |
| `_InterCrewTransfer.cshtml` | Renders allowed destinations from the shared matrix; removes the duplicated JavaScript default that allowed unknown sources. No write capability. |

The complete internal lifecycle regression exposed the canonical received-return
writer clearing BinsReceived/destination while retaining the receive ledger.
The correction preserves that immutable internal receipt snapshot; it does not
suppress the strict readiness failure or alter completed ledger/movement evidence.
Registry hashes below this review are content locks, not authority to bypass any
runtime writer capability. No new writer, migration, or production operation.

## Historical closure performance correction (2026-10-16)

Reviewed against preparation head `590db49e3f8745421dcf774b186741938df2d0df`
under the owner's focused correction instruction. The only application changes are
three existing read adapters; no physical writer, shared replay rule or model changes.

| Path | Reviewed change and capability conclusion |
| --- | --- |
| `RoomInventoryLedgerQueryService.cs` | Adds an internal historical lot-component scope before the existing SQL aggregate. A bounded SELECT of immutable Bins Run edges retains connected display labels. Local HashSet/Queue additions are not writes. Baseline subqueries deliberately retain the unfiltered ledger; normalization, grouping, reconciliation and metadata calculation are unchanged. |
| `InventoryEvidenceLoader.cs` | Uses the scoped aggregate only for historical closure. Bounded no-tracking identity indexes select complete relevant segment/movement records using the existing canonical normalizer, including contradictory segment links. Ordinary reads, consistent transaction ownership, evidence objects and fingerprint composition remain unchanged. No SQL mutation, persistence, writer capability or readiness exemption. |
| `InventoryEvidenceLoader.RecordedCustody.cs` | Passes the existing requested identity keys and normalized lots into the closure. Exact parent, paired ledger, audited relocation, chronology and fingerprint validation remain unchanged. |

Registry updates for these three files record this reviewed read-only diff. They do
not waive runtime writer fences, authorize projection repairs, or replace executed
PostgreSQL correctness/performance validation. Protected-data rehearsal and production
release remain separate pending gates.

### Final origin-revision dependency review

The final scoped/full-reader regression exposed a selected receipt whose other
historical identity triggers original-receipt snapshot validation. The narrowed
loader omitted that trigger. Reviewed fix: retain the original room query solely
for a bounded, distinct SELECT of selected receipt IDs joined to their current
quantity/identity. The original mismatch predicate and revision interpretation
remain unchanged. It loads no unrelated receipt entities, performs no writes,
and introduces no capability, schema, authority or readiness exemption. Update
only this read adapter's content lock after this review.

### Broad aggregate reuse review (2026-10-16)

Reviewed the application diff against `7367a9d5d497111f2df200404ccf271d7b839dab`
before updating its three read-adapter content locks. `InventoryEvidenceLoader`
passes one locally owned ledger reader through its historical closure. The ledger
reader retains at most 2,000 persisted aggregate groups before canonical-year and
Bins Run reconciliation. Reuse requires the same transaction ID, effective cutoff,
complete room coverage and no warehouse/profile/overlay filter. Missing rooms use
the existing canonical SQL; a bounded provider-side key lookup selects reused
groups with the same effective-label predicate. The combined result retains the
global 2,000-row rejection before inference/reconciliation. No quantity arithmetic,
parent/receipt/correction/treatment proof, replay, fingerprint formula or writer is
changed. Local collection additions and retained groups are not persistence. No
schema change, permission, command-fence exemption or readiness relaxation exists.

The aggregate SQL, metadata and complete historical evidence remain authoritative
read inputs. Finished availability results and treatment projections are never
reused as proof. Cross-request caching and unbounded per-identity query loops are
not introduced. Runtime correctness and performance evidence are separate gates;
these reviewed hashes do not establish release or protected-restore authorization.
