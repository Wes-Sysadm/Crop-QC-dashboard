-- Read-only backup/lease/status evidence; never starts a backup.
SELECT current_timestamp AS observed_at,
(SELECT json_agg(r ORDER BY "Id" DESC) FROM
(SELECT "Id","BackupType","Status","StartedAt","CompletedAt","DeployedCommit","PackageFileName","PackageStorageKey","ManifestStorageKey","FileSizeBytes","Sha256","VerifiedAt","RetentionProcessedAt","LeaseReleasedAt","PrunedAt","CurrentStage","HeartbeatAt","FrozenObjectCount","ObjectsCompleted","FailureStage","ErrorSummary"
FROM "BackupRunRecords" ORDER BY "Id" DESC LIMIT 6) r) AS backups,
(SELECT json_agg(l) FROM "BackupOperationLeases" l) AS leases,
(SELECT json_agg(g ORDER BY "PacificDate" DESC) FROM (SELECT * FROM "BackupNightlyRunGuards" ORDER BY "PacificDate" DESC LIMIT 3) g) AS nightly_guards,
(SELECT json_agg(x) FROM (SELECT "NotificationType","Status",count(*) FROM "BackupNotificationRecords" WHERE "BackupRunId">=185 GROUP BY "NotificationType","Status") x) AS notifications,
(SELECT json_agg(x ORDER BY "MigrationId") FROM (SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId">='20261001') x) AS migrations,
to_regclass('public."ReceiptCustodyAcknowledgments"')::text AS custody_table;
