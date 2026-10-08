# PR #275 final integration and owner review

## Governance and scope

This continues draft [PR #275](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/275)
on `codex/recorded-history-reconstruction`. Approved main and base:
`057dfd1d49bb8f3f2017ff583b292645fce63fcc`. Specification and ADR register:
**1.0.1**; last governance-content commit reported by the synchronization tool:
`566413428f86266c1d3d5015010276ea0090115f`. The final PR head accompanies this report
in the completion handoff; this document is committed with that head.

Applicable rules: **INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001,
AUD-001, OPS-001, GOV-001**, and **ADR-001–006**. This is an implementation repair
and evidence update, not a policy change. No business decision or foundational
rule change is required or made. Main's governance files are preserved verbatim.

The review read current AGENTS, the catalog/register, change/validation/sync
procedures, Phase 1–3 contracts, scoped-testing and release standards, both prior
reconstruction reports and all six reconstruction implementation files. Quantity
authority remains `RoomInventoryLedgerQueryService` through `InventoryEvidenceLoader`;
external custody remains the existing canonical custody contract. Projection
arithmetic measures representation coverage only. There is no new origin or
physical quantity writer, and no ordinary receiving/treatment/resolver change.

Main was merged without rewriting published history at
`a1bb64b61347bd3d25073e9123c027eef21e93e0`; there were **no main-integration
conflicts**. The report-only synchronization check ran successfully under PowerShell
7 and returned its expected manual-review exit 2: no incoming main commits, exact
governance agreement, but PR-specific reports and writer classifications differ
from main. Its HEAD-to-main `D` entries describe feature-only reports, not deleted
working files. An initial Windows PowerShell invocation could not load the script
under its execution policy; no persistent policy setting was changed.

## Demonstrated defects and corrections

1. **Immutable audit/journal changes could be downgraded to current-state review.**
   With valid later receiving, alteration of a pre-repair audit or command record
   did not throw. Existing protected membership hashes now reject changed original
   `AuditLogs` and `InventoryCommands`, alongside ledger and movement history.
   Newly appended records remain permitted. This preserves AUD-001 and prevents
   later activity from excusing corrupted original evidence.
2. **Resolver evidence omitted persisted descriptive/origin metadata.** Changing a
   replacement's grower snapshot could pass verification because the resolver
   deliberately carries fewer fields. Verification now compares sealed persisted
   current rows against their original metadata, allowing only lifecycle fields
   that later validated operations may change. Deleted committed rows fail.
   Retired rows additionally require the exact journal commit timestamp.
   Four new tampering cases cover original audit, original command, replacement
   metadata and retirement clock. Corrected-fixture pre-fix execution demonstrated
   three failures (23 passed); retirement-clock rejection was already covered by
   another guard, and now has an explicit timestamp contract.
3. **#271 positive corrections have a deliberate new-arrival clock.** Two combined
   regressions failed because #275 required every effect to use the intent's
   effective timestamp. #271 posts explicitly confirmed positive untreated
   corrections at commit time. The maintenance proof now recognizes that exact
   serialized contract only for positive receipt-override effects and untreated
   receipt-correction movements. Other effects still require the original clock.
   This does not duplicate or alter #271's writer or permissions.
4. **Approval eligibility did not account for the newer custody writer fence.**
   Once any immutable receipt acknowledgement exists, #271 requires a
   compensation-aware writer. #275 does not yet include acknowledgement,
   placement and reversal tables in its protected execution manifest. Preview
   now identifies the first actual `ReceiptCustodyAcknowledgment` and explicitly
   blocks reconstruction while that contract is in use. It does not bypass the
   fence, acquire its capability, reinterpret held bins or claim partial-receipt
   repair support. This is a maintenance integration limitation, not unknown
   physical inventory or a request for external paperwork.
   Independent verification also returns historical-commit-only/current-review
   status when this newer custody exists, explicitly declining to certify held
   allocation. A combined-only regression creates a real later partial
   acknowledgement after repair to check that distinction.

The registered capability for `InventoryCommandProjectionReconstruction` remains
CLI-only audited projection maintenance: retire proven derived rows, optionally
create a shared residual, append approval/execution audits and a command journal.
It cannot write physical ledger effects. The verification and command-evidence
helpers remain read-only. Only these three substantively reviewed file hashes
changed in #275; no writer exemption, scanner bypass or operational capability
was added.

## Authority, treatment and verification assessment

Fresh snapshots, exact identities, signed canonical events and custody continue
to own quantities. New receiving, transfers, packing, corrections, voids, loss,
processor/outside custody and compensating movements are covered through existing
contracts; transfer-matching receipts have zero additional receiving-origin credit.
Backup #186 values remain historical fixtures. New proven rows are retained; only
demonstrated projection excess is superseded. No FIFO/proportional receipt
assignment is introduced. Shared untreated quantity remains usable by ordinary
operations with sufficient proof; receipt-specific operations retain exact ancestry
requirements. Correction reasons do not allocate surviving bins.

Treatment remains point-in-time. Later arrivals/correction additions do not gain
old room treatment, and treated transfer allocations retain application links.
Mixed/unknown reconstruction remains blocked with the relevant projection,
application or command evidence. This bounded maintenance algorithm is not a
general treatment allocator. Valid room eligibility and ordinary workflow guards
remain unchanged; global release readiness still requires persisted correction.

`Verified` certifies the sealed repair and unchanged current evidence.
`VerifiedWithLaterActivity` additionally proves later canonical allocations and
current quantities. `CommittedEvidenceVerifiedCurrentReviewRequired` certifies
only the original repair; it **does not certify the current allocation** or clear
a release/repair gate. Mutable parent changes or complex later treatment may
require that review, but original immutable audit/journal/ledger/movement changes
now reject outright. Verification is read-only. Serializable execution, exact
preview/approval binding, NOWAIT locks, rollback, same-intent replay and uncertain
commit recovery remain in place.

## Pending PR integration

The following heads were fetched and inspected; all remained open drafts:

| PR | Reviewed head | Finding |
|---|---|---|
| #271 | `58e6d29edc9d81326274a3d7ff73c56dde20fcc5` | Partial custody is implemented here, not copied into #275. Its positive-correction clock is now understood by the proof. Repairs after custody acknowledgement remain explicitly unsupported. |
| #272 | `9fcca90e206460d55b030a87017d587212b5a29f` | Adds explanations after the original strict discrepancy scan. A proven pool still fails readiness until persisted excess is corrected. Retain this diagnostic implementation. |
| #273 | `5c380508668fe078007212db49eed840a0c46d7f` | Earlier form of the same maintenance API/CLI, DTOs and projection lifecycle. #275 contains and extends that functionality; no extra schema migration or second command is needed. |

Recommendation: review **#275 as the reconstruction successor to #273**. Do not
merge both implementations independently. Preserve #273's historical report and
approval history already carried in #275; owner disposition of the older PR is
separate. #272 is complementary. #271 can coexist at code/schema level with the
explicit post-acknowledgement repair boundary; production sequencing must not
silently assume unrestricted combined repair support.

A separate, unpublished `codex/pr275-final-integration-review` candidate combines
#275/current main with #271 and #272. Original PR branches and main were untouched.
The only textual conflict was `Program.cs`'s registered hash: the merged host
retains #275's explicit CLI dispatch and #271's 1,007-object schema expectation.
The merged host was inspected before calculating its hash. #272's readiness
service hash was separately reviewed: only diagnostic evidence was added; the
mutating correction entry point and strict issue predicate remain unchanged.

The combined-only probe extends #271's real 50/49 lifecycle. At states
`unresolved/held/placed = 1/49/0`, `1/0/49`, `0/1/49`, `0/0/50`, it checks
conservation, normal custody resolution, exact allocation selection and read-only
reconstruction rejection naming the acknowledgement. Full settlement remains a
valid #271 operation; #275's maintenance limitation remains explicit afterward.
The reproducible probe patch accompanies this report, rather than changing #271.

## Fresh production preview procedure

**No current production read was performed in this review. Current quantities,
treatment populations and production readiness remain unverified.**

Use a separately authorized, non-pooling read-only connection and the frozen
reviewed binary in an isolated CLI process. Do not start the web host or deploy it.
Disable startup creation/seeding; set the provider to PostgreSQL and canonical
mode on. Add PostgreSQL `default_transaction_read_only=on` at connection/session
level. Do not put the connection string or credentials in request files or logs.
The CLI accepts **equals-form arguments**:

```text
dotnet CropQc.Web.dll --reconstruct-historical-projection --mode=preview --request-file=target.json
```

In the isolated process set `Database__Provider=PostgreSql`,
`CanonicalInventoryCommandsEnabled=true`, `Database__EnsureCreatedOnStartup=false`
and `Database__SeedMasterDataOnStartup=false`. Supply the authorized read-only
connection through the existing secret mechanism as `ConnectionStrings__CropQc`;
require its PostgreSQL options to include `default_transaction_read_only=on`.
Before use, check `SHOW default_transaction_read_only` through that connection.
No environment setting is persisted to a production service by this procedure.

The request is only `ProjectionReconstructionTarget`: current warehouse/room and
complete canonical identity (crop year, grower lot, Fruit Profile, grower number,
lot, variety, production, organic flag and normalized status). Re-resolve those
identifiers from current recorded evidence before preparing each file. Never
copy historical quantities, replacement IDs, fingerprints or approval records.
Run each preview once and retain its full JSON, observed UTC timestamp and binary
SHA. It includes canonical ledger/receipt/movement/application evidence, separate
supported custody, revision diagnostics, raw projections, preserved/retired IDs,
proposed residual, fingerprints and versions. Material activity requires a fresh
preview; multiple target previews are separate snapshots, not one global instant.

If the acknowledgement schema is present, use the compensation-aware canonical
reader to capture `ReceiptHeld` and `InTransit` separately, with original dispatch,
acknowledgement, placement and reversal IDs. #275 itself intentionally does not
claim a complete held-custody preview or repair after that contract is in use.
Follow the exact event identified by its blocker. For transfers, reconcile
dispatched = unresolved + held + net placed using those canonical records;
never treat receipt `BinCount` or acknowledgement as room credit. The code and
test evidence in this report are not a current production repair proposal.

Do not call approve/execute or add either mutating confirmation flag. Preview
opens a read-only transaction; verification mode is also read-only. A future
production mutation requires separate explicit authorization, a fresh independently
verified predeployment backup, current approval/evidence, bounded maintenance and
independent verification. No old snapshot is a substitute for that gate.

## Validation and operational limits

**Ready for owner review within the documented maintenance scope.** This is not
a recommendation to execute production repairs or deploy the combined candidate.
The reviewed #275 source commit is
`f62266a723d835a543ac8e783576fef28aff5591`; the unpublished combined candidate is
`48fdce2c4ea97af7f46c59389457e41c14ca3ece`. Its final ancestry merge leaves the tested
tree identical to `c962fba`. The final documentation-only commit is identified by
the PR head and completion handoff.

Fresh executions for this review:

| Check | Final result |
|---|---|
| #275 focused, mandatory business-rule and architecture contracts | 265 passed, 0 failed, 0 skipped; includes 137 explicitly PostgreSQL-attributed cases. |
| #275 concurrency contracts | 8 passed, 0 failed, 0 skipped: 6 synthetic PostgreSQL cases and 2 restored PostgreSQL races. |
| Combined #275/#271/#272 focused, custody, readiness, concurrency and mandatory governance contracts | 337 passed, 0 failed, 0 skipped; includes 184 explicitly PostgreSQL-attributed cases and 2 restored PostgreSQL races. |
| Combined restored dynamic lifecycle | 1 passed, 0 failed, 0 skipped; newly executed seed 255, 59 committed states. |
| Governance metadata/rejection tests | 22 passed; 20 catalog rules validated, including the completed PR disclosure. |
| Synchronization fixtures | 18 passed on PowerShell 7 and 18 on Windows PowerShell 5.1, zero failures/skips. |
| Restore / Release solution build | Both candidates passed; final incremental builds 12 warnings, 0 errors; preceding full builds 69 existing warnings, 0 errors. |
| Formatting / diff whitespace | Passed for changed C# and repository diff. |
| EF default design-time provider | Both candidates passed, no pending changes. |
| Explicit PostgreSQL model check | Pending-model warning reproduced on separately built unchanged main `057dfd1`; no #275 entity/model/migration changes. Baseline warning remains, not disguised with a migration. |

Provider-attributed counts are subsets, not additional tests. Remaining cases
include unit/structural/provider-specific fixtures. Earlier runs are not added to
these totals. The restored lifecycle was rerun this review; it preceded only the
final held-custody verifier classification and race-fixture correction. The final
337-case run covers both changes. All six historical repairs were independently
verified and replayed again with the final maintenance implementation. The last
combined ancestry merge changed no file contents. No previous 205-case result or
previous-turn lifecycle run is represented as a new execution.

The restored races initially failed because their legacy-inventory fixture tried
to seed an already canonical restored journal. The fixture now creates isolated
master records and receives its 19 bins through the actual canonical command;
assertions use the existing journal count as baseline. No journal was deleted or
guard disabled. Final runs include both restored races without skips. The dynamic
lifecycle similarly uses isolated synthetic masters; two facility-code aliases
are excluded from its master-fixture fingerprint. Original business history stays
protected.

The existing backup #186 archive was independently verified and restored afresh
at `2026-10-08T18:59:25.686682Z`, to local database
`pr271_release_final_integration186`. Its snapshot is
`2026-10-07T23:53:37.406346Z`, archive SHA-256
`0170afc86576e3c57483ebdf1cc3b431555b545bf1cd9e454e7e020bdf22ccc0`.
Archive CRC, four component hashes/sizes, the SQL dump and 11,768 photo references
passed. Applying #271's three local additive migrations left all **109 original
table fingerprints unchanged** (migration-history bookkeeping excluded).

| Historical pool | Authoritative bins, unchanged | Projection before | Projection after | Preview / approve / execute / verify seconds |
|---|---:|---:|---:|---|
| Evans CA 5 / 3152 | 61 | 162 | 61 | 7.936 / 7.814 / 23.183 / 4.100 |
| Evans CA 5 / 9682 | 252 | 536 | 252 | 6.768 / 7.043 / 21.734 / 4.123 |
| WP-7 / 1372 | 1,122 | 1,568 | 1,122 | 6.236 / 6.210 / 19.533 / 3.645 |
| DH-15 / 2350 | 202 | 598 | 202 | 6.314 / 6.336 / 18.742 / 3.180 |
| WP-5 / 1084 | 10 | 130 | 10 | 5.430 / 5.519 / 17.574 / 3.185 |
| WP-8 / 2350 | 170 | 362 | 170 | 5.612 / 5.708 / 18.023 / 3.749 |

These are historical values, **not current production targets**. Authority stayed
at 1,817 bins; 1,539 duplicate projection bins were removed from current coverage.
Sixteen superseded rows remain historical; six explicitly shared replacements,
six command records and twelve approval/execution audits were appended. All
**26 protected group hashes are identical before/after**: no physical ledger,
movement, receipt history or treatment application was rewritten. All six commands
committed, independently verified and replayed idempotently. Strict readiness
changed from six genuine treatment blockers to **PASS: 1,007 schema objects,
438 identities, zero treatment blockers**, with inventory and topology passing.

Evidence: [machine-readable runs, hashes, timeline and rehearsal](pr275-final-integration-evidence-2026-10-08.json)
and [combined-only integration probes](pr275-combined-integration-probes.patch).
The probe patch is for the combined candidate, not a second implementation in #275.
It uses zero-context hunks (`git apply --unidiff-zero`); reverse-checking it against
the tested combined tree passed. Its base is #275 plus the reviewed #271/#272 heads.
GitHub Actions are reported separately on the final pushed head; local results do
not imply server-side checks or branch protection passed.

Full protected-table hashes and sealed membership manifests are intentionally
expensive. Approval and execution take global SHARE ROW EXCLUSIVE locks with
NOWAIT. NOWAIT prevents the repair waiting behind writers; it does **not** prevent
later writers from waiting while the repair holds locks. These commands must not
run during ordinary receiving. Even unrelated committed activity can stale the
global fingerprint. Retain a bounded authorized maintenance window, external
command timeout/cancellation and rollback-on-failure; do not reduce evidence to
make it faster. Local measurements are not a production SLA. Data growth, audit
payload growth and production load remain release-window sizing risks.
Observed execution held the maintenance transaction for roughly 18–23 seconds;
this is unacceptable on an ordinary receiving request path. The largest repair
audit payload was 1,578,179 bytes; all six approval/execution pairs totaled
9,480,230 bytes. Production sizing and retention must account for this growth.

The PostgreSQL model warning is compared with a separately built checkout of
current main. No entity/model/migration file is changed by #275; no migration is
added to hide the provider-specific baseline. The combined candidate applies only
#271's three reviewed additive migrations to the disposable restore.

No UI, QC Station, hardware or installer code changed; no browser, onsite hardware
or MSI certification is claimed. No unrelated full application suite is required.
Remaining gates include current-production evidence, owner review, a future
authorized recovery point and production maintenance sizing. Post-acknowledgement
reconstruction needs a separate reviewed integration of custody protection,
locking, snapshot/verification and writer capability; changing a flag is insufficient.

## Production boundary

No production inventory, authoritative ledger or historical record was modified.
No production reconstruction, backup operation or application deployment occurred.
No PR was merged into main. No foundational business rule changed. All synthetic
transactions, migrations and reconstruction executions were confined to disposable
localhost PostgreSQL databases. This report requests owner review, not release
or production repair authorization.
