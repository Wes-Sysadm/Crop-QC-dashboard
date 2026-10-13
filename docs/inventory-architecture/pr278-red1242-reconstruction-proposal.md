# Separate proposal: McDougall room 73 / RED / lot 1242

**Not executed or approved for production. Not a migration or release step.**
This proposal uses the read-only 2026-10-09 14:46:28.269371 UTC observation and
14:56:41.564392 UTC supplement linked in the [investigation](movement-event-authority-investigation.md).
Refresh the complete ledger, movement, application-source and projection evidence
and its fingerprint before any separately authorized repair; changed state requires
a new proposal. Do not reuse these numbers as an authorization token.

Scope: warehouse 3, room 73 (MCD-02), grower/lot 430 / 1242, fruit profile 14 / RED.
The authoritative room quantity is **652**, while segments 729 and 730 total
**442**. No RED/1242 withdrawal is present in the observed complete room ledger.

| Origin/arrival evidence | Receipt attribution | Remaining bins | Treatment |
|---|---|---:|---|
| ReceiptAdd 3821 | 2252 | 56 | Untreated |
| ReceiptAdd 3835 | 2266 | 56 | Untreated |
| ReceiptAdd 3863 | 2294 | 68 | Untreated |
| ReceiptAdd 3931 | 2334 | 68 | Untreated |
| ReceiptAdd 3978 | 2343 | 68 | Untreated |
| ReceiptAdd 4016; application source 40 | 2378 | 70 | Application 29 |
| ReceiptAdd 4024 | 2386 | 70 | Untreated |
| ReceiptAdd 4066 | 2419 | 70 | Untreated |
| ReceiptAdd 4100 | 2439 | 70 | Untreated |
| Transfer 411; ledger 4074; movement 801 | Shared / null | 56 | Untreated |
| **Total** | | **652** | **582 untreated + 70 application 29** |

The **210-bin gap is 70 + 70 + 70 from receipts 2386, 2419 and 2439**.
The earlier untreated projection is 372 = 316 original receipt arrivals + 56
shared transfer bins. Segment 730 already represents receipt 2378's 70 treated
bins correctly. Application 29 does not apply to the three later receipts.

The transfer has upstream funding through receipt 2262, source ledger 3831 and
debit 4073, but movement 801 has no exact receipt allocation. Retain its 56 bins
as shared; do not write receipt 2262 onto them merely because quantities match.

Proposed bounded action, only after separate approval and current release/backup/
readiness gates: audit and supersede the defective current projection representation
using the refreshed complete event proof; retain old rows, application links,
retired quantities, actors, timestamps and before/after fingerprints. The proposed
ten disjoint cohorts above are derived replacements. They create **zero** bins
and change **no** ledger, receipt, transfer, movement, loss, packing or application
record. A projection-only result must reconcile to the same authoritative 652.

Required acceptance: exact 210 projection increase, 652 physical bins before and
after, 70 under application 29 before and after, shared 56 retained, all protected
history unchanged, and idempotent replay of the approved reconstruction. The
separate ATGL/profile 22 position and all other lots are outside this proposal.

Deploying PR #278 runs no reconstruction command. Its additive migration changes
only cohort schema/indexes. The final rollback rehearsal deliberately retains this
210-bin historical gap while exercising newly received/moved cohorts.
