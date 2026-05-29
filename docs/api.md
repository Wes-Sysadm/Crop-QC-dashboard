# MVP 1 API

The MVP 1 API is a Receiving/QC foundation for the future web dashboard and Windows QC Station. It does not implement UI, storage inventory, Mexico qualification, room controller imports, packout imports, pool closing imports, or analytics.

## Setup

- API project: `src/CropQc.Api`.
- DbContext: `CropQcDbContext`.
- Database provider: configurable `SqlServer` or `PostgreSql`. SQL Server remains the default for local development; Render Postgres is the target production provider.
- OpenAPI is available in development at `/openapi/v1.json`.
- Authentication is still a placeholder; endpoint shapes are designed for future authenticated users.

## Master Data

- `GET /api/master-data/warehouses`
- `GET /api/master-data/warehouses/{warehouseId}/rooms`
- `GET /api/master-data/fruit-profiles`
- `GET /api/master-data/grades`
- `GET /api/master-data/defect-types`
- `GET /api/master-data/sample-types`
- `GET /api/master-data/starch-scale-values`
- `GET /api/master-data/fruit-size-thresholds?fruitType=Apple`
- `GET /api/master-data/fruit-size-thresholds?fruitType=Pear`

## Receipts

- `POST /api/receipts`
- `GET /api/receipts/{id}`
- `GET /api/receipts/search`
- `PUT /api/receipts/{id}/same-day-fields`
- `POST /api/receipts/{id}/needs-review`

Receipt creation requires crop year, received timestamp, original Compu-Tech receipt ID, warehouse, room, fruit profile, grower, lot code, and bin count. The Compu-Tech receipt ID is not changed for duplicate samples.

## QC Samples

- `POST /api/receipts/{receiptId}/samples`
- `GET /api/samples/{id}`
- `GET /api/receipts/{receiptId}/samples`
- `GET /api/warehouses/{warehouseId}/samples/today`
- `PATCH /api/samples/{id}/statuses`

Duplicate receiving samples are allowed. The next `SampleSequenceNumber` is assigned and display IDs use `12345`, `12345(2)`, `12345(3)`, etc. Duplicate samples are marked `Needs Review`.

## Fruit Readings and Defects

- `PUT /api/samples/{sampleId}/fruit-readings/{rowNumber}`
- `POST /api/fruit-readings/{readingId}/defects`
- `DELETE /api/fruit-readings/{readingId}/defects/{defectId}`

Rows are limited to 1 through 25. A completed row requires Pressure 1, Pressure 2, weight in grams, and grade. Starch can be saved later. Size category/status is calculated from fruit type and minimum weight thresholds. Below the smallest threshold is `Undersized`.

## Photos

- `POST /api/photos/metadata`

Photo metadata attaches to exactly one parent: receipt or QC sample. Photo binaries are not stored in the database. Local file storage is the current development provider; Google Shared Drive is the target durable file provider for a later PR.

Expected photo types:

- `BinTruck`
- `SampleBeforeCutting`
- `CutFruit`
- `FruitAfterStarch`
- `Other`

## QC Summary

- `GET /api/samples/{sampleId}/summary-readiness`
- `POST /api/receipts/{receiptId}/email-logs`
- `GET /api/receipts/{receiptId}/email-logs`

Readiness requires a receipt, at least one completed fruit row, all completed rows to have required measurement fields, starch on all completed rows, at least one receipt-level `BinTruck` photo, and sample-level `SampleBeforeCutting`, `CutFruit`, and `FruitAfterStarch` photos.

Email log creation is a send/resend placeholder only. It defaults From to `HL@fruitandland.com`, To to `QC@fruitandland.com`, and uses the sample taker as Reply-To when available.

## Audit

Write endpoints call an audit service placeholder for create, edit, delete, send, and resend events. A full audit interceptor is intentionally deferred.
