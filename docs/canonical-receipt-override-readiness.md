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

The 24 negative cases damage only detached in-memory copies of genuine service-generated evidence. Committed PostgreSQL records remain immutable. Missing/mismatched journal, parent, hash, actor, type, version, source, lot, variety, status, room, quantity, balance, snapshots, receipt provenance, duplicate allocation and movement/treatment evidence remain blocked.

- Focused new tests: 25 passed, zero failed/skipped.
- Combined relevant suite: 124 passed, zero failed/skipped (new tests, legacy receipt overrides, deduction invariants, canonical receipt identity workflows, architecture/writer coverage).
- Two optional old PostgreSQL receipt-override tests were explicitly excluded rather than counted as exercised without their fixtures. New canonical tests ran on disposable PostgreSQL.
- Restore/build, changed-file formatting verification, EF pending-model check and diff whitespace check passed. Existing unrelated compiler warnings remain.
- The exact scoped production ledger/override/correction/command/movement JSON also passed the public readiness service in a local in-memory harness: zero findings and no tracked writes. This is scoped evidence validation, not a claim that the revised full production gate has run.

No migration or installer change. No merge, deployment, production repair, backup execution, configuration change or cron change is included.

## Recovery boundary

The older web and PR #267 worker share the same database schema. PR #267 changes backup photo-manifest scope, not physical inventory or migrations. Its format-v3 package requires the v3-aware verifier; the older web verifier interprets it as v2 and can reject its active-versus-total photo count. Do not use that older verifier to certify a v3 package.

The existing worker can create a valid v3 package independently of the web gate. Preserve its normal schedule and lease protections. The code fix still requires review, the applicable verified release-backup gate, a fresh read-only full readiness check, and separately authorized controlled deployment. Do not repair correct inventory to force deployment. After authorized version convergence, validate health and the next backup's full archive/manifest/checksum/read-back/restore contract.
