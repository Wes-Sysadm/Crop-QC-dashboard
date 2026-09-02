# Crop QC change-scoped testing and data-integrity standard

This standard applies to all Crop QC development, review, rehearsal, and production-release work unless the user explicitly requests a broader validation scope for a specific change.

The default rule is simple:

> Test the blast radius of the change, prove the affected data is correct, and preserve the historical record. Do not recertify unrelated parts of Crop QC.

## 1. Determine the blast radius first

Before choosing tests, identify:

- the feature or defect being changed;
- the records the change can create, update, reverse, supersede, or read differently;
- the workflows that directly read or write those same records;
- shared services, invariants, queries, or schema objects materially changed by the work;
- the historical evidence that must remain immutable;
- any rollback or compatibility risk created by the change.

Write this affected-area list before expanding validation.

Do not broaden testing merely because another test suite, route matrix, browser harness, benchmark, or production restore is available.

If investigation discovers a real dependency on another area, add that area to the blast radius and state why.

## 2. Data correctness is the primary validation gate

For any change that can alter operational data, a successful request or HTTP 200 is not enough. Validation must prove that the resulting data represents the business event correctly.

Capture relevant before-state and after-state evidence and verify, as applicable:

- total quantities reconcile;
- no bins, fruit, samples, records, or allocations are lost;
- nothing is duplicated or deducted twice;
- no unintended restoration or re-creation occurs;
- Grower/Grower Lot identity is correct;
- Fruit Profile, variety, production type, and Organic/Conventional identity are internally consistent;
- facility, room, custody, date/time, and reporting attribution are correct;
- treatment quantity, treatment identity, and treatment provenance remain consistent;
- every operational child has the expected parent relationship;
- the current active revision/state is unambiguous;
- current reports use the authoritative current state;
- records outside the intended change are unchanged.

For quantity-affecting corrections, explicitly reconcile the math. For metadata-only corrections, explicitly prove quantity and inventory records are unchanged.

## 3. Preserve historical information

Do not make current data appear correct by destroying the evidence of what originally happened.

Preserve historical operational evidence where applicable, including:

- original Receipts;
- prior Actual Run revisions;
- prior Bins Run entries;
- previous inventory adjustments;
- transfers and reversals;
- treatment segments, applications, and movements;
- Run Expectations;
- packout evidence;
- correction records;
- AuditLogs;
- original actors and timestamps;
- operation keys;
- before/after snapshots.

Corrections should normally use an established revision, compensating transaction, dedicated correction record, reversal/replacement workflow, or audited supersession relationship instead of overwriting or deleting history.

The application must distinguish:

- **Historical evidence:** what the system recorded at the time; and
- **Current authoritative state:** what the corrected operational record is now.

Both must remain available and reconcilable.

## 4. Correction invariant

After a correction, the current operational state must be the same state the application would have produced if the corrected information had been entered correctly in the first place.

At the same time, the system must retain enough evidence to answer:

- what was originally recorded;
- what changed;
- why it changed;
- who changed it;
- when it changed;
- which revision, reversal, correction, or supersession made the new state authoritative.

## 5. Focused regression testing

The default regression set contains only tests needed for the affected area.

Include, where relevant:

- the exact reported defect or requested behavior;
- the primary success path;
- meaningful boundary cases for the changed logic;
- stale/concurrent submission protection;
- authorization and antiforgery for new protected writes;
- idempotency for retryable operations;
- direct data-integrity and reconciliation assertions;
- preservation of historical evidence;
- the directly affected report/query output.

Do not automatically add tests for unrelated Photos, Field Samples, Receiving, Transfers, reports, email, Admin pages, memory, or browser routes unless the implementation actually reaches those areas.

## 6. Full-suite policy

A complete application test suite is **not** the default requirement for every change.

Use the focused affected-area suite throughout development.

Run broader or full-suite validation only when there is a concrete reason, such as:

