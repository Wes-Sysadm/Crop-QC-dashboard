# Read-only compliance review of outstanding inventory work

Review baseline: main `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`; #270 merged, #271–#273 open draft. This is a source/documentation review, not fresh production certification or a rerun of another PR's reported tests. No PR was modified or merged. Links are pinned to reviewed heads; historical test counts below remain attributed reports.

## #270 — strict receipt correction readiness (context)

Reviewed head `182b7417462808a97b2b0b3239329089a1048d34`, merged into the baseline. INV-003, REC-001, AUD-001, OPS-001.
The verifier cross-checks the committed command/hash, nested allocations, exact -N/+N pairs, source position, receipt type and treatment membership instead of forcing a valid canonical correction into the old flat snapshot format. Its 28 positive/tampered-evidence cases are part of this governance gate. It is read-only; it does not fix physical inventory or waive other readiness failures.

## #271 — partial custody and audited corrections

[Reviewed head](https://github.com/Wes-Sysadm/Crop-QC-dashboard/tree/58e6d29edc9d81326274a3d7ff73c56dde20fcc5).
Rules: INV-001–INV-006, TRT-001/TRT-002, REC-001/REC-002, MOV-001, ROOM-001, AUD-001, OPS-001.

Source review: InventoryCommandReceiptCustody requires exact dispatch movement allocations and receipt/transfer versions, proves dispatch/ledger coverage and caps net acknowledgements. Placement creates destination credit only for held quantity. Compensations preserve original rows; source is not credited merely because acknowledgement is reversed. The receipt-held reader groups treatment evidence by allocation identity. The container equation is dispatched = unresolved + held + net placed.

Receipt quantity additions now use untreated/no-application projection and correction-time posting rather than copying the selected historical treatment signature. This closes a real TRT-001 gap in current main; this governance PR does not implement that fix or declare main's correction behavior compliant.

[Reported completion evidence](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/58e6d29edc9d81326274a3d7ff73c56dde20fcc5/docs/pr271-custody-completion.md) includes real PostgreSQL races, replay/unknown commit, rollback, mixed treatments, wrong-company rejection and the 50-bin/49-acknowledged lifecycle. Reported full result: 2,359 passed, 12 skips; 55 optional early-return methods are not exercised-provider evidence. This task did not rerun that branch.

Integration requirements: retain #270 strict evidence checks and #271's net compensation readers/SQL writer fence. Add partial acknowledgement/placement/compensation methods to governance traceability/mandatory provider coverage after integration. Do not merge overlapping #268/#269 independently without resolving duplicated fixes. Three additive migrations must be rehearsed on the final combined candidate. After first custody use, old main is not a safe rollback target; database fencing does not make old readers correct.

## #272 — diagnostic explanations, not a bypass

[Reviewed head](https://github.com/Wes-Sysadm/Crop-QC-dashboard/tree/9fcca90e206460d55b030a87017d587212b5a29f).
Rules: INV-003–INV-006, TRT-001, AUD-001, OPS-001.

Source review: TreatmentLineageReadinessService still calculates failure from the stored excess list. It only adds bounded canonical evidence explanations to that list. Mismatched read quantities become EvidenceChanged; a proved pool does not clear readiness. Six captured scenarios and contradictory evidence tests preserve authority/projection separation and distinguish treatment from receipt confidence.

[Investigation](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/9fcca90e206460d55b030a87017d587212b5a29f/docs/investigations/six-lineage-blockers-2026-10-08.md) reports 59 focused passes and unchanged protected records on its restore. Reported six findings remain failures. Its 1,817 authoritative versus 3,356 projected bins are snapshot evidence, not a current live claim.

Documentation tension: requests for physical counts/exact ancestry must be tied to a specific unsupported action after recorded replay is exhausted (INV-005/INV-006). Do not use absent exact receipt allocation to relabel proven shared quantity unknown or demand external paperwork by default. The conservative resolver limitation is implementation evidence, not new policy. No bypass or repair is authorized by this review.

## #273 — bounded derived-projection reconstruction

[Reviewed head](https://github.com/Wes-Sysadm/Crop-QC-dashboard/tree/5c380508668fe078007212db49eed840a0c46d7f).
Rules: INV-002–INV-006, TRT-001/TRT-002, AUD-001, OPS-001.

Source review: separate preview/approval/execution/verification, exact evidence fingerprints, canonical proof independent of projection quantities, serializable/NOWAIT locks, preserved retired rows and receiptless replacement pool. Post-state checks compare authority and protected fingerprints before adding audit/journal. ReceiptRevisionEvidenceValidator reports consistent correction arithmetic but does not grant receipt custody. The three supported pools retain unknown/ambiguous exact ancestry.

[Report](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/5c380508668fe078007212db49eed840a0c46d7f/docs/investigations/historical-projection-reconstruction-2026-10-08.md) states 28 standalone focused passes; its combined 2,400 distinct passing cases include targeted reruns, not one pristine full run; four skips and 51 early-return methods remain disclosed. Three reconstructed pools retain physical quantity; remaining three still block full readiness. Not rerun here.

Remaining review concerns: approval-reference text is not independent cryptographic verification of human authorization or durable backup; operations must verify those externally. Whole-table locks/fingerprints are intentionally restrictive and require a bounded, separately approved maintenance plan. Its blanket personnel/onsite evidence recommendation must be narrowed under INV-005/INV-006, not made a prerequisite for every shared-pool action. SQL-provider snapshot drift reported on baseline remains a separate limitation, not permission for a migration here.

## Combined integration gates

1. Reconfirm all heads/base and review new deltas. Resolve Program and writer-registry/capability-list overlap semantically; retain both legitimate additions and recompute only reviewed source hashes.
2. Preserve #270 strict verifier, #272 strict readiness, #271 net-custody readers/compensations and #273 projection-only maintenance. No independent physical authority from projections was found in the reviewed new code paths; this is not an exhaustive runtime certification.
3. Run exact combined-candidate PostgreSQL partial custody, corrected receipt treatment, reconstruction failure/concurrency, immutable history and complete readiness checks; do not reuse reported counts as a current pass.
4. Distinguish documented policy from algorithmic proof gaps. Review which operation actually requires exact ancestry; preserve nonnegative/conservation/treatment guards.
5. No six-blocker resolution is part of this governance PR. Future repair needs fresh evidence, verified backup, explicit bounded approval and data-compatible rollback. Neither this review nor the three supported #273 rehearsals clears release readiness.
