# Updating approved knowledge

GitHub reviewed main owns policy. An unmerged proposal, technical discovery,
passing test, earlier conversation or global file cannot silently change it.

1. Identify affected rule IDs, relevant decisions, exact existing behavior and
   the reason for change. Separate a genuine new policy choice from missing data
   or an implementation defect. Ask for explicit business approval only for the
   former; do not repeatedly ask the owner to confirm established rules.
2. Record the human approval reference/date and exact approved behavior. Keep
   proposals marked proposed until approved. Never infer approval from green CI.
3. On a fresh focused `codex/` branch from current main, update the canonical
   specification and bump its version; preserve rule IDs. Amend/add a stable
   decision with evidence, components, rule IDs, approval and supersession details.
   Existing decisions remain readable; do not erase superseded policy history.
4. Update the relevant implementation/detail references and exact contracts in
   traceability.json; add meaningful behavior tests when policy changes. Identify
   gaps rather than assigning a nearby test as proof of an unimplemented rule.
   Run governance validation, required contracts and change-scoped tests.
5. Create a reviewable PR. Disclose previous/new behavior, affected rule IDs,
   historical evidence, test/provider results, coverage gaps, production impact
   and the approval reference. Governance/test/registry/CI changes require the
   template's Governance change section, even if they appear purely editorial.
6. Obtain explicit human review/approval and merge only through the repository's
   standard process after authorization. Proposed branch protections are in
   [repository settings](REPOSITORY_SETTINGS.md); they are not currently enforced.
7. After merge, every computer fetches and synchronizes using
   [Windows setup](WINDOWS_SETUP.md). Report version/SHA differences and reload
   instructions. No separate computer-specific catalog is created.

Routine fixes implement existing rules; they should update technical evidence or
tests without redefining the foundations. A protected-change disclosure is review
metadata, not authorization to weaken policy. The checker deliberately cannot
decide whether prose expresses the owner's approval; independent human review is
required. No update to knowledge authorizes production repair, release or merge.
