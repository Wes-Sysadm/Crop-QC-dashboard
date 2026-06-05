# Admin Data Cleanup And Crop Years

## Crop Year Convention

Crop QC uses the starting-year convention for crop years.

- Default start: August 1
- Default end: July 31 of the following calendar year
- Example: CropYear `2026` runs from `2026-08-01` through `2027-07-31`

The defaults can be adjusted with configuration:

```text
CropYear__DefaultStartMonth=8
CropYear__DefaultStartDay=1
CropYear__DefaultEndMonth=7
CropYear__DefaultEndDay=31
```

Because seasons may start as early as May and run as late as December of the following calendar year, receipt entry requires explicit crop-year confirmation when the entered received date does not match the selected crop year candidates.

## Receipt Browsing

`/Receipts` defaults to the current crop year. Users can switch crop year from the filter. Admin and Manager users can select All Crop Years for cleanup or review.

Receipt rows show crop year, receipt ID, received date, warehouse, room, grower, lot, variety, sample count, QC status, and last updated time.

## Receipt Sample Review

`/Receipts/{receiptId}` shows all non-deleted QC samples tied to the receipt. Each sample row shows:

- sample type
- status
- completed fruit count
- average pressure
- starch/photo/email status
- readiness
- sample actions

Users who can view the receipt can open linked samples and use `Open in QC Station`. Admin users also see `Delete Sample`.

## Admin Sample Delete

Admin sample delete is a soft delete. It marks the sample as deleted and records:

- who deleted it
- when it was deleted
- the reason
- sample and receipt context in audit logs

Soft delete hides the sample from normal receipt/sample/station workflows. It does not delete the receipt and does not delete Google Drive photos. Photo metadata remains associated with the deleted sample for audit/history.

## Admin Data Cleanup

`/Admin/DataCleanup` is Admin-only. It previews selected test data before cleanup and requires typing:

```text
DELETE TEST DATA
```

Filters include crop year, All Crop Years, date range, warehouse, sample type, receipt ID, emailed samples, deleted samples, and photo metadata.

Cleanup modes:

- Soft cleanup: marks selected samples deleted. Recommended.
- Hard purge test data: permanently deletes selected sample records, fruit rows, selected photo metadata, and email logs. Google Drive files are not automatically deleted.

The cleanup page shows environment and database provider. Production and All Crop Years selections display stronger warnings. Render Postgres backups are not a replacement for export/backup before production cleanup.

## Photo Retention

No automatic photo deletion runs. Photos are retained under the existing retention policy and Google Drive cleanup remains future Admin-reviewed archive/delete work.
