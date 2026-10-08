# Shared project knowledge

GitHub `Wes-Sysadm/Crop-QC-dashboard`, reviewed `main`, is the shared source of
project instructions. Git distributes it to Windows clones. There is no local
knowledge database, background service or scheduled synchronization.

Start at [AGENTS.md](../../AGENTS.md), the [business rules](CROP_QC_BUSINESS_RULES.md)
and relevant [decisions](ARCHITECTURAL_DECISIONS.md). Use [Windows setup](WINDOWS_SETUP.md),
[change procedure](CHANGE_PROCEDURE.md), [validation](VALIDATION.md),
[repository settings](REPOSITORY_SETTINGS.md) and [PR assessment](OUTSTANDING_PRS.md).

## Rule compliance assessment

Every task, including a read-only review, reports the specification version and
governance SHA, applicable rule/decision IDs, affected invariants, implementation
paths and authoritative records versus projections. State proposed behavior and
conflicts; inventory and treatment effects; historical records preserved/changed;
new origins/writers; exact tests, provider and results; unverified rules, skipped
bodies and assumptions; production implications/authorization; and whether a
genuine policy decision needs explicit owner approval (with its reference).
For a read-only task, say which checks were not run and why; do not imply runtime
proof. The PR template carries the same assessment into review.

## Existing authoritative material inspected before consolidation

| Source | Continuing authority / precedence |
|---|---|
| [Root instructions](../../AGENTS.md) | Git/PR workflow, backups, production, migrations, testing, code review, QC Station and MSI safeguards remain intact. |
| [Phase 1 reads](../inventory-architecture/phase1-read-contract.md) | Quantity, custody, treatment and receipt-confidence separation; read-only evidence and recorded chronology. Historical rollout status is not current status. |
| [Phase 2 commands](../inventory-architecture/phase2-command-engine.md) | Canonical executor, serializable validation, conservation, intent-bound replay and audited projection supersession. |
| [Phase 3 cutover](../inventory-architecture/phase3-workflow-cutover.md) | All physical writers, capability boundaries, workflow adapters, reversals and activation gates. |
| [Writer registry](../inventory-architecture/phase3-workflow-registry.json) and [reviewed candidates](../inventory-architecture/phase3-reviewed-write-candidates.json) | Existing structural protection and classified maintenance paths; hashes require substantive review, not blind refresh. |
| [Receipt correction](../receipt-bin-count-correction.md), [conservation gates](../receipt-inventory-conservation-gates.md), [proven lineage](../proven-lineage-eligibility.md) | Detailed receipt-scoped proof, movement/correction history and shared-pool eligibility. Older bounded algorithms are not new policy. |
| [Truck reconciliation](../truck-receipt-reconciliation.md), [truck verifier](../truck-receipt-verifier-contract.md), [override readiness](../canonical-receipt-override-readiness.md) | Receipt adoption, verifier and exceptional override gates remain binding; a governance update grants no override authority. |
| [Architecture](../architecture.md), [legacy decisions](../decisions.md) | Technology, storage, email, device and observational-QC boundaries remain in effect. Obsolete receipt-only room authority is explicitly superseded by the phase contracts and INV-002/INV-004. |
| [Testing](../change-scoped-testing-standard.md), [release](../overnight-release-standard.md), [backup](../backup-restore.md) | Existing change-scoped verification and operational gates; this PR grants no production permission. |

The inventory specifications and current implementation were inspected on main
`e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. Other repository documents remain
discoverable through Git; load relevant detail without rereading every historical report.
Approved business behavior outranks an implementation limitation. Compatible,
more specific safeguards remain binding. If documents conflict, name both sources
and seek a business decision only when the established policy cannot resolve it.

## Consolidation and outstanding work

Draft [#274](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/274), inspected at
`c272d6a5f057cf8ca7549610ee3a1f5b76992b24`, already proposes a 20-rule catalog,
six decisions and contract checks. This independent main-based PR reuses those
compatible IDs, tests and traceability, adds all 27 foundational requirements and
safe Windows synchronization, and retains main's detailed AGENTS instructions with
an explicit owner-approved-policy boundary around task overrides. It uses
ARCHITECTURAL_DECISIONS.md as requested. The [file-by-file comparison](PR274_CONSOLIDATION.md)
records the final consolidation into #276. Do not merge two competing catalogs or
assume #274 is approved. No other PR implementation is incorporated. Follow the
[post-merge activation procedure](POST_MERGE_ACTIVATION.md) only after authorization.

Known gaps remain visible: positive receipt corrections can inherit historical
treatment on current main; partial acknowledgement/compensation depends on #271;
historical reconstruction has limits recorded in #272/#273 and later draft #275.
Governance records policy without certifying those unmerged implementations.
