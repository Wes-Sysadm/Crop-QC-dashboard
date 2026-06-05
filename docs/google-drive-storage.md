# Google Drive Storage

MVP 1 stores photo binaries outside the database. Postgres stores metadata only.

## Target Provider

- Use a Google Shared Drive, not a user's personal My Drive.
- Store QC photos and attachments in the Shared Drive.
- Store structured metadata and stable file references in Postgres.
- Keep the file storage boundary provider-based so local development can keep using `LocalFileStorageService`.
- Configure `FileStorage__Provider=GoogleDrive` on Render to enable real Google Drive uploads.
- Google Shared Drive URL: `https://drive.google.com/drive/folders/0ADHRTHdG9u98Uk9PVA?dmr=1&ec=wgc-drive-%5Bmodule%5D-goto`
- Shared Drive / root folder ID: `0ADHRTHdG9u98Uk9PVA`.
- Service accounts do not have their own Drive storage quota. A normal My Drive shared folder is not enough for service account uploads; the target must be a Google Shared Drive folder.

## Folder Structure

Under the provided root folder, the app creates or reuses this path:

```text
Photos/
  {CropYear}/
    {Warehouse}/
      Receipt-{ReceiptId}/
        BinTruck/
        TopOfTruck/
        Hectre/
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
GoogleDrive__UseSharedDrive=true
GoogleDrive__RootFolderId=0ADHRTHdG9u98Uk9PVA
GoogleDrive__SharedDriveId=0ADHRTHdG9u98Uk9PVA
GoogleDrive__ServiceAccountJson=<service account JSON>
GoogleDrive__ApplicationName=Crop QC Dashboard
GoogleDrive__BaseFolderName=Photos
```

For local testing, use either `GoogleDrive__ServiceAccountJson` or `GoogleDrive__ServiceAccountJsonPath`. Do not commit service account JSON files.

## Google Setup

1. Enable the Google Drive API in the Google Cloud project.
2. Create a service account for the Crop QC Dashboard.
3. Add the service account email from the JSON to the Google Shared Drive / root folder `0ADHRTHdG9u98Uk9PVA`.
4. Use Content Manager or Manager access so the app can create folders and upload files.
5. Store the service account JSON in Render as `GoogleDrive__ServiceAccountJson`.

If uploads fail, verify the Drive API is enabled, the JSON is valid, `GoogleDrive__UseSharedDrive=true`, `GoogleDrive__RootFolderId` and `GoogleDrive__SharedDriveId` are set to `0ADHRTHdG9u98Uk9PVA`, and the service account has Content Manager or Manager access. The error `Service Accounts do not have storage quota` means the upload is not being treated as a Shared Drive upload target.

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
- Upload the file binary to the configured Shared Drive using Shared Drive API options such as `SupportsAllDrives`, `IncludeItemsFromAllDrives`, and the configured shared drive ID for folder searches.
- Return a storage reference with Drive ID, file ID, folder ID, file name, size, content type, and web link.
- Avoid storing image binary in Postgres.
- Audit create/delete/void actions through the existing audit boundary.

Local development and automated tests should continue to use the local storage provider unless a Google integration test environment is explicitly configured.
