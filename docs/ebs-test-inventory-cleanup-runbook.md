# EBS test inventory cleanup

This package removes room-ledger test inventory at EBS outside the existing
Evans 7 room. It does not create Bins Runs, transfers, receipts, Actual Runs, or
other historical operational records.

It must not be run in production without a current verified backup and explicit
production authorization for this exact cleanup.

## Protection boundary

The scripts resolve exactly one EBS Evans 7 room from persisted room identity
fields (`Code`, `Name`, `CropQcRoomName`, `CompuTechRoomCode`, or
`DisplayName`). Variety is not part of the protection decision.

The apply transaction captures complete JSON row snapshots for the Evans 7
room, its receipts, ledger adjustments, room depletions, Bins Run entries,
transfers, and related grower lots. It also captures all WP room-ledger rows.
Before commit it compares every captured row and rolls back if any value, row,
or relationship changed.

## Required sequence

1. Keep application autodeploy disabled.
2. Run `preflight-ebs-test-inventory-cleanup.sql` read-only and retain its
   candidate and blocker output.
3. Capture and independently verify the standard production backup.
4. Restore that backup to clearly disposable PostgreSQL.
5. Run preflight, apply, and verify against the restored copy.
6. Confirm the final EBS room-ledger inventory contains only Evans 7 and that
   Evans 7 and WP guard counts/hashes did not change.
7. Obtain explicit production authorization.
8. Run production preflight again.
9. Apply with both required psql variables:

   `psql --set=cleanup_authorization=REMOVE_NON_EVANS7_EBS_TEST_INVENTORY --set=operator_email=<authorized-admin> --file scripts/postgresql/apply-ebs-test-inventory-cleanup.sql`

10. Run the read-only verification script and retain its output.

## Fail-closed behavior

The apply stops without changing data when:

- EBS or Evans 7 cannot be resolved uniquely;
- the authorization phrase or active operator is invalid;
- a candidate ledger row is linked to a Bins Run, transfer, Actual Run,
  revision, or room depletion;
- any Evans 7 row changes;
- any WP ledger row changes; or
- any non-Evans 7 EBS ledger row remains.

Rows with operational relationships are intentionally not guessed or deleted.
They require separate review and authorization. The cleanup is idempotent when
no candidate ledger rows remain.

## Rollback

Any error before commit rolls back the entire cleanup. After a successful
commit, use the verified pre-cleanup database backup for restoration; do not
invent compensating Bins Runs or negative true-ups.
