# Admin receipt editing — ADR-008 adoption record

Status: owner-approved policy instruction implemented for review in draft PR #282;
not effective on main until normal merge. Release is not authorized.

Owner: Wes. Reference: “PR #282 — Complete the Existing Admin Receipt Editing
Workflow”, CHANGES REQUESTED on b0aec08d3eb280f34d3d4d0b873c5357e6b1b63f,
2026-10-14 America/Los_Angeles, conversation
01a117ce-8381-7602-b396-3c44cc35402e. The instruction explicitly requires
“Use the established Receipts Admin permission for editing existing receipts”
and “Admins may initiate corrections without arbitrary age restrictions.”
This records the actual owner instruction, not another reviewer or a GitHub approval.

Previous policy/behavior: receiver Create permission could correct receipt counts;
Web metadata editing accepted receiving permission; the API metadata route accepted
receiving permission and imposed a same-day restriction. Identity/location corrections
required Admin. Separate review/apply controls and generic metadata reasons remained.

New policy: all existing receipt metadata, quantity, identity and location edits
require live Receipts Admin and an operator-entered reason. Age alone is not a block.
Use one Save on the existing Edit page. Compare with saved authority and dispatch
one established atomic operation. Metadata fields may share a metadata command;
structured identity fields may share an identity command. Cross-kind changes need
a future reviewed composite command, not sequential independent commits.

Affected adapters: Web Edit and compatibility AdminInventoryOverride POST, the
Dashboard metadata adapter, ReceiptInventoryOverrideService, API PUT
`/api/receipts/{id}/same-day-fields`, and canonical existing-receipt commands.
The persisted permission levels are None/View/Create/Admin; `PageAccessLevel.Edit`
is an alias of Create. The legacy receipt-edit area alone does not grant Receipts
Admin. Tests cover both Create and legacy Edit-only configurations.

The API route name and journal's `SameDayOnly` field remain for wire/replay
compatibility; their former age restriction is superseded. The field still selects
the API's existing QC review behavior on name changes. Repository search found no
station/WinForms callers of this PUT route; external Create/Edit-only clients must
use an authorized Admin session. Creation, QC sampling, receiving treatment and
dedicated transfer acknowledgement/placement permissions remain separate.

History/migration/rollback: no schema migration or production data conversion.
No history is rewritten by adoption. Unchanged displayed minute values retain the
saved precise timestamp. Date edits that cross opening inventory or relevant
treatment applications require explicit evidence review. Rollback to the prior
application would restore the previous permissive permission policy; release must
review that limitation and the existing compensation-aware rollback requirement.
Database restore over newer business activity is never an application rollback.

Safeguards: exact receipt ancestry where required, quantity/custody conservation,
point-in-time treatment, seals, required proof, antiforgery, operation keys,
concurrency, canonical writer fencing, strict readiness and all OPS-001 release
gates remain. A rejected save retains submitted values and reviewed tokens.

Validation ownership: REC-001 traceability names the PostgreSQL HTTP and permission
contracts. The #282 investigation report records actual outcomes and remaining
identity limitations. Passing tests do not approve merge, deployment or a repair.