- a shared foundational service changed and has broad consumers;
- a shared inventory, identity, authorization, time, persistence, or query abstraction changed;
- the schema/model change materially affects broad application behavior;
- focused tests expose a wider regression;
- the change genuinely spans multiple application areas;
- the user explicitly asks for broader certification.

When a full suite is justified, it normally runs once near final review/merge rather than repeatedly during development and release preparation.

The completion report must state the reason broad validation was required.

## 7. Browser and route testing

Test only materially changed user-facing routes and the immediately dependent routes needed to prove the workflow.

For a UI change, test the changed screen, its write/read-back behavior, and any directly affected display/report route. Do not run a complete authenticated route catalog by default.

Responsive checks are required only when the change modifies layout, controls, navigation, or responsive behavior.

## 8. PostgreSQL and restored-production testing

Use PostgreSQL integration or a restored production copy when it materially increases confidence in the changed area, especially for:

- migrations and compatibility scripts;
- data corrections;
- production-specific data shapes;
- inventory calculations;
- historical-data compatibility;
- provider-specific queries;
- concurrency/locking behavior that cannot be proven with the normal test provider.

When used, exercise the affected workflows only.

Do not turn every restored-production rehearsal into whole-application certification.

A restored-production test must remain disposable and must not replace the normal production backup gate for an authorized release.

## 9. Schema changes

For a schema change, validate the migration/compatibility blast radius:

- migration applies from the supported prior state;
- reviewed compatibility/preflight behavior works where the repository uses it;
- expected schema objects exist;
- incompatible partial state fails closed when applicable;
- transaction rollback leaves no partial schema when applicable;
- EF has no pending model changes;
- the prior application can run against the additive schema when rollback compatibility is part of the release risk.

Do not require unrelated feature workflows merely because a migration exists.

## 10. Production release testing

Production release validation remains safety-focused but change-scoped.

Always preserve the mandatory production gates that protect availability and data:

- exact candidate SHA;
- fresh verified production backup before the first production mutation;
- reviewed schema/data preflight where applicable;
- release-specific readiness/invariants;
- health and database health;
- affected authenticated route/workflow smoke;
- exact affected data verification after deployment/correction;
- known-good application rollback path when the release creates rollback risk.

Do not require unrelated application routes, unrelated feature flags, memory benchmarks, or whole-product browser matrices unless the release actually touches those areas or evidence shows a wider problem.

Synthetic production mutations remain prohibited; mutation proof belongs in disposable/restored environments.

## 11. Examples

### Actual Run correction

Test:

- Actual Run revision/correction path;
- affected inventory deductions/restorations;
- treatment lineage when the source changes;
- grower/lot/bin/date reporting affected by the run;
- audit/revision history;
- stale write/idempotency as applicable.

Do not automatically test Photos, Field Samples, Receiving, unrelated Transfers, email, or unrelated Admin screens.

### Receipt correction

Test the Receipt and the inventory/provenance paths the correction can change. Preserve original Receipt/correction history. Do not test unrelated QC/photo/report workflows unless they consume the changed field.

### Photo change

Test photo upload/storage/presentation/output paths affected by the change. Do not run inventory/run/transfer certification.

### Reporting-only change

Test the authoritative source query, filters/grouping, affected report output, and proof that the report is read-only. Do not retest operational mutation workflows unless the report change alters shared operational queries.

## 12. Required report for every change

Report:

### Changed area
- what was modified;
- what user/business behavior changed.

### Directly affected data
- which tables/entities/workflows can change;
- which historical records must remain unchanged.

### Data integrity
- before/after reconciliation;
- authoritative current state;
- historical evidence preserved;
- unintended writes: none, or list and justify them.

### Focused tests
- exact affected tests run;
- pass/fail totals;
- database/browser/provider-specific checks actually run.

### Outside scope
- unrelated areas intentionally not retested.

### Broader testing, if any
- the exact shared dependency or risk that justified expanding the blast radius.

The goal is high confidence in the changed workflow and its data without adding unnecessary development or release time.