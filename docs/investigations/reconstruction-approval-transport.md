# PR #277 — Lossless Reconstruction Approval Handling

[Pull request #277](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/277)
contains this separate tooling correction.

Status: **GO FOR REVIEW**. Production reconstruction and deployment remain stopped.
This is a separate release-tooling correction discovered during the production
release of merged PR #275, not another commit attributed to that merged PR.

## Scope and rule compliance

Base/current main: `fd7f691c2268779c26740e5f3fa7866390691b5d`.
Branch: `codex/lossless-reconstruction-approval`. No update from a newer main was
needed. Specification/register version 1.0.1, governance-content commit
`566413428f86266c1d3d5015010276ea0090115f`. The knowledge synchronization helper
confirmed matching fetched instructions; its initial sandbox network restriction
was resolved through the ordinary authorized read-only Git fetch.

Applicable: INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001, AUD-001,
OPS-001, GOV-001 and ADR-001–006. This is a technical transport fix, not a policy
decision. Authority remains recorded transactions/custody; projections remain
derived. Treatment and exact receipt ancestry remain separately proven. Every
existing approval, stale-state, concurrency, backup, audit, custody and treatment
guard remains the application's responsibility and is unchanged.

Changed area: PowerShell maintenance JSON transport, its narrowly scoped tests,
Windows CI and documentation. There is **no application, model, migration,
canonical writer/registry or foundational-rule change**. Local fixture repairs
use only the existing `InventoryCommandExecutor` through the merged CLI. No new
HTTP endpoint, database writer or deployment mechanism is introduced.

## Exact defect and reproduction

The prior untracked `execute-approved.ps1` did:

```powershell
$p = Get-Content -Raw $previewFile | ConvertFrom-Json
@{ preview = $p; <# approval metadata #> } | ConvertTo-Json -Depth 90
```

On PowerShell **7.6.5**, `ConvertFrom-Json` inferred `System.DateTime` with Local
kind. `ConvertTo-Json` emitted the local `-07:00` representation instead of the
application's `+00:00` representation. All recorded instants were equivalent, but
the application binds more than instant equality.

The original failure was reproduced against the existing isolated backup #189
restore using the exact merged binary. Raw stdout bytes were saved separately
from diagnostic stderr before any PowerShell JSON parsing. The exact old-wrapper
approval file was passed to `--mode=approve --request-file=...` and rejected with:

> Approval preview is stale or unsupported.

The CLI reads that file with `File.ReadAllTextAsync`, then deserializes the request
with web JSON defaults. The unchanged `SamePreview` method in
`InventoryCommandProjectionReconstruction.cs` serializes both typed previews with
only `SnapshotCapturedAt = null` and compares the complete strings. It does not
normalize every `DateTimeOffset` to another offset. Twenty altered timestamps
therefore remain significant after excluding the capture timestamp.

The exact emitted preview, wrapper input, old approval file and CLI error are
retained in restricted local evidence. The preview was **10,746 bytes**, SHA-256
`49ba5415d2a92cf0bd29a1d6554ff7954653b950d0b3d6da33c95fef23034962`.
The old approval was **10,877 bytes**, SHA-256
`cad02ebdfa48515885301ad3412b5cb3566b8c7a71dfa0cf9b0d9164e59f6ae3`.
The command request file is checked unchanged before/after every corrected CLI
invocation; no endpoint instrumentation or server code alteration is used.

Complete structural comparison found exactly these **21 changed values**:

| Preview path | Count |
|---|---:|
| `plan.changes[0..1].beforeUpdatedAt` | 2 |
| `snapshot.ledger[0..2].at` and `.recordedAt` | 6 |
| `snapshot.projections[0..1].createdAt` and `.updatedAt` | 4 |
| `snapshot.movements[0..2].at` and `.createdAt` | 6 |
| `snapshot.watermark.versions[0..1].updatedAt` | 2 |
| `snapshotCapturedAt` | 1 |

For example `2026-09-04T15:07:31.459524+00:00` became
`2026-09-04T08:07:31.459524-07:00`. Exact before/after values for every path are in
the [machine evidence](reconstruction-approval-transport-evidence.json).

A complete non-whitespace token comparison found **22 changed tokens out of 949**.
The additional token is `beforeSegmentsJson`: Unicode quote/plus escapes were
rewritten as escaped quotes/literal plus signs. Its decoded string value was
unchanged, so it was not the stale-guard cause after typed deserialization. This
distinction matters: timestamps were not the only textual rewrite. All remaining
927 tokens, property/array order and decoded non-timestamp values matched. The
old wrapper also reindented the nested object. The corrected transport preserves
all these representations, including whitespace, without special-casing records.

## Implementation and actual wrapper path

The versioned replacement is in [scripts/reconstruction](../../scripts/reconstruction/README.md):

- `ReconstructionProtocol.psm1`: strict UTF-8/JSON validation, metadata whitelist,
  duplicate-name rejection, and opaque insertion of the entire original preview.
- `Write-ReconstructionRequest.ps1`: separate approval/execution envelope entry.
- `Invoke-ReconstructionMaintenance.ps1`: explicit argument list, separate
  concurrent stdout/stderr byte capture, original command exit code and immutable
  output files. This also avoids the earlier scalar-splat argument failure.

The composer uses a JSON DOM solely to validate objects and inspect envelope
property names. It does **not** serialize any preview DOM/object or interpret a
timestamp. It appends the original preview text verbatim to operator-owned
metadata and writes strict UTF-8 without a BOM. Invalid UTF-8, mixed logs, duplicate
keys, unexpected envelope fields and overwrites fail before creating a request.
There are no six-pool identifiers in the production transport code.

Every restored approval and execution went through these actual PowerShell
scripts, with a byte-for-byte assertion that the preview region equals the
original emitted stdout. Every operation used the exact same preview file for
approval and execution. No hand-edited request, direct executor substitute or
lossy PowerShell object was used for the passing path. The old stopped production
artifacts remain evidence and must not be reused. Future release integration must
retain the existing workspace/SHA/backup/maintenance checks around this transport;
this task does not invoke that production wrapper or resume the release.

## Isolated backup #189 rehearsal

Source backup SHA-256:
`674c9c86d34067b1ca2e0e00dd301c2b7d4917174a2a87e9cf573c7fedc02d58`.
Existing verified restore: `pr271_release_pr275_189` on localhost PostgreSQL 18.
Snapshot `2026-10-08T23:52:26.0191Z`, serving source `e58d73a`.
The application binary remained the exact merged PR #275 publish at
`fd7f691c2268779c26740e5f3fa7866390691b5d`; all three assembly hashes were checked
against the recorded manifest before the rehearsal. Web SHA-256:
`93350D9F3F390959B1F7703D28EAC4A91C3882466EB34B438832D8CDEB6B19ED`.

`test-restored-protocol.py` is an opt-in, local-only fixture. It has no Render
access or credential discovery and pins the connection to localhost with the
existing approved disposable naming contract. Its six hardcoded cases are
historical test fixtures only. It captures full readiness before/after, independently
hashes protected tables and invokes the actual corrected wrapper for all commands.

| Pool | Derived before → after | Authority before = after | Approval / repair audit | Replacement |
|---|---:|---:|---|---:|
| Evans-5 / 3152 | 162 → 61 | 61 | 176594 / 176595 | 834 |
| Evans-5 / 9682 | 536 → 252 | 252 | 176596 / 176597 | 835 |
| WP-7 / 1372 | 1,568 → 1,122 | 1,122 | 176598 / 176599 | 836 |
| DH-15 / 2350 | 598 → 202 | 202 | 176600 / 176601 | 837 |
| WP-5 / 1084 | 130 → 10 | 10 | 176602 / 176603 | 838 |
| WP-8 / 2350 | 362 → 170 | 170 | 176604 / 176605 | 839 |
| **Total** | **3,356 → 1,817** | **1,817** | **6 approvals + 6 repairs** | **6** |

These IDs exist only in the disposable restore. All six executions returned
`Committed`, independent verification returned `Verified`, and identical-intent
replay returned `Replayed`. A final independent CLI verification of all six after
the complete sequence also passed. Six committed journals and exactly 12 scoped
audits were present; failed approvals added no audit/journal.

All **26 protected group** row counts/full-row hashes matched before/after,
excluding only the explicitly scoped derived replacements/retirements and their
new reconstruction audit/journal rows. Receipts, ledger, transfers, corrections,
consumption/loss, treatment applications/sources/movements and unrelated segments
were preserved. Six shared untreated residuals were created without inventing
receipt ancestry; all intended historical segments were retained as superseded.
The verifier independently reloaded authoritative quantity, current projections,
identity, custody, treatment and history. No authoritative bin was added or lost.

Normal full readiness **passed naturally** after reconstruction:

- Schema: PASS, 979 expected objects.
- Inventory deductions: PASS, 1,079 inspected; zero blockers.
- Custody topology: PASS; zero invalid or missing records reported.
- Treatment: PASS, 441 current identities, **zero blockers** (six before).

This verifies the backup snapshot only. It makes no claim that production was
repaired or that newer production receiving activity is represented in this restore.

## Negative guards and tests

- A quantity-modified preview transported losslessly was rejected by the original
  stale-preview guard. No audit was created.
- A previously valid preview became stale after a real committed reconstruction;
  transporting it unchanged still failed the same guard.
- A second PostgreSQL session held a writer lock; the unchanged approval command
  failed immediately with `55P03`. The lock was rolled back without a data change.
- The original lossy timestamp payload reproduced the production rejection locally.
- Unchanged fresh previews were accepted, with all existing guards active.

| Validation | Result |
|---|---|
| Solution restore / Release build | PASS; 0 errors, 69 existing warnings |
| New transport tests, actual PowerShell | **21 passed**, 0 failed/skipped |
| Existing focused .NET tests, actual PostgreSQL | **76 passed**, 0 failed/skipped |
| Breakdown | 57 dynamic reconstruction, 8 reconstruction, 9 writer architecture, 2 governance architecture |
| Governance metadata tests / catalog check | **22 passed**; 20-rule catalog check PASS |
| EF pending-model check | PASS; no changes since last migration |
| PowerShell parser, JS syntax, Python compile, whitespace | PASS; no C# formatting target changed |
| Six-pool actual-wrapper restore rehearsal | PASS, including negative guards, replay, fingerprints and full readiness |
| Broader application suite / UI / installer | Not run; unchanged and outside this transport boundary |

The Windows CI workflow adds only the focused transport tests; existing governance
and contract workflows are retained. GitHub check results and final PR/head identity
are reported with the pull request. Human review/merge is still pending; automated
tests do not approve policy or production release.

No production connection, reconstruction, configuration change, backup operation,
merge or deployment occurred during this tooling task. There is no MSI/hardware
impact or schema rollback requirement. The application guard was not normalized,
weakened or bypassed. **GO FOR REVIEW.**
