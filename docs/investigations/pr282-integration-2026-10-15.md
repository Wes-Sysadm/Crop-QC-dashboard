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

Remaining at this integration checkpoint: final mandatory/affected runs, final
model/format/CI and exact Linux production-shaped rollback rehearsal. Current
production #283 schema is the rollback baseline; previous permission/UI behavior
returns on application rollback. Compatibility with committed new edits must be
proved, not inferred merely from no new schema. No final-review GO is claimed
until that evidence is complete.
