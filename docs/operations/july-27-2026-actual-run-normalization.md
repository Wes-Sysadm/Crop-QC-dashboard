# July 27, 2026 Actual Run historical normalization

This package is limited to the reviewed July 27, 2026 WP Bartlett Bins Run evidence. It does not create, delete, reverse, or recalculate inventory movements. It links the existing Bins Run entries and their existing depletion adjustments to one historical Actual Run, creates revision 1 and a historical reconstructed benchmark with source snapshots, and writes one correction audit.

The command is a dry run unless `--apply` is supplied. Any preflight issue is a hard stop. Do not change the constants or weaken a refusal during operations.

## Reviewed target

- Pacific physical-run time: July 27, 2026 at 10:11 PM (UTC `2026-07-28T05:11:00Z`)
- facility and room: WP / Room 4 (`WarehouseId=4`, `RoomId=1`)
- historical operator: Alexis Ledezma (`UserId=8`, `alexis@wp-packing.com`)
- variety: Bartlett (`FruitProfileId=17`, `BART`, Conventional, not organic)
- grower/lot identity: grower number `1084`, lot `1084`
- Bins Run entries: `28`, `29`, `30`
- existing depletion adjustments: `89`, `90`, `91`
- existing source adjustments: `82`, `86`, `84`
- quantities: `64 + 62 + 58 = 184` bins
- deterministic revision operation key: `historical-actualrun-20260727-wp-bart-1084-28-29-30`

Entries 28–30 have the exact same physical `RunAt`, were recorded by Alexis in sequence over 49 seconds, and share facility, room, reporting crop year, fruit profile, production/organic identity, grower number, and lot. The next Bins Run record is entry 31 at 5:31 PM Pacific on July 28, more than 19 hours later, and is already a separate Actual Run.

## Dry-run preflight

Run the built application with the target database connection and no `--apply`:

```text
dotnet CropQc.Web.dll --normalize-july-27-2026-actual-run
```

Require all of the following before considering apply:

- `success: true`
- `applied: false`
- `preflight.state: Ready`
- `preflight.issues: []`
- exactly three `targetLines`, IDs 28–30, totaling 184 bins
- the expected operator, timestamp, facility, room, variety, production type, organic state, grower/lot, receipt, adjustment, and source-adjustment evidence
- recorded `targetFingerprint` and `protectedFingerprint`
- recorded reporting baseline for All, WP, EBS, and the grower/variety/lot grouping hash

`AlreadyApplied` is acceptable only when the result identifies the one deterministic Actual Run, revision, historical reconstructed benchmark, three expectation sources, three linked entries, three linked existing adjustments, one reconstruction marker, and one audit marker. Do not apply again.

## Historical reconstruction semantics and run-62 evidence

This package does not claim that an expectation was calculated before the run. The existing `ConfigurationSnapshotJson` stores a durable `ExpectationBasis=HistoricalReconstruction` marker, the true reconstruction time, the physical `RunAt`, `QcEvidenceCutoff=PhysicalRunAt`, `ConfigurationBasis=CurrentConfigurationAtReconstruction`, and package identifier `July27ActualRunNormalization:2026-07-27`. No schema change is required. Normal contemporaneous Run Expectations do not receive this marker.

Source identity is taken from the authoritative reporting snapshots when present. Entries 28–30 therefore retain crop year `2026`, reporting `FruitProfileId=17`, Bartlett, Conventional, and not organic even where an older physical field is null or less authoritative.

The run-62 restore proves these calculation-setting rows existed before the physical run and have never been updated: Apple pounds/bin `880` (created `2026-07-24T14:41:43.724639Z`), Pear pounds/bin `920` (`2026-07-24T14:41:43.735571Z`), default expected packout `85%` (`2026-07-24T14:41:43.738740Z`), and minimum distribution fruit `10` (`2026-07-24T14:41:43.739289Z`). Each has `UpdatedAt=NULL`; no RunProjection-setting update audit exists. The current calculation also snapshots the code constants 40 lb/box and cull shares 35% peeler, 35% juice, and 30% waste. Thus the database-backed configured inputs are historically reconstructable for this run, but the calculation itself is truthfully a later reconstruction with current reviewed code, not an original pre-run forecast.

