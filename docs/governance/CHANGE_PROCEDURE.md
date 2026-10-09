# Updating approved knowledge

GitHub reviewed main owns policy. An unmerged proposal, technical discovery,
passing test, earlier conversation or global file cannot silently change it.
Classify each change as clarification, evidence update, implementation repair or
genuine policy change. Never weaken policy to fit a test or implementation limit.

1. Identify affected rule IDs, relevant decisions, exact existing behavior and
   the reason for change. Separate a genuine new policy choice from missing data
   or an implementation defect. Ask for explicit business approval only for the
   former; do not repeatedly ask the owner to confirm established rules.
2. For a genuine policy change, record the business owner's identity, explicit
   approval reference/date, previous and new rule, affected workflows and any
   migration, historical-data and rollback implications. Evidence-only updates
   must not invent policy approval. Record the exact approved behavior. Keep
   proposals marked proposed until approved. Never infer approval from green CI.
3. On a fresh focused `codex/` branch from current main, update the canonical
   specification and bump its version; preserve rule IDs. Amend/add a stable
   decision with evidence, components, rule IDs, approval and supersession details.
   Existing decisions remain readable; do not erase superseded policy history.
   Retired rule/decision IDs remain recorded and must never be reused.
4. Update the relevant implementation/detail references and exact contracts in
   traceability.json; add meaningful behavior tests when policy changes. Identify
   gaps rather than assigning a nearby test as proof of an unimplemented rule.
   Run governance validation, required contracts and change-scoped tests. Name the
   owning follow-up for coverage gaps; a gap is not a waiver of a required gate.
   Never refresh writer-registry hashes automatically to make a failure disappear;
   review the changed writer and prove the capability boundary first.
5. Create a reviewable PR. Disclose previous/new behavior, affected rule IDs,
   historical evidence, test/provider results, coverage gaps, production impact
   and the approval reference. Governance/test/registry/CI changes require the
   template's Governance change section, even if they appear purely editorial.
6. Under effective ADR-007, obtain explicit project-owner approval and merge only
   through the standard process. One owner release instruction supplies merge and
   release authority within its recorded scope; do not request duplicate owner
   approval in GitHub. Preserve substantive code review and all technical gates.
   Additional human review is optional unless the owner explicitly requires it or
   effective configuration enforces it. Proposed protections are in
   [repository settings](REPOSITORY_SETTINGS.md); never bypass live requirements.
   Adoption of ADR-007 itself still follows the prior independent-review rule;
   see [transition](RELEASE_AUTHORIZATION.md#transition-and-pr-278).
7. After merge, every computer fetches and synchronizes using
   [Windows setup](WINDOWS_SETUP.md). Report version/SHA differences and reload
   instructions. No separate computer-specific catalog is created.

Routine fixes implement existing rules; they should update technical evidence or
tests without redefining the foundations. A protected-change disclosure is review
metadata, not authorization to weaken policy. The checker cannot authenticate the
owner or decide that approval is genuine. The operator verifies and records the
original owner instruction under [release authority](RELEASE_AUTHORIZATION.md).
Policy publication alone grants no release or repair authority. Historical rule:
version 1.0.1 required independent human review of governance changes; this
proposal replaces that requirement prospectively, not for its own adoption.
