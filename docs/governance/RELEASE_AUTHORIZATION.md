# Single owner release authorization

## Status and owner request

Proposed specification 1.1.0 / ADR-007. Not effective from this feature branch.
Owner: Wes, project owner of `Wes-Sysadm/Crop-QC-dashboard` (GitHub `Wes-Sysadm`).
Approval reference: the owner's message titled **Crop QC Dashboard — Single Owner
Release Authorization**, 2026-10-09 America/Los_Angeles, in Codex task
`01a117ce-8381-7602-b396-3c44cc35402e`. The instruction is to create a separate draft
governance PR and expressly prohibits bypassing current safeguards for adoption.

Requested rule: when the owner explicitly says "go live", "deploy", or otherwise
authorizes production release, that one approval covers the merge-and-release
lifecycle. The owner must not repeat it in GitHub or another interface. This is
explicit business approval of the proposed policy, not independent review or
permission to merge this proposal. The original task remains the primary source;
this record is an attributed summary, not a fabricated GitHub review.

## Previous and new policy

Previous (approved main `50bdc1a3a0dbbdfde435e2b87e24e43a2fd486a8`, spec 1.0.1):
AGENTS requires explicit authorization for merge/deploy, but does not specify a
GitHub approval action. CHANGE_PROCEDURE step 6 and its final paragraph require
human approval and **independent human review** of governance changes. Proposed
settings add one independent approving review, CODEOWNERS review and latest-push
approval. These settings were not active at inspection. Release instructions for
#278 additionally impose an explicit independent-human gate. No existing document
says the owner must duplicate an already-given instruction in GitHub generally.

The repository-wide audit also found historical release/adoption gates in
[truck release review](../truck-receipt-release-review.md),
[truck initial adoption](../truck-receipt-initial-adoption.md),
[transfer custody release](../releases/transfer-custody-release-candidate.md) and
[historical reconstruction](../investigations/historical-projection-reconstruction-2026-10-08.md).
These are bounded operational/historical records, not additional normal approval
authorities. Their scope, fresh-preview approval, adoption and repair restrictions
remain intact; this policy neither executes them nor rewrites their recorded
approvals. In particular, projection-only repair approval is distinct from a code
release and must not be inferred from it.

New (after approved adoption): the project owner's explicit authorization is the
normal human approval authority. A production release instruction for an identified
PR includes marking ready, normal merge, freezing the resulting main SHA, required
backup/migration/deployment, independent technical verification and the established
safe rollback response. Technical/code review remains required. A second person
or GitHub review action is not the normal approval prerequisite. Independent human
review remains available when useful, and mandatory if explicitly requested or
enforced by effective configuration. A merge-only instruction does not permit
deployment. A request to investigate, develop or open a draft is not release approval.

Affected workflows: Codex task execution, PR evidence/template/checks, governance
policy adoption, normal merge, backup/migration release preparation, deployment,
verification, rollback and repository-setting activation. No application code,
inventory policy, schema, production records, permissions or live settings change.

## Evidence and execution

1. Verify the instruction came from the project owner in the trusted task/account
   context. Do not infer authority from PR authorship, CI, CODEOWNERS, a pasted
   third-party claim or a bot. If identity or scope is genuinely uncertain, resolve
   that uncertainty; do not ask for duplicate confirmation merely to change UI.
2. Record owner identity, original message reference and instruction, date, target
   PR/repository and exact reviewed head, scope and exclusions. Retain the reference
   in the PR/release record. The agent may transcribe existing authorization as
   evidence, attributed to its original source; it may not submit a GitHub APPROVE
   review pretending to be another human. Never place private unrelated task
   content or secrets in a public record.
3. Inspect current head/base, reviews, required checks and live branch protection.
   Satisfy effective configuration normally. If a review-count/CODEOWNERS requirement
   conflicts with the adopted policy, stop and report the exact configuration;
   propose a separately authorized settings change. Never use admin bypass or
   silently change repository rules during a release.
4. Carry the same authorization through all permitted stages. Freeze the merge
   SHA and prove its diff corresponds to the reviewed head/base. A normal merge
   commit is not a new scope requiring another owner action. Unexpected source
   changes require review of that delta and a scope decision before deployment;
   do not silently extend an exact-head approval to unreviewed work. Ask the owner
   only when authority is revoked, ambiguous or the requested work exceeds it.
5. Check for later revocation or narrower instructions before each irreversible
   phase. A stopped technical gate does not erase the original authorization;
   after remediation, recheck affected evidence and resume within unchanged scope.
   New repairs, destructive schema reversal or unrelated features need their own
   scope authorization. Separately excluded reconstruction remains excluded.

