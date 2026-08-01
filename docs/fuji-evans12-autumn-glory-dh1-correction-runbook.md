# Fuji Evans 12 and Autumn Glory DH Room 1 correction

This package is a one-time, operator-run PostgreSQL correction. It is not a migration, startup task, or automatic repair.

## Reviewed production evidence

- EBS warehouse `Id=1`; Evans 12 room `Id=22`, code `EVANS-12`, name `Evans Street 12`, Crop QC name `Evans-12`, CompuTech code `evanca12`, display name `Evans-12`.
- Fuji fruit profile `Id=1`, variety code `FUJI`, conventional apple.
- The reviewed physical Fuji ledger is adjustment IDs `35,36,37,54,55,66,67,68`, linked to Bins Run IDs `1,2,13,14,15`. Starting quantities `118 + 819 + 263` and deductions `56 + 152 + 62 + 667 + 263` net to exactly zero. No production mutation is authorized or needed for Fuji.
- The false 729-bin display was caused by chained legacy deductions whose persisted crop year is null. One-level source resolution split `+62/-62` and `+667/-667` into separate crop-year groups. The shared ledger read path now canonicalizes the fragments when exactly one persisted crop year exists for the full room/Grower Lot/lot/fruit-profile identity.
- DH warehouse `Id=2`; Room 1 `Id=33`, code `DH-1`, name `Room 1`, CompuTech code `DHCA0101`.
- Autumn Glory fruit profile `Id=22`, variety code `ATGL`, conventional apple.
- Receipt `Id=37` is a one-bin, crop-year-2025 receipt already soft-deleted with reason `Fake`. Its preserved `ReceiptAdd` adjustment `Id=52` still contributes `+1`. It has no Bins Run, transfer, Room Depletion, Actual Run, or reversal parent. The correction changes only adjustment `52` from `+1/new 1` to `0/new 0`, preserves the receipt and all history, and records one audit marker.

During the read-only inspection, valid Evans 7 Gala was initially 388 bins. A legitimate user then created Actual Run `5` (`First pass Wildfire run`) with Bins Run entries `35` and `36`, depleting `260 + 128` bins at `2026-08-01 01:07 UTC`. The protected value therefore legitimately changed to zero through actual operations. The package never restores or edits it; immediate-preflight fingerprints protect whatever reviewed operational state is current.

## Mandatory backup and restored-copy rehearsal

Before deployment or any production write:

1. Record the currently deployed application SHA and PostgreSQL environment.
2. Run `dotnet CropQc.Web.dll --run-backup=predeployment` in the production runtime.
3. Confirm the uploaded package in the restricted Google Drive destination, read it back, and verify filename, UTC time, size, SHA-256, manifest, archive, and database dump.
4. Restore that exact package into a clearly disposable localhost PostgreSQL database with `scripts/verify-backup-restore.ps1`.
5. Run preflight, first apply, verify, second apply, and verify against the restored copy. Confirm the first apply updates one row, the second updates zero, one audit exists, and the protected-state hashes match.
6. Stop if any fingerprint, backup, restore, or verification gate fails.

Never expose connection strings, OAuth material, or backup credentials in logs or the PR.

## Commands

Run preflight immediately before apply:

```powershell
psql -X -v ON_ERROR_STOP=1 -f scripts/postgresql/preflight-fuji-evans12-autumn-glory-dh1-correction.sql
```

Record the two reported guard fingerprints. Apply with the exact immediate-preflight values:

```powershell
psql -X -v ON_ERROR_STOP=1 `
  -v correction_authorization=APPLY_FUJI_EVANS12_AUTUMN_GLORY_DH1_CORRECTION `
  -v operator_email=wes@fruitandland.com `
  -v expected_gala_fingerprint=<GALA_LEDGER_GUARD fingerprint> `
  -v expected_wp_fingerprint=<WP_LEDGER_GUARD fingerprint> `
  -f scripts/postgresql/apply-fuji-evans12-autumn-glory-dh1-correction.sql
```

Then run:

```powershell
psql -X -v ON_ERROR_STOP=1 -f scripts/postgresql/verify-fuji-evans12-autumn-glory-dh1-correction.sql
```

Run apply and verify a second time. `rows_updated_this_run` must be `0` on the second apply and the audit count must remain one.

## Safety and recovery

The apply uses a serializable transaction, advisory and table locks, exact identities and fingerprints, one exact update, full protected inventory/history snapshots, before/after hashes, and a unique audit marker. Any mismatch raises an exception before commit.

If the transaction fails, PostgreSQL rolls it back. If post-commit verification fails, stop application writes and restore the independently verified predeployment backup. Do not invent a compensating receipt, Bins Run, transfer, Actual Run, or generic balancing entry.
