# PR #284: cross-scope receipt-origin evidence

Development continuation from `176eaddee066ff9b9939e4c7807887260ae5bb8d`,
application source `512940de3714eb4dec7eb26dcab282d1d6289acc`, against unchanged
main `df977d041dae2d449cf6fe5f8581e1f5cae41947`. This report covers synthetic
development only. Final commit and final-head CI are recorded on PR #284.

## Rules and scope

Specification 1.2.0, governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b`; ADR-001–008 and INV-001–006,
TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 apply.
This is a read-dependency repair, not a policy change. Receipt origin, current
physical quantity, exact surviving receipt ownership and treatment membership
remain distinct. No writer, model, migration, origin guard, readiness exemption,
aggregate reuse or historical interpretation changes.

The owner authorized synthetic databases only. No production query, protected
restore, correction, reconstruction, merge, deployment or rollback/handback was
performed. The earlier #204 restore approval remains consumed.

## Confirmed cause

The [prior production-shaped evidence](pr284-production-shaped-reverification-2026-10-16.md)
established that WP-7 / warehouse 4 / room 4 / lot 1372 / profile 17 has 501
authoritative bins in every scope. Receipt 626's original 56-bin ReceiptAdd
997 is in warehouse 3 / room 59; receipt 720's original 56-bin ReceiptAdd 1185
is in warehouse 3 / room 58, with a different original profile. Both differ from
their current receiving metadata. Broad reads loaded their original revisions;
narrow reads returned null OriginalQuantity/OriginalIdentityVerified.

`LoadRoomsAsync` selected relevant receipts globally by ID, but detected the
need for original revisions using room/warehouse-filtered ReceiptAdd rows.
Its historical-lot path performed an additional query with that same room
restriction. The destination's room membership therefore changed the proof for
an unchanged receipt. No quantity or treatment difference explained it.

The fix replaces both paths with one bounded SELECT of compact ReceiptAdd fields
by already-relevant receipt IDs across facilities. A dictionary of the receipts
already loaded in the same snapshot supplies the comparison, avoiding a second
receipt scan or join. It retains the exact quantity/crop/grower/profile mismatch
predicate. The existing first revision
ordering (CreatedAt, then Id), scalar/object parsing, false/unknown results,
and receipt-evidence interpretation remain unchanged. These external
origin rows are dependencies only; they never enter the destination's quantity
ledger. Existing ReceiptId indexes support the lookup. There is no N+1 query or
unbounded all-history request.

A strengthened regression also demonstrated that changing an original receipt
under a different historical profile could leave the watermark unchanged while
the interpreted `56/false` result remained the same. The watermark now additionally
binds sorted relevant original rows and the earliest selected revision's exact
interpretation inputs. Later irrelevant revisions are not substituted or hashed
as original proof. Future-recorded dependencies make historical snapshots
unavailable. Existing serialized evidence is retained, not removed to force
equality. Source fingerprints intentionally change; previously opened forms must
refresh and existing stale-input guards enforce that. No physical or treatment
authority is added by the fingerprint.

The [capability review](transfer-eligibility-capability-review-2026-10-15.md)
documents this inspected read-only diff before its content-lock update. Four
PostgreSQL methods become mandatory INV-005 contracts. No existing contract is
removed. The provider round-trip expectation increases from 10 to 11 for any
number of selected positions because of the single batched lookup.

## Synthetic proof and integrity

`ReceiptOriginScopeTests` creates four receipt references sharing one destination
identity after recorded cross-facility transfer debit/credit pairs. Their current
quantities are 64, 56, 54 and 64: **238 destination bins**. This is deliberately
synthetic; it does not reproduce or newly authorize all 501 production bins.

| Synthetic receipt | Current bins | Original bins | Original identity verified |
| --- | ---: | ---: | --- |
| 100000 (626 equivalent) | 64 | 56 | true |
| 100001 (720 equivalent) | 56 | 56 | false |
| 100002 | 54 | null | null |
| 100003 | 64 | null | null |

The initial test failed against unchanged code: broad proof retained both
original snapshots, while all three narrow variants returned null. With the
fix, complete position objects—including receipts, treatment, ledger, watermark
and version dependencies—match for all-room, warehouse, single-room and canonical
command scopes. The actual outside-transfer selector's SourceKey carries the
same fingerprint. The exact synthetic proof is retained in the companion JSON.

The fixture intentionally retains an unsupported correction. The real
InventoryOriginGuard rejects a receipt-change parent that credits more bins than
its recorded debits. Exact receipt-specific operation on the unverified receipt
also remains blocked. A corrected loader is not an authorization to move that
pool. Production ledger 1187 and the separate 56 origin failures are untouched.

All-table before/after hashes match around the reader/selector/guard sequence.
Relevant receipt versions, original proof, revision completion and treatment
reversal change dependent fingerprints; an unrelated receipt does not. Existing
stale-selection rejection remains. Separate PostgreSQL sessions prove a
repeatable-read snapshot retains the earlier receipt version across a concurrent
commit. ReadCommitted is rejected. Historical cutoffs retain the unavailable
snapshot diagnostic and do not display not-yet-arrived destination stock.

Negative cases preserve earliest-revision priority, location-independent original
proof, false proof for malformed/contradictory snapshots, unknown proof for
missing origins/revisions and nonmultiplication of receipt evidence with a
conflicting origin. No shared-pool ancestry is manufactured. Existing affected
tests cover depleted/consumed stock, treatment chronology/reversals, internal and
cross-company routes, receipt identity/location corrections and command races.

An existing affected location test used `DateTime.Now` for a Pacific form. On a
UTC Linux runtime it sent a future business timestamp and correctly failed the
command guard. Only its input now uses the existing Pacific business-time service;
the future-time guard is unchanged. The corrected test passes.

## Validation and performance

**GO for focused human review; NO-GO for release advancement.** The final
combined run passed **270 distinct cases**, including **202 mandatory contract
cases / 89 required methods** and the **111 affected cases**, with zero failures
or skips. These sets overlap; they are not added together. Final .NET and
PostgreSQL tests ran after both the scope repair and the strengthened dependency
fingerprint change. The final original-proof regression fingerprint in all four
scopes (also asserted in the actual selector SourceKey) is
`896F05F182BD94EE8D9090B65064691F3B9A3C86538549A055C57178A2CF51C4`.
The [companion JSON](pr284-cross-scope-receipt-origin-2026-10-16.json) retains each
scope's complete receipt evidence and versions, measured rows/queries/plans,
runtime/DLL identities and integrity/cleanup proof.

The 29 Node governance cases and 20-rule metadata check pass. Solution restore,
Windows Release build (zero errors; 74 existing dependency/compiler warnings),
Linux Release test build, EF pending-model check, changed-file formatting and
`git diff --check` pass. Formatting reports existing workspace-loading warnings.
No schema migration, installer or hardware test is required by this read-only
change. Final-head GitHub checks must pass; see the PR's exact-head Checks result.
No production-shaped result is claimed by this development run.

The first SQL-join attempt was rejected: on the broad synthetic fixture its plan
made 6,123 receipt-index probes, repeatedly evaluating the large ID array, and
took 1,830.092 ms despite an estimated cost of 154.27. This was a material new
regression, with no JIT involved. The final compact query removes that join and
performs the existing mismatch comparison in memory. No database tuning, timeout
increase or JIT workaround was used.

Final matched Linux x64 / .NET 9.0.21 / PostgreSQL 18.4 measurements use the same
59-room synthetic history, two application CPUs / 2 GiB, two database CPUs / 3 GiB,
and unchanged planner settings. No build/test overlapped timing. The baseline is
frozen application `512940d`; the final candidate source LF SHA-256 is
`9c205a0f0e4c483a1224097a42c0914766a0be586128661143879f3201d52953`.
Warm ranges are two rounds after process warm-up, not a production latency SLA.

| Workflow | Baseline warm ms | Final warm ms | SELECTs before → after | Rows read before → after |
| --- | ---: | ---: | ---: | ---: |
| Single room | 190.5–193.3 | 189.1–191.4 | 29 → 30 | 782 → 1,030 |
| Room transfer | 253.0–258.9 | 251.7–274.9 | 32 → 33 | 827 → 1,075 |
| Inter-crew | 3,209.2–3,268.6 | 2,996.3–3,042.3 | 36 → 37 | 20,817 → 27,064 |
| Outside warehouse | 3,145.5–3,163.4 | 2,969.0–2,979.2 | 32 → 33 | 20,812 → 27,059 |
| Processor | 3,202.4–3,222.4 | 2,969.1–3,019.0 | 34 → 35 | 21,390 → 27,637 |
| All-room evidence | 2,241.5–2,263.9 | 2,051.1–2,119.8 | 29 → 30 | 18,836 → 25,083 |

The additional rows are compact origin dependencies, not newly available bins.
Single-room proof rows increase from 639 to 886; broad proof rows from 15,678 to
21,924. Query count increases by one per measured workflow, not by receipt count.
Broad selectors retain two canonical aggregates, direct all-room proof retains
one, and the room-transfer path retains its existing three; none gains the
previously rejected extra historical aggregate.

The final broad origin SELECT loads 6,123 relevant rows in **1.320 ms**; its
historical subset loads 123 in **0.058 ms**. Narrow reads load 242 in **0.516 ms**
and five in **0.020 ms**. PostgreSQL uses
`IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt` for the small lookup and
chooses sequential scans where economical. There is no receipt join, repeated
large-array membership inside a nested loop, or JIT in the origin plans. The
room-transfer high sample is 16 ms above baseline's high sample; the other warm
ranges are lower and the measured lookup cost is sub-millisecond there. No
material new regression is demonstrated by these matched samples.

All six complete output comparisons preserve every field except the intentionally
strengthened opaque fingerprint and its SourceKey/LotKey suffix: 1,026 fingerprint
and 1,177 source-key occurrences change across the compared outputs. Identity,
quantity, custody, receipt evidence, treatment applications, confidence, blocker
details, versions and selector eligibility match. All **112 synthetic tables**
retain their counts and content fingerprints, aggregate SHA-256
`1d610cdd3688c354f5d7173e21181f9549f15dd8db22fcbb8b5d29a2ccdf0753`.

The task-owned network-isolated PostgreSQL container
`596bf1d073fce8e461ee5d607c0e068bfeebf41396acfb8001c4b356ef802f47`
and its exclusive anonymous volume
`bded970336ad80356a5f8d0aa87523c59a9150aa94a29e8a871149b22625d059`
were deleted at **2026-10-16T20:25:59Z**; absence was verified. No published ports,
production credentials, production connection or protected backup were used.

## Remaining lifecycle gates

Keep PR #284 draft for focused human review. After that review, obtain fresh
scoped authorization for one read-only production-shaped restore. Recheck **all
452 positive positions**, recording every cross-scope proof/fingerprint mismatch
rather than stopping at the first known position. Verify original receipt proof
and independently retain ledger 1187's origin rejection. The previous restored
1,179-bin / six-unsealed-position gain, 20 visible positives and 56 origin failures
remain historical evidence, not fresh synthetic certification of production.

Only after that read-only gate passes may a separately authorized same-database
forward/rollback/handback rehearsal proceed, retaining committed activity. Safe
rollback activation, authenticated routes, explicit release authority, fresh
independently verified production backup and strict live readiness remain required.
Nothing in this report authorizes production data repair or release.
