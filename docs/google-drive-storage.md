# Google Drive Storage

MVP 1 stores photo binaries outside the database. Postgres stores metadata only.

## Target Provider

- Use a Google Shared Drive, not a user's personal My Drive.
- Store QC photos and attachments in the Shared Drive.
- Store structured metadata and stable file references in Postgres.
- Keep the file storage boundary provider-based so local development can keep using `LocalFileStorageService`.
- Configure `FileStorage__Provider=GoogleDrive` on Render to enable real Google Drive uploads.
- Provided QC files root folder ID: `1pcVsEpDVdYpDrTphXwsLkhuA8D-FH79I`.

## Folder Structure

Under the provided root folder, the app creates or reuses this path:

```text
Photos/
  {CropYear}/
    {Warehouse}/
      Receipt-{ReceiptId}/
        BinTruck/
        SampleBeforeCutting/
        CutFruit/
        FruitAfterStarch/
        Other/
```

Example:

```text
Photos/2026/WP/Receipt-12345/BinTruck/
```

Folder creation is idempotent. The app searches for an existing folder by name under the expected parent folder before creating a missing folder.

## Configuration

Render environment variables:

```text
FileStorage__Provider=GoogleDrive
GoogleDrive__RootFolderId=1pcVsEpDVdYpDrTphXwsLkhuA8D-FH79I
GoogleDrive__ServiceAccountJson=<service account JSON>
GoogleDrive__ApplicationName=Crop QC Dashboard
GoogleDrive__BaseFolderName=Photos
```

For local testing, use either `GoogleDrive__ServiceAccountJson` or `GoogleDrive__ServiceAccountJsonPath`. Do not commit service account JSON files.

## Google Setup

1. Enable the Google Drive API in the Google Cloud project.
2. Create a service account for the Crop QC Dashboard.
3. Grant the service account access to the provided root folder `1pcVsEpDVdYpDrTphXwsLkhuA8D-FH79I`.
4. Use Editor or Content Manager equivalent access so the app can create folders and upload files.
5. Store the service account JSON in Render as `GoogleDrive__ServiceAccountJson`.

If uploads fail, verify the Drive API is enabled, the JSON is valid, and the service account has access to the root folder.

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
- `CapturedAt` / `UploadedAt`
- `CapturedByUserId` when the logged-in user is available.

Current model fields still include SharePoint-oriented names (`SharePointDriveId`, `SharePointItemId`) because the first MVP schema was built for SharePoint/OneDrive. Google Drive uploads also populate provider-neutral fields (`StorageProvider`, `DriveId`, `FileId`, `FolderId`) and keep the legacy fields populated for compatibility.

## Retention

Photos and attachments in the target Google Shared Drive must be retained for at least 3 crop years after the current crop year. The dashboard configuration value `PhotoRetentionCropYearsAfterCurrent` defaults to `3`, but it is currently a planning value only.

No automatic Drive purge, cleanup, or deletion job is enabled. Admin-reviewed archive/delete workflow is future work and must be built before any automated retention action is allowed.

## Implementation Notes

- `GoogleDriveStorageService` implements `IFileStorageService`.
- It resolves or creates the crop year / warehouse / receipt / photo type folders.
- Upload the file binary to the configured Shared Drive.
- Return a storage reference with Drive ID, file ID, folder ID, file name, size, content type, and web link.
- Avoid storing image binary in Postgres.
- Audit create/delete/void actions through the existing audit boundary.

Local development and automated tests should continue to use the local storage provider unless a Google integration test environment is explicitly configured.