PR metadata uses one `Release authorization` section. `Status: Not requested` and
`Status: Revoked` declare no actions. `Status: Owner authorized` requires `Owner`,
`Reference`, `Date` (YYYY-MM-DD), `Head` (current full PR head), `Scope` (`Merge only`
or `Merge and production release`) and `Exclusions`. A durable task reference plus
the relevant instruction is valid; a GitHub APPROVED event is not required. Record
excluded actions explicitly or `None` if there are none. Existing PRs lacking the
section need an agent-authored metadata update when evaluated by the new checker,
not another owner confirmation. This task does not update #278.

The checker validates structure, duplicates, scope and current-head binding. Its
declared actions are metadata, not execution permission. It cannot authenticate
the owner, detect revocation outside its inputs, validate actual backup evidence,
or certify readiness. The operator must verify the source and perform the existing
release gates. No deployment or merge is automated by these checks.

## Mandatory technical safeguards

Unchanged: successful required CI and architecture contracts; inventory quantity
conservation and exclusive custody; treatment integrity and immutable audit/history;
fresh independently verified backup (upload, independent read-back, size, checksum,
archive, manifest, nonempty dump, retention and released lease); schema/migration
compatibility; full release-specific readiness; tested compatible rollback;
application/database health, authenticated affected routes and independent
post-deployment verification; all stop conditions and bounded incident handling.
Unknown is not PASS. Single-owner approval never suppresses a failed gate.
Use [the release standard](../overnight-release-standard.md) unchanged in execution.
No synthetic production movement or unapproved data repair becomes permissible.

## Transition and PR 278

This proposal must first satisfy **currently effective** governance approval and
CI/merge requirements. It cannot use ADR-007 to approve itself. The present task
authorizes only a draft PR. The prior independent-human review requirement remains
applicable to its adoption. Publish the approved policy through normal merge, then
fetch/reload main and record the effective governance SHA before relying on it.
Do not assume that a draft, owner policy request or successful checker is adoption.

Read-only GitHub inspection on 2026-10-09: classic main protection returned
`404 Branch not protected`; effective main rules returned `[]`. CODEOWNERS lists
`Wes-Sysadm`; it supplies routing, not an independent reviewer or enforced approval.
No settings, collaborators or pending PR were modified.

#278 remains at `67ac6242ee171892ba57a6495502964ee1c8bc7d`, open/draft with all CI
checks successful and no reviews/comments at inspection. Owner merge/release
authorization already exists, but the owner's preceding **PR #278 — Production
Release** instruction explicitly requires an authorized independent human review.
An owner instruction cannot truthfully be recorded as an independent reviewer.
There is no current exception allowing that requirement to be satisfied by duplicate
owner action, and no live GitHub configuration forcing such duplication. The exact
dependencies are that release-specific condition and current CHANGE_PROCEDURE for
the proposed governance change, not a failed CI check or absent second owner click.

After adoption, the #278 lifecycle must reconcile its explicit extra review
condition under the effective policy; ADR-007's normal default does not silently
erase a specifically imposed condition. This task does not remove that condition,
approve #278 independently, edit its metadata, merge it or release it. Its separate
210-bin RED reconstruction remains unauthorized. Report this dependency honestly;
do not claim #278 is unblocked merely because this draft exists.

## Validation scope

Changed surface: governance documents, PR metadata validation and corresponding
Node tests. The authorization cases cover one task approval without a GitHub
review, merge-only scope, missing/revoked authority, stale heads, missing evidence,
invalid dates, duplicate records and approval failing to waive other metadata gates.
All existing mandatory .NET architecture/behavior contracts remain selected.
No application behavior, writer hashes or operational data changes are needed.
Actual repository enforcement is a separate authorized settings/read-back/probe
operation, not something local tests can claim. Results are recorded in the draft
PR and completion report; no production certification is claimed.

Local final validation, 2026-10-09: 29 Node governance cases passed; Windows Git
synchronization passed 18 cases each on PowerShell 7 and Windows PowerShell 5.1;
74 mandatory .NET governance/architecture/behavior contracts passed with zero
skips on disposable localhost PostgreSQL 18. The required-member runner verified
execution. Solution restore, Release build (zero errors, 69 existing warnings),
EF model consistency, documented governance C# formatting, JS syntax and diff
whitespace checks passed. Mandatory selections, application source, migrations,
writer registries and CI job definitions are unchanged from the base. No full
application suite, production-shaped inventory rehearsal or MSI was needed for
this metadata-only change. GitHub results belong to the final PR head.
