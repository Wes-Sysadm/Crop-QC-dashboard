# Direct location transfers — implementation assessment

Owner: Wes. Approval: the November 5, 2026 request “Crop QC Dashboard —
Simplified Direct Location Transfers” in the implementation conversation. This
authorizes development and a review PR, not merge, deployment or conversion of
production records. Starting main: `abdee7e5562ad6ae1a347d993b738bb758c8d7b3`;
starting specification 1.2.0, governance commit
`2e600ad72d20a52560d3c13526ba933447feb701`.

Owner scope clarification in the same conversation: “All Crop QC rooms; retain
outside-packing workflow.” The direct picker includes every active, open Crop QC
room across companies. Outside Warehouse custody retains its separate existing
outside-packing workflow; no fictitious room is created for an outside warehouse.

## Assessment before implementation

INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001 and OPS-001
apply. The approved policy changes are immediate cross-company movement without
a new Truck Receipt, and an explicitly unverified source assignment after a
missing-quantity correction. Neither is an inventory origin. Quantity remains
in the existing ledger, custody transactions and immutable movements. Treatment
segments remain projections, never stock authority. Shared inventory does not
acquire exact receipt ancestry through this change.

Current initiation uses DashboardDataService room moves or InterCrewTransferService
dispatch. The canonical executor checks the route, debits the source and records
immutable treatment allocations. Cross-company dispatch creates InTransit custody;
TruckReceiptReconciliationService matches a receipt, then canonical acknowledgement
establishes receipt-held custody and placement credits a room. Partial acknowledgements,
placements and their compensations are retained independently. PR #252 established
the old route requirement. PR #278 separated valid destination admission from
historical projection completeness; that separation remains mandatory.

Necessary checks: active/unsealed locations, permissions, canonical identity,
funded source and authorized origins, selected treatment, complete intent replay,
fresh fingerprints/versions, serializable transaction, exact conservation, audit
and projection delta validation. The cross-company dispatch/receipt prerequisite
is workflow-specific and is superseded only for new direct commands. Legacy
evidence is not reclassified by changing a flag.

## Technical dependencies and design

Reuse RoomTransfer, its paired RoomInventoryAdjustments and TreatmentLineageMovements.
Add explicit direct/correction metadata to the existing parent, preserving legacy
defaults. One command can contain multiple inventory/treatment allocations, with
one atomic journal and transaction. All new location moves use the direct command;
legacy dispatch/acknowledgement/placement remain available only for historical work.

Corrections append compensating RoomTransfers linked to their original transfer.
The default destination is the original source. Store the actual evidence and an
explicit physical-location verification state, separately from assigned custody.
Never label the default as a physical observation. A verified subsequent correction
can relocate a supported assignment. Exact original movement allocations bound
correction quantity; already consumed or ambiguous allocations cannot be recreated.
Original parents and ledger/movement history remain retained.

Legacy conversion uses proven receipt-held allocations and the existing audited
placement writer under a distinct conversion intent. It preserves acknowledgement,
receipt, dispatch and placement references. Already placed quantities are excluded;
unacknowledged or contradictory quantities remain unresolved. An operator must
select a valid destination; deployment/startup performs no conversion. Additional
historical shapes require evidence review, never forced completion.

Consumers to verify: canonical availability and origin recursion, movement replay,
room ledger/display, treatment cohorts, Bins Run selectors, transfer history,
receipt conservation, transit/held readiness, command journal replay, Web endpoints
and authorization. Direct parents must not enter Truck Receipt reconciliation queues.

## Verification and release scope

Use disposable PostgreSQL for direct internal/cross-company movement, multiple
allocations, mixed treatments, origin recursion, stale/overdraw/race/replay,
correction and verification history, conversion with partial/unresolved quantities,
transaction rollback and retained committed-data compatibility. Run the materially
affected existing movement, receipt custody, treatment, origin and architecture
contracts; no unrelated feature recertification. Update reviewed writer metadata
only after inspecting the final changes. Record exact results before review.

Schema is additive. Do not drop used correction evidence on rollback. A compatible
rollback must understand these records or refuse physical writes; old main alone
is not an acceptable writable rollback after first use. Prove candidate → compatible
rollback → candidate on a disposable database with committed transactions retained.
Production release requires owner authorization, verified durable backup, migration
review, exact-candidate rehearsal and independent verification. Production legacy
conversion is a separately approved, evidence-backed operation after release.

## Reader and writer review

