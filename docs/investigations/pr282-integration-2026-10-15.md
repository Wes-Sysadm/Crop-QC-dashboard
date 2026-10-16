# PR #282 integration of approved PR #283

Development and review preparation only. Owner Wes authorized this continuation
in “PR #282 — Integrate PR #283 and Finish Admin Receipt Editing”, 2026-10-15,
conversation `01a117ce-8381-7602-b396-3c44cc35402e`. No merge, deployment or
production correction is authorized.

## Starting state and compliance

Existing branch `codex/receipt-correction-form-contract`, initial head
`0e4fd225370337de9cd2e7d633214d8530ce7f8b`, old base
`3002d0774f52ee5e94aaef81dbd0d62c6bed335b`; fetched new main/base
`ea307b168e563d432dd4f513da6c06f4e5e54d5e`. Report-only synchronization identified
two branch commits and five incoming commits, proposed policy differences, and
untracked investigation reports. All untracked work was preserved; no reset,
stash, force-push or replacement PR. Main specification is 1.1.0, governance
evidence commit `38733d2633af9f1155dab83a1c468a6914ce20f8`; proposed 1.2.0 /
ADR-008 remains unadopted until normal approval/merge.

Rules: REC-001/002, INV-001–006, TRT-001/002, MOV-001, ROOM-001, AUD-001,
OPS-001 and GOV-001; ADR-001–007 retained, ADR-008 records the already requested
permission/UI change. Authoritative transaction origin, exact deltas, unique
custody, selected receipt ownership, uncertain shared ancestry, point-in-time
treatment and immutable audit history remain mandatory. Strict readiness and
canonical writer fences are not weakened. No new writer, command, composite
transaction, schema migration or production repair is introduced.

Affected area: existing Admin Edit GET/POST, dispatcher, metadata/quantity/
identity/location adapters, live permission and retry boundaries; directly
dependent receipt evidence and split-restoration readiness from #283. History:
original receipt origins, ledger, dump/loss, movement, treatment applications and
sources, previous audit rows. No unrelated QC/photo/email/installer testing.

## Deliberate merge resolution

Only `CanonicalReceiptIdentityWorkflowTests.cs` conflicted, in three regions:

1. After restoration: keep #282 diagnostic output, #283 authority/available/
   excluded-excess checks (19 and 26, target excess ten), exact seven-bin receipt
   proof, immutable history and replay assertions.
2. After reclassification: keep diagnostics, #283 19/26 authoritative positions,
   ten-bin excluded excess in each, exact receipt proof and retained-history checks.
3. Helpers: retain both `Describe` and #283 `CaptureHistory`, `AssertPosition`,
   `AssertReceipt`. Do not reintroduce raw projection equality.

The merged traceability and reviewed writer manifest retain both sides. #283
reader/readiness code is unchanged; #282 source implementation is unchanged in
this integration. A new three-case HTTP contract exercises the combined one-Save
Admin boundary for room-held, outside-held and consumed exact seven-bin receipts,
then outside return/loss reversal. It binds retries to intent, excludes historical
projection excess, checks exact ownership without assigning the shared nineteen,
and compares original ledger/movement/treatment/audit history. Aggregate treatment
slice reference sets are not used as exact receipt allocation proof.

## Workflow and validation scope

One existing Edit page, required entered reason and one Save Receipt; no new UI.
Supported metadata updates use their audited command; quantity, structured identity
and location use their existing separate atomic commands. Cross-kind edits fail
before writes. Omitted internal LotCode is not an identity request; explicit
tampering, stale tokens, changed-intent retries, insufficient selected proof,
unknown treatment, seals and contradictory custody remain blocked. Live Receipts
Admin is enforced before replay and writes. Creation/QC/treatment/dedicated
custody permissions stay separate.

The merged first run passed 30 selected HTTP/identity/receipt-specific/restoration
cases, including all four prior failures. The new integrated HTTP theory passed
all three cases. Development setup exposed test-only constructor and aggregate
slice assumptions; they were corrected to the actual canonical source contract
and receipt-specific reader before counting PASS. No application guard changed.

The existing rendered 13-consumed/15-corrected regression proves original origin
13, immutable October 7 dump 13, audited origin +2, revised count 15 and current
owned stock 2 in the selected proven room. Missing presence/location/treatment
proof is rejected without writes. The added two have no prior treatment. This
is synthetic evidence, not authorization or proof for any named production receipt.

## Executed validation and frozen artifacts

Integration commit: `b049f89b641877b6165c31fc7b2e5f3c3e6e0386`, a normal merge
whose parents are original #282 head and approved #283 main. This is the frozen
executable source; later report-only commits do not alter its runtime content.

