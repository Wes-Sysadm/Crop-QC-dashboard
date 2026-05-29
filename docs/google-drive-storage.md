# Google Drive Storage Plan

MVP 1 stores photo metadata in the database only. This document defines the target Google Shared Drive direction. It does not implement real Google Drive upload yet.

## Target Provider

- Use a Google Shared Drive, not a user's personal My Drive.
- Store QC photos and attachments in the Shared Drive.
- Store structured metadata and stable file references in Postgres.
- Keep the file storage boundary provider-based so local development can keep using `LocalFileStorageService`.

## Folder Structure

Suggested Shared Drive root:

```text
Crop QC Photos/
  2026/
    WP/
      Receipt-12345/
        BinTruck/
        SampleBeforeCutting/
        CutFruit/
        FruitAfterStarch/
```

The generated path should use:

- Crop year.
- Warehouse code.
- Original receipt ID.
- Photo type.
- Timestamped file name.

## Metadata To Store

Photo metadata should include:

- `StorageProvider = GoogleDrive`
- `DriveId`
- `FileId`
- `FolderId`
- `FileName`
- `ContentType`
- `FileSizeBytes`
- `WebViewLink` or app link.
- `ReceiptId` or `QcSampleId`
- `PhotoType`

Current model fields still use SharePoint-oriented names (`SharePointDriveId`, `SharePointItemId`) because the first MVP schema was built for SharePoint/OneDrive. A future schema migration should rename or generalize these fields before Google Drive production use.

## Future Implementation Notes

Add a `GoogleDriveStorageService` implementing `IFileStorageService`.

Expected responsibilities:

- Resolve or create the crop year / warehouse / receipt / photo type folders.
- Upload the file binary to the configured Shared Drive.
- Return a storage reference with Drive ID, file ID, folder ID, file name, size, content type, and web link.
- Avoid storing image binary in Postgres.
- Audit create/delete/void actions through the existing audit boundary.

Local development and automated tests should continue to use the local storage provider unless a Google integration test environment is explicitly configured.
