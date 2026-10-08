# File-by-file reconciliation of draft 274 into 276

Compared #274 at `c272d6a5f057cf8ca7549610ee3a1f5b76992b24` with #276 starting at
`a299ec5830e5e6c9f2f86ac4ff4ef460f707b196`, against main
`e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. This covers all 18 files changed by
#274. Final revisions remain in #276; no competing PR or second catalog/test
system was created. #274 remains open pending the owner's decision.

| File in 274 | Comparison and disposition in 276 |
|---|---|
| `.github/CODEOWNERS` | Same owners/patterns, plus sync helper and root/nested instruction overrides. Access prerequisite documented; no fictional reviewer added. |
| `.github/pull_request_template.md` | Byte-identical donor template: 11 assessment fields and protected-change disclosure. |
| `.github/workflows/governance.yml` | Donor governance and PostgreSQL jobs retained; Windows 7/5.1 matrix adds two contexts. Read-only permissions and disposable provider retained. |
| `AGENTS.md` | Startup/assessment intent retained. Restored assessment for read-only reviews and clarified task overrides cannot approve foundational changes. Donor deletion of detailed Field Sample/device instructions rejected; all original main detail and production gates retained. |
| `docs/architecture.md` | Removed remaining receipt/sample/depletion-only current-quantity description. Original receipt location is not current custody; historical QC observations and phase authority preserved. |
| `docs/governance/CHANGE_PROCEDURE.md` | Consolidated retired IDs never reused, change classification, named owner approval, previous/new behavior and history/rollback impact, gap ownership and substantive writer-hash review. No routine bug-fix approval loop introduced. |
| `docs/governance/CROP_QC_BUSINESS_RULES.md` | All 20 IDs retained, expanded to index 27 foundations. Version 1.0.1 clarifies overrides. Corrected donor origin wording: transfer custody does not create a second physical origin. Quantity, ancestry and treatment proof remain separate. |
| `docs/governance/DECISIONS.md` | Six stable decisions preserved in sole canonical ARCHITECTURAL_DECISIONS.md, with evidence/components and explicit adoption status. ADR-006 clarifies ordinary overrides. No duplicate register; legacy technical decisions unchanged. |
| `docs/governance/OUTSTANDING_PRS.md` | Donor findings/limitations retained; pinned main and draft heads distinguish proposed work from adopted policy. No changes to 271–273 or runtime fixes imported. |
| `docs/governance/README.md` | Restored task assessment and truck reconciliation/verifier/override-readiness links. Source precedence, sync and reconciliation links added. |
| `docs/governance/REPOSITORY_SETTINGS.md` | Donor review/stale approval/check/force-push controls retained; restored conversation resolution. Added exact contexts/source app, strict updates, no-admin-bypass, access prerequisites, readback and negative probe. Settings still inactive. |
| `docs/governance/VALIDATION.md` | Real-provider/no-skip proof and rejection safeguards retained. Distinguishes historical evidence from final rerun; adds both shells, ignored-file safety, actual-install discovery limitation and activation proof. |
| `docs/governance/traceability.json` | All donor required method mappings retained; adds existing unknown-treatment-gap contract, pending gaps and sync references. One matrix remains authoritative. |
| `scripts/governance/check.mjs` | Donor catalog/body/path checks retained. Adds stable evolution, links/anchors, indexed foundations and dependencies. Finalization protects nested instruction/override changes and rejects backward versions or silent required-architecture-suite removal. Approval remains human-reviewed. |
| `scripts/governance/check.test.mjs` | All donor rejection cases retained; adds evolution/link/schema tests and automated missing/nonlocal/unmarked PostgreSQL rejection. Same entrypoint. |
| `scripts/governance/test-contracts.ps1` | Byte-identical donor runner. Local test database, required methods/suites, discovery/execution verification and failed/skipped rejection retained. |
| `tests/CropQc.Api.Tests/BusinessRuleContractTests.cs` | Byte-identical donor contracts, including actual PostgreSQL provider requirement. |
| `tests/CropQc.Api.Tests/GovernanceArchitectureTests.cs` | Byte-identical donor reflection checks. Existing canonical writer architecture tests and registries unchanged. |

#276-only additions are Windows setup/helper/fixtures, canonical register naming,
activation and this record. No runtime writers, database history or production
permissions change. Pending treatment/receipt acknowledgement gaps stay visible.
File equality and required mapping retention were checked with Git/JSON; prose
differences were reviewed for policy meaning. Results: [validation](VALIDATION.md).