| Check | Result |
| --- | --- |
| Solution restore / Release build | PASS, zero errors; existing package/nullable/analyzer warnings retained. |
| Initial combined HTTP / identity / restoration selection | 30 PASS, zero failed/skipped; includes the four formerly failing cases. |
| New one-Save mixed-custody theory | 3 PASS; room, outside return, loss reversal. |
| Mandatory PostgreSQL / architecture / rule contracts | 161 PASS, zero failed/skipped. Both parents' mandatory test selections verified retained. |
| Additional affected permission/correction/custody/concurrency/admission selection | 183 PASS, zero failures, three initially unconfigured entries; see below. |
| PostgreSQL date-accounting theory, configured rerun | 22 PASS, zero failed/skipped. |
| Governance | 29 Node tests PASS; metadata PASS for all 20 rules. |
| Model / changed-file formatting / whitespace | PASS; no pending model change or new migration. |
| Linux build and runtime identity | PASS; frozen production SDK/runtime digests, Linux amd64, .NET/ASP.NET 9.0.21. |

Counts are per run and overlap; they are not a sum of unique tests. The initial
affected selection skipped one PostgreSQL date theory because its distinct
`CROPQC_TEST_RECEIPT_DATE_POSTGRES` setting was absent. Its configured rerun executed
all 22 rows successfully. Two restore-only race cases remain unexecuted without
`CANONICAL_INVENTORY_RESTORE_POSTGRES`; synthetic PostgreSQL concurrency tests and
all mandatory contracts did execute. Neither skipped case is counted as proof.
No unrelated full application suite or MSI test was run.

Local TRX: `artifacts/pr282-integration/{integration,mixed-http,affected,postgres-date}.trx`
and `artifacts/governance/28c5459709e146d6b93afc32d58f4a40/contracts.trx`.
Final required CI must be read from the current PR head; the draft is mergeable
against main. No architecture exemptions or assertion deletions were used.

Frozen candidate image `cropqc-pr282:integration-b049f89`, immutable local OCI
identity `sha256:44c5d842ec7d47866b6808d0d5f73b591dc185908b05723db66cd7bc7752a9b0`;
archive `candidate-linux.tar`, SHA-256
`b94f7bb4886d6dc7f8623b8bf8b27981e1d9656c001ba068cbabc7dbbe675e2f`.
SDK `sha256:2f5eb32870b4c0074a3d5b5191a03553bcab7b74463a76a7c0a5f3ef65c90fe5`,
ASP.NET runtime `sha256:7da398155e0d6439cf0058bc1afa937817a03e320714b876a5f58a7d64f79d50`.
This is a local Linux artifact, not a Render deployment claim.

Rollback candidate: current production #283 source/main
`ea307b168e563d432dd4f513da6c06f4e5e54d5e`, whose tree equals reviewed #283 source
`80df2bffc5ca1a91973702b1c2313aff34721a62`; retained exact image
`sha256:f65d1b54b331c4bc11d902c62cef81de80a4a17c051e7af8d3b36547d4131318` and
archive SHA-256 `671127a939c44f5bca0f99dbfb95d81274b401f138ab0884aac806044effbbb7`.
It is compensation-aware and compatible with the existing 34-migration schema,
but **post-#282 workflow compatibility is not yet claimed**. The prior edit UI and
less restrictive receiver permission behavior return on rollback; new policy
and affected operators must account for that limitation.

## Remaining preparation safeguard

A fresh disposable restore of retained verified backup #203 is justified before
final human review: test the changed Admin dispatcher against production-shaped
history, then execute rollback reads/writes after committed new edits while
preserving all original evidence and strict readiness. The prepared harness uses
only synthetic added fixtures and the existing unchanged command engine. It was
compiled against frozen candidate DLLs; compilation is not rehearsal success.

Automatic approval review rejected the restore command twice before execution.
The first reason was insufficient authorization for copying production data into
a local test database. The second rejected the explicit Step 9 quotation because
it did not accept attachment text as trusted authorization. The request actually
states “A previous production backup is permissible as isolated rehearsal input.”
No workaround was attempted after the rejection. An explicit text confirmation is
pending for already-local backup #203, a disposable local Docker database with
`--network none`, no published ports, no production credentials/connection, and
cleanup after rehearsal. No restore container was created by those denied calls.

**NO-GO for final human review until the required production-shaped rollback
rehearsal and final-head CI are complete.** This is an unproven safeguard, not a
known failing inventory or Admin-edit behavior. No production resource was
accessed or modified. No merge, deployment, correction, migration or historical
reconstruction occurred. Keep #282 draft. A later release separately requires
owner authorization, a fresh independently verified backup, exact merged runtime,
schema/readiness checks, compatible deployable rollback and independent live
verification. Backup #203 cannot substitute for that fresh release backup.
