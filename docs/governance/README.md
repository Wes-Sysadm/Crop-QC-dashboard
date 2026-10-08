# Engineering governance

Start with [CROP_QC_BUSINESS_RULES.md](CROP_QC_BUSINESS_RULES.md), then [decisions](DECISIONS.md) and [traceability.json](traceability.json). The JSON is the single rule-to-code/test matrix; it owns evidence references and explicit gaps, not duplicate policy wording.

## Rule compliance assessment

Include in each development task/PR and read-only review:

- Applicable rule IDs and existing implementation paths.
- Authoritative records versus derived projections.
- Proposed behavior and possible conflicts with the rules.
- Exact tests proving the behavior; provider, skipped bodies and unverified rules.
- Unresolved assumptions, distinguishing facts/implementation/missing evidence/new policy.
- Production-data implications and explicit authorization boundary.

For non-inventory work say inventory is unaffected and select QC/FIELD/DEV/SYNC/PHOTO or OPS rules as appropriate. Do not require inventory workflow recertification for an unrelated device/photo change.

## Existing contract map

- [Read contract](../inventory-architecture/phase1-read-contract.md): authority/custody, confidence and replay.
- [Command engine](../inventory-architecture/phase2-command-engine.md): transaction, idempotency and projection lifecycle.
- [Workflow cutover](../inventory-architecture/phase3-workflow-cutover.md), [workflow registry](../inventory-architecture/phase3-workflow-registry.json), [writer registry](../inventory-architecture/phase3-reviewed-write-candidates.json): implementation ownership. Preserve their architecture checks.
- [Receipt conservation](../receipt-inventory-conservation-gates.md), [Truck reconciliation](../truck-receipt-reconciliation.md), [verifier](../truck-receipt-verifier-contract.md), [canonical override readiness](../canonical-receipt-override-readiness.md): operation-specific contracts.
- [General architecture](../architecture.md): domain boundaries, with obsolete receipt-only quantity description removed.
- [Release](../overnight-release-standard.md), [scoped testing](../change-scoped-testing-standard.md): executable release process, never waived by CI.

Phase documents include historical rollout statements. Read their technical contracts with current source/main state; do not replay historical deployment instructions as current authorization.

## Checks

`node scripts/governance/check.mjs` checks unique rule IDs, local references, exact rule/test/decision mappings and protected change disclosure. `node --test scripts/governance/check.test.mjs` exercises rejection cases. Neither proves business runtime behavior.

`pwsh scripts/governance/test-contracts.ps1` runs structural and selected runtime contracts. It requires a localhost disposable PostgreSQL 18 connection in `CANONICAL_INVENTORY_TEST_POSTGRES`, builds the test project and rejects missing/failed/skipped selected results. Tests use fresh UUID-named databases and drop only those fixtures. No production connection, restore or external storage is used. This does not replace a production-shaped rehearsal when a release needs one.

The PR workflow runs on every pull request (including drafts/body edits), without path filters, and uses a disposable PostgreSQL 18 service. It does not deploy. Governance CI is a small permanent architecture baseline; larger workflow testing remains change-scoped. Existing writer checks are executed, not replaced.

See [required repository settings](REPOSITORY_SETTINGS.md), [change procedure](CHANGE_PROCEDURE.md), [outstanding PR review](OUTSTANDING_PRS.md), and [this task's validation](VALIDATION.md).
