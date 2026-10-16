# Transfer eligibility capability review

Implementation review against `df977d041dae2d449cf6fe5f8581e1f5cae41947`.
This is an implementation repair under specification 1.2.0, not a policy amendment.

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