QC evidence is bounded in the database query before materialization by `QcSample.SampleTakenAt <= 2026-07-28T05:11:00Z`. For the exact Room 4 / crop 2026 / profile 17 / lot 1084 identity, sample `108` at `2026-07-27T22:25:51.056545Z` with 20 readings is the latest eligible evidence and is selected for each of the three sources. Later samples are excluded.

## Apply on an authorized database

Apply requires a current successful, verified, retained, unpruned backup whose lease was released. Production additionally requires `--confirm-production`. Use the exact fingerprints emitted by the immediately preceding reviewed dry run:

```text
dotnet CropQc.Web.dll --normalize-july-27-2026-actual-run --apply --confirm-production --backup-run-id=<verified-run-id> --requested-by=<active-admin-email> --reason=<approved-correction-reason> --expected-target-fingerprint=<dry-run-target-sha256> --expected-protected-fingerprint=<dry-run-protected-sha256> --authorization-token=APPLY_REVIEWED_JULY_27_2026_ACTUAL_RUN_NORMALIZATION
```

For the disposable copy restored from reviewed backup run 62, the dump necessarily captures its own `BackupRunRecords` row while the backup is still `Running`; package upload, read-back verification, retention, and lease release happen after the database dump is created. The externally verified package is `cropqc-production-predeployment-20260811-035656.zip`, 1,889,970 bytes, SHA-256 `af54589c20c5921681a00f9e01cad801907673fc4bc6f42bfb6d8b81e03603ba`. In an explicitly non-production application environment only, attest that exact package for rehearsal with:

```text
--confirm-disposable-restore --backup-run-id=62 --verified-backup-package-sha256=af54589c20c5921681a00f9e01cad801907673fc4bc6f42bfb6d8b81e03603ba
```

This restored-copy exception is hard-coded to run 62 and its reviewed package hash and is rejected in production. The reason, target/protected fingerprints, administrator identity, and authorization token still apply. Do not update the restored database's backup row to simulate later backup stages.

The apply transaction performs only these writes:

1. creates one active historical `ActualRun` at the original physical time with Alexis as the historical creator/operator and WP as the historical facility;
2. creates one current create revision with the deterministic operation key;
3. changes entries 28–30 from unlinked `Legacy` reporting rows to linked `Depletion` rows without changing their quantities or operational snapshots;
4. links existing depletion adjustments 89–91 to that revision without changing their quantities, timestamps, or inventory fields;
5. creates one historical reconstructed 184-bin benchmark and exactly three sources for entries 28–30, with the durable marker and physical-run QC cutoff;
6. creates one audit marker whose user is the correction administrator and whose before/after data separately identifies Alexis as the historical operator.

It does not create a RoomInventoryAdjustment or BinsRunEntry, alter `__EFMigrationsHistory`, link a projection, create packout rows, or change a receipt/QC record.

## Required verifier

Require a successful result with `applied: true` and `preflight.state: AlreadyApplied`. Then rerun the command without `--apply` and require `AlreadyApplied` with `applied: false`.

Compare the before/after output:

- the protected fingerprint is identical;
- All/WP/EBS totals are identical;
- the grower/variety/lot reporting grouping hash and group count are identical;
- RoomInventoryAdjustments and BinsRunEntries counts are identical;
- entry quantities remain 64, 62, and 58;
- adjustment changes remain -64, -62, and -58;
- exactly one Actual Run, one revision, one expectation, three expectation sources, and one correction audit were added;
- all three source snapshots are crop year 2026 / fruit profile 17 and use only QC sample 108 at or before the physical `RunAt`;
- the persisted reconstruction marker matches the July 27 package, and the Actual Run page labels it as a reconstructed benchmark rather than a frozen pre-run expectation;
- no PackoutRun was added;
- inventory readiness is unchanged.

If the command fails, it rolls the transaction back. Confirm the target remains `Ready` with no partial Actual Run, revision, expectation, linkage, or audit before investigating. Never repair a partial/conflicting state manually.

For the mandatory combined run-62 rehearsal, start from one brand-new restore, capture the baseline, dry-run/apply July 27 first, then obtain a fresh July 28 dry-run from that post-July-27 state and apply July 28. Prove the two physical runs, revisions, expectations, source sets, and package markers remain distinct. Rerun both commands and require `AlreadyApplied`. Expectation ID `13` seen in earlier isolated rehearsals was reused only because those rehearsals used separate databases whose sequences began from the same run-62 state; it never represented one shared expectation across the two historical days.
