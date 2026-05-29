# MVP 1 Data Model

This document describes the implemented Entity Framework Core model for MVP 1 Receiving/QC. It intentionally excludes storage inventory, room controller imports, Mexico qualification, packout imports, pool closing imports, and long-term performance analytics.

## EF Core Boundary

- DbContext: `CropQcDbContext`.
- Provider configuration supports `SqlServer` and `PostgreSql`.
- Current checked-in migrations live in `src/CropQc.Data/Migrations` and are SQL Server-oriented.
- Render Postgres is the target production database, but it should get a fresh provider-specific initial migration or migration bundle before production cutover.
- Design-time factory: `CropQcDbContextFactory`.

Runtime configuration:

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionStringName": "CropQc"
}
```

or:

```json
"Database": {
  "Provider": "PostgreSql",
  "ConnectionStringName": "CropQc"
}
```

Environment variable `DATABASE_PROVIDER` overrides the configured provider. `ConnectionStrings__CropQc` supplies the active connection string.

## Security and Configuration

- `Users` stores dashboard/QC Station users, password hash metadata, active status, and password change timestamp.
- `Roles` stores Admin, Manager, QC User, and Viewer.
- `UserRoles` maps users to roles.
- `RolePermissions` supports admin-configurable permissions.
- `PasswordPolicies` stores configurable password policy settings.

Seeded password policy:

- Minimum length: 8.
- Require uppercase: true.
- Require lowercase: true.
- Require number: true.
- Require symbol: true.
- Password expiration days: 365.

## Retention Policy

- Database data is retained indefinitely by default.
- No automatic deletion is enabled for receipts, QC samples, fruit readings, QC fruit defects, photo metadata, audit logs, QC summary email logs, users, roles, warehouses, rooms, varieties, grades, defects, starch scales, size thresholds, or other master data.
- Photos and attachments are retained for at least 3 crop years after the current crop year.
- `PhotoRetentionCropYearsAfterCurrent` defaults to `3`, but it is a planning value only. No automatic photo deletion currently runs.
- Admin-reviewed archive/delete workflow is future work and must be built before any purge behavior is enabled.

## Master Data

- `Warehouses` stores EBS, DH, McDougall, and WP.
- `Rooms` stores admin-editable rooms per warehouse, including room code, max bin capacity, and active status. Room code is unique per warehouse, not globally.
- `FruitProfiles` stores variety name/description, variety code, commodity/fruit type, production type, derived organic flag, and active status. Admins edit `ProductionType`; `IsOrganic` is derived automatically from whether production type is Organic.
- `SampleTypes` stores Receiving Sample, Door Sample, and Line QC Sample.
- `Grades` stores W1, W2, W3, W4, WF, US1, US2, and USF.
- `DefectTypes` stores the MVP 1 defect list. Defect severity is not tracked in MVP 1.
- `StarchScales` stores starch scale definitions, optionally scoped by fruit type or fruit profile.
- `StarchScaleValues` stores values for the seeded 6-point starch scale.
- `FruitSizeConversionThresholds` stores commodity-specific size thresholds. Apple and Pear are seeded, and additional commodities can be added by Admins when thresholds are needed.

## Receiving/QC Transactions

- `Receipts` stores crop year, received timestamp, original Compu-Tech receipt ID, warehouse, room, fruit profile, grower, lot code, bin count, and create/update timestamps. The original Compu-Tech receipt ID remains unchanged.
- `QcSamples` stores receiving samples for a receipt, sample type, workflow statuses, user/station capture metadata, actual sample size, sequence number, and notes. Duplicate samples for the same Compu-Tech receipt use `SampleSequenceNumber`; display formatting is `12345`, `12345(2)`, `12345(3)`, etc.
- `QcFruitReadings` stores up to 25 displayed fruit rows per sample. The actual sample size may be fewer than 25.
- `QcFruitDefects` allows multiple defects per fruit reading.
- `QcPhotos` stores photo metadata and external file references only. Photo binaries are not stored in the database. A photo attaches to either a receipt or a QC sample.
- `QcSummaryEmailLogs` stores send/resend history. Each send or resend creates a separate row and may optionally reference a QC sample.
- `QcStations` stores Windows QC Station registration metadata.
- `OfflineSyncItems` is a placeholder for later offline QC Station sync tracking.
- `AuditLogs` records create, edit, delete, send, import, and export actions.

## Measurement Rules

- Pressure is stored in lbs.
- Weight is stored in grams.
- Completed fruit rows require Pressure 1, Pressure 2, weight, and grade.
- Starch is per fruit and may be missing until completed later.
- Starch is required before QC Summary email can be sent.
- Starch can be missing while a sample is saved, but the QC Summary cannot send until starch is complete for all completed fruit rows and the fruit-after-starch photo exists.
- Pressure readings include optional source fields so later logic can distinguish FTA, Manual, and Manual Override.
- Size conversion weights are minimum thresholds, not closest-match values.
- Fruit should be assigned the largest size category it qualifies for.
- If fruit weight is below the smallest threshold, size status is Undersized.

## Workflow Statuses

`QcSamples.Status` is stored as a string for MVP 1. Intended status values are:

- Data Entry In Progress.
- Starch Pending.
- Photo Pending.
- Ready to Send.
- Sent.
- Voided.

`StarchStatus`, `PhotoStatus`, and `EmailStatus` are also strings for now so app logic can evolve without schema churn during early MVP work.

## Photo Types and Sources

Supported photo types are:

- BinTruck.
- SampleBeforeCutting.
- CutFruit.
- FruitAfterStarch.
- Other.

Bin/truck photos attach to `Receipts`. Sample before cutting, cut fruit, and fruit after starch photos attach to `QcSamples`. Photo source is stored as a string, with intended values such as USB Camera and Manual Upload.

## Relational Constraints

- User email is unique.
- Role name is unique.
- Warehouse code is unique.
- Room code and room name are unique within a warehouse.
- Variety code is unique.
- Sample type name is unique.
- Grade code is unique.
- Defect name is unique.
- Fruit size category is unique per fruit type.
- QC sample sequence is unique per receipt.
- QC fruit row number is unique per sample and constrained to 1 through 25.
- Completed fruit rows require core measurements and grade.
- QC photos must attach to exactly one parent: receipt or QC sample.
- QC photo external drive/item reference is unique.
- QC Summary email logs are indexed by receipt and sample, but not unique, to preserve send/resend history.
- Offline sync local entity tracking is unique per station, entity, and local ID.

## Seed Data

Initial seed data includes:

- Roles: Admin, Manager, QC User, Viewer.
- Warehouses: EBS, DH, McDougall, WP.
- Grades: W1, W2, W3, W4, WF, US1, US2, USF.
- Defects: Bruise, Sunburn, Bitter Pit, Scald, Decay, Puncture, Watercore, Limb Rub, Stem Bowl Crack, Internal Browning, Other.
- Sample types: Receiving Sample, Door Sample, Line QC Sample.
- 6-point starch scale values: 1, 1.2, 1.5, 1.8, 2, 2.5, 3, 3.5, 4, 4.5, 5, 6.
- Fruit profiles and variety codes from MVP 1 requirements, including organic/conventional production type and organic flag.
- Apple and pear size conversion thresholds from MVP 1 requirements.
