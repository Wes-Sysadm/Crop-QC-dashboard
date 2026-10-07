# Canonical receipt identity correction readiness

## Problem and scope

The deployment verifier rejected a legitimate canonical 64-bin receipt identity correction twice for each of its two ledger rows. `ReceiptOverrideIdentityMismatch` required the old `ReceiptAdminOverride` type, although `InventoryCommandReceiptIdentity` deliberately writes `InventoryIdentityCorrection`. `ReceiptOverrideSnapshotInvalid` tried to read flat `warehouseId`/`roomId` fields from the canonical nested `InventoryReceiptAllocation[]` snapshot.

The writer conserved inventory with an old-identity debit and a new-identity credit. This is a verifier contract defect, not evidence that four inventory changes were corrupt. It affects future canonical receipt identity corrections as well as existing records. There is no receipt-ID or adjustment-ID exemption.

Affected area: read-only receipt override/deployment validation and its reviewed-source fingerprint. No writer, receipt UI, canonical availability calculation, schema, backup implementation, or production data changes.

## Validation contract

Legacy flat snapshots retain their existing checks. Canonical version-3 identity rows must additionally prove:

- The exact persisted correction and override parents, receipt, actor, reason, operation key and commit timestamp agree.
- A committed `CorrectReceiptIdentity` command has an intact SHA-256 intent and names the exact ledger and treatment movement IDs.
- Required nested snapshot fields exist; missing fields never silently become default identity/quantity values.
- Before/after receipt identity, count, location and version agree with the reviewed command and correction target.
- Source snapshots agree, allocations have unique keys and exact receipt provenance, and their quantities reconcile to the parent.
- Each room has its exact debit/credit pair and command effect; current quantities are conserved. Movement quantities, source/target identity and treatment snapshots agree.
- Existing completeness, parent-count, amount, per-room conservation and identity-correction checks still run. Unsupported or incomplete evidence fails closed.

JSON command timestamps retain 100ns .NET ticks; comparisons to PostgreSQL effective timestamps use PostgreSQL's microsecond precision. No timestamp or historical record is rewritten.

## Evidence and verification

The normal receiving and receipt-edit service path reproduces all four original findings before the fix. It passes after the fix and leaves the complete disposable PostgreSQL fingerprint unchanged during readiness validation. Another receipt shares the source lot in the fixture, proving the selected 64-bin receipt does not authorize reclassification of its neighbor.

The 27 negative cases damage only detached in-memory copies of genuine service-generated evidence. Committed PostgreSQL records remain immutable. Missing/mismatched journal, parent, hash, actor, type, version, source, lot, variety, status, room, quantity, balance, snapshots, receipt provenance, duplicate allocation and movement/treatment evidence remain blocked. Final review added three failing regressions for conflicting source-position, treatment-application and receipt-type evidence, then tightened the validator to reject them. Source positions must agree exactly with allocations, and their proven treatment slices must reconcile quantity, application IDs and projection IDs.

- Focused canonical readiness: 28 passed, zero failed/skipped, using genuine normal-service corrections in disposable PostgreSQL.
- Optional legacy PostgreSQL administrator workflow: passed with a fresh current-schema fixture. Its positive reclassification receipt now has an independent lot, avoiding receipt-specific treatment evidence created by earlier scenarios in the same fixture. Production ambiguity guards and their negative tests were not weakened.
- Final complete application suite: **2,321 passed, 0 failed, 0 runner skips**, Release configuration, 9 minutes 14 seconds. Explicitly requested for this review, with no test filter or exclusions. Canonical, backup, Truck Receipt, fruit-profile, receipt-date, legacy normalization and legacy override PostgreSQL fixtures are configured.
- The historical TR508197/receipt-142 restore test is not exercised: the oldest available backup already contains the correction (GrowerLot 474, version 1). Rewriting its history would not create a faithful pre-correction fixture. Optional tests that return early without environment-specific fixtures can appear as passed to the runner; that is not exercised provider coverage.
- Initial full-run failures were investigated: an outdated legacy fixture lacked the command table, and two backup tests could not launch pg_dump inside the sandbox. The final run uses a fresh current-schema legacy fixture and normal local child-process access, without changing the backup tests.
- Restore/build, changed-file formatting verification, EF pending-model check and diff whitespace check passed. No schema change.
- Fresh **full production readiness** at 2026-10-07 21:55:48.687582 UTC passed: 1,079 negative adjustments examined (93 historical / 986 new format), zero deployment blockers. The four receipt-override findings disappeared; six unchanged version-0 NoParent advisories remain nonblocking. The entire final verifier source ran in an enforced read-only, repeatable-read diagnostic transaction which rolled back, with no tracked changes. This was not application startup, migration or deployment.
- Receipt 2161 remains 64 bins: original +64, correction -64 old identity / +64 target identity. Source lineage 791 remains Historical/0; target 792 remains Current/64; movements 855/856 and audits 173835–173837 remain preserved. Receipt-ledger fingerprint remains c80672ba8b447f56ba3da1d4b1dc328d.
- Backup 184's cached package was independently reverified and freshly restored to isolated PostgreSQL on 2026-10-07. ZIP CRC, size/SHA, all component hashes, manifests and complete dump passed. This is the October 5 recovery baseline, not a new release-window backup or fresh remote read-back.

No migration or installer change. No merge, deployment, production repair, backup execution, configuration change or cron change is included.

## Recovery boundary

The older web and PR #267 worker share the same database schema. PR #267 changes backup photo-manifest scope, not physical inventory or migrations. Its format-v3 package requires the v3-aware verifier; the older web verifier interprets it as v2 and can reject its active-versus-total photo count. Do not use that older verifier to certify a v3 package.

The existing worker can create a valid v3 package independently of the web gate. Preserve its normal schedule and lease protections. The code fix still requires review, the applicable verified release-backup gate, a fresh read-only full readiness check, and separately authorized controlled deployment. Do not repair correct inventory to force deployment. After authorized version convergence, validate health and the next backup's full archive/manifest/checksum/read-back/restore contract.