The new `/Transfers` form uses canonical availability, transaction fingerprints,
the existing transfer-create and room-transaction-edit permissions, and CSRF
protection. Transfer Admin is required for corrections. The older room-move
POST remains a compatibility adapter to the same direct command; the old dispatch
POST redirects without writing. Outside packing retains its existing writer.
Each inventory/treatment allocation has a RoomTransfer parent and paired ledger
entries, grouped by the atomic command journal. Receipt and treatment origin
records are retained, not reassigned to the destination as new arrivals.

Room availability, origin recursion and downstream movements already recognize
paired RoomTransfer evidence; they require no special exception for direct
parents. Conservation counts their existing signed ledger entries once. Its
compensation validator now accepts the distinct legacy-conversion intent only
when the same exact placement, audit, command, custody and movement proofs pass.
Unverified assignments appear separately in the conservation report and room/UI
warnings. Those warnings follow immutable movement links and stop at verified
correction evidence. They describe historical uncertainty, not an independently
measured current quantity or ownership of particular receipts.

Reviewed-source classifications cover the two new read/command adapters and the
changed canonical writer, context, dashboard adapters and conservation readers.
Local collection additions are not physical writes. No new physical-write
capability is available to a controller. Specification 1.3.0 and ADR-009 record
the owner-approved workflow change; stable rule IDs and previous mandatory tests
remain in the traceability matrix.

## Separately controlled production conversion plan

1. After a separately authorized release, take a read-only inventory of open
   historical dispatches, acknowledgements, placements and reversals. Record IDs,
   versions, immutable allocation links, treatment applications and quantities
   in room, transit, held and unresolved custody. Reconcile to the ledger before
   proposing any command. Do not infer receipt ownership from a room balance.
2. Prepare an operator-reviewed destination and exact allocation preview for
   proven held remainder only. Exclude already placed/reversed quantities.
   Unacknowledged, contradictory and unsupported historical shapes remain open
   with their evidence gap; this implementation does not force-convert them.
3. Obtain separate authority for the identified production commands and verify
   the standard durable pre-operation backup, including read-back and checksum.
   Rehearse those exact shapes on a protected disposable copy under the release
   standard before production execution. No conversion runs during startup or
   migration.
4. Execute each supported transfer through `ConvertLegacyTransfer` with stable
   operation keys and current receipt/transfer versions. The canonical writer
   moves the selected held quantity to its valid room atomically. A changed
   fingerprint/version or missing evidence stops that command. Retry the same
   intent/key only; do not create replacement inventory to resolve a rejection.
5. Independently compare pre/post origin totals, room/transit/held quantities,
   treatment membership, unchanged receipts/history, linked placement and audit
   IDs, unresolved remainder and conservation/readiness findings. Check another
   supported movement from the converted position in rehearsal. Stop on an
   unexplained difference and retain all evidence.

## Migration and recovery limits

`20261105184028_DirectLocationTransfers` adds four RoomTransfer columns, a
restrictive correction relationship, supporting indexes and an evidence check.
Legacy rows default to non-direct with null correction metadata. PostgreSQL
also installs a writer capability fence and makes direct parents immutable.
First use of either a direct parent or conversion journal activates the fence;
the migration's Down refuses to erase used evidence. Persisted command kind 42
is the conversion identifier and must not be renumbered.

The disposable migration test applies actual PostgreSQL migration operations,
preserves legacy quantities through an unused down/up, then rejects destructive
down and an incompatible writer after use. The HTTP transition test is candidate
→ **the same assignment-aware candidate with `DirectTransfers:Enabled=false`**
→ candidate. It retains a committed correction, reads its unverified evidence,
rejects new direct transfers, performs a supported inventory exit, then completes
a verified correction without losing the intervening activity. This is evidence
for feature-disable recovery, not proof that historical main is a writable
rollback binary. SQL Server migration types/model are generated and checked;
PostgreSQL is the runtime integration target for this change.

A release must keep additive schema and use that compatible recovery profile
or a separately rehearsed assignment-aware recovery build. Never restore an old
database over newer legitimate transactions merely to undo this feature. Before
first deployment, perform exact-candidate production-shaped rehearsal and a
scoped operator UI review. No production backup, migration, deployment or
conversion has been performed for this PR.

Corrections require surviving proven original allocations. Already consumed or
structurally changed allocations are not fabricated. A shortage correction
retains a reason and actual evidence even when the source assignment is unverified;
later verified corrections append evidence rather than rewriting that history.
Traversal warnings are conservative at their graph-size/depth limit and never
authorize inventory or veto an otherwise valid command.
