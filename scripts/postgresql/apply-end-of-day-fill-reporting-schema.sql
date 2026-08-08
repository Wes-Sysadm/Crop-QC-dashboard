\set ON_ERROR_STOP on
START TRANSACTION;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260807044836_AddEndOfDayFillReporting', 0));
CREATE TEMP TABLE _end_of_day_fill_room_capacity_before ON COMMIT DROP AS
SELECT "Id", "CapacityBins" FROM "Rooms";

DO $precheck$
DECLARE
    target_table_count integer;
    expected_room_count integer;
    wp_candidate_count integer;
    ebs_candidate_count integer;
    unresolved_room_count integer;
    duplicate_room_code_count integer;
BEGIN
    SELECT count(*) INTO target_table_count
    FROM (VALUES
        ('EndOfDayFillReportGroups'),
        ('EndOfDayFillReportRecipients'), ('EndOfDayFillUserGroupAssignments'),
        ('EndOfDayFillReportSends'), ('EndOfDayFillSendReservations')) AS expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NOT NULL;
    IF target_table_count NOT IN (0, 5) THEN
        RAISE EXCEPTION 'Unsupported partial End of Day Fill schema (% of 5 tables). Transaction rolled back.', target_table_count;
    END IF;
    IF (target_table_count = 0 AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='EndOfDayFillReportGroupId'))
       OR (target_table_count = 5 AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='EndOfDayFillReportGroupId'))
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroupRooms')) IS NOT NULL THEN
        RAISE EXCEPTION 'Unsupported or obsolete Room assignment schema. Transaction rolled back.';
    END IF;
    IF (SELECT count(*) FROM "Warehouses" WHERE "IsActive" AND lower(btrim("Code")) IN ('dh','mcdougall','wp','ebs')) <> 4
       OR EXISTS (SELECT lower(btrim("Code")) FROM "Warehouses" WHERE "IsActive" AND lower(btrim("Code")) IN ('dh','mcdougall','wp','ebs') GROUP BY lower(btrim("Code")) HAVING count(*) <> 1) THEN
        RAISE EXCEPTION 'Expected exactly one active DH, McDougall, WP, and EBS warehouse identity. Transaction rolled back.';
    END IF;

    SELECT count(*) INTO duplicate_room_code_count
    FROM (
        SELECT w."Id", lower(btrim(r."Code")) AS normalized_room_code
        FROM "Warehouses" w
        JOIN "Rooms" r ON r."WarehouseId"=w."Id"
        WHERE w."IsActive" AND lower(btrim(w."Code")) IN ('dh','mcdougall','wp','ebs')
        GROUP BY w."Id", lower(btrim(r."Code"))
        HAVING count(*) <> 1
    ) duplicates;
    IF duplicate_room_code_count <> 0 THEN
        RAISE EXCEPTION 'Duplicate normalized Room codes exist in the reviewed DH, McDougall, WP, or EBS warehouse scope. Transaction rolled back.';
    END IF;

    WITH expected(facility, warehouse_code, room_code) AS (
        SELECT 'WP', 'dh', 'DH-' || n FROM generate_series(1, 22) AS n
        UNION ALL SELECT 'WP', 'mcdougall', 'MCD-01'
        UNION ALL SELECT 'WP', 'mcdougall', 'MCD-' || n FROM generate_series(3, 16) AS n
        UNION ALL SELECT 'WP', 'wp', 'WP-' || n FROM generate_series(4, 8) AS n
        UNION ALL SELECT 'EBS', 'ebs', 'LAMB-' || n FROM generate_series(13, 17) AS n
        UNION ALL SELECT 'EBS', 'ebs', 'EVANS-' || n FROM generate_series(1, 12) AS n
        UNION ALL SELECT 'EBS', 'ebs', room_code FROM (VALUES
            ('EVANS-BACKSIDE'), ('EVANS-BKT'), ('EVANS-HALLWAY1'), ('EVANS-HALLWAY2')) special(room_code)
        UNION ALL SELECT 'EBS', 'ebs', 'BM-' || n FROM generate_series(1, 6) AS n
    ), resolved AS (
        SELECT e.facility, e.warehouse_code, e.room_code, count(r."Id") AS match_count
        FROM expected e
        LEFT JOIN "Warehouses" w
          ON w."IsActive" AND lower(btrim(w."Code"))=e.warehouse_code
        LEFT JOIN "Rooms" r
          ON r."WarehouseId"=w."Id" AND r."IsActive"
         AND lower(btrim(r."Code"))=lower(e.room_code)
        GROUP BY e.facility, e.warehouse_code, e.room_code
    )
    SELECT count(*),
           count(*) FILTER (WHERE facility='WP' AND match_count=1),
           count(*) FILTER (WHERE facility='EBS' AND match_count=1),
           count(*) FILTER (WHERE match_count<>1)
    INTO expected_room_count, wp_candidate_count, ebs_candidate_count, unresolved_room_count
    FROM resolved;
    IF expected_room_count <> 69 OR wp_candidate_count <> 42 OR ebs_candidate_count <> 27 OR unresolved_room_count <> 0 THEN
        RAISE EXCEPTION 'Reviewed End of Day Fill Room scope did not resolve exactly. expected=69 wp=% ebs=% missing_or_ambiguous=%. Transaction rolled back.',
            wp_candidate_count, ebs_candidate_count, unresolved_room_count;
    END IF;
END $precheck$;

SELECT to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroups')) IS NOT NULL AS schema_exists \gset
\if :schema_exists
\echo 'End of Day Fill tables already exist; preserving schema and applying only idempotent initial configuration.'
\else
CREATE TABLE "EndOfDayFillReportGroups" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "Name" character varying(150) NOT NULL,
    "Facility" character varying(10) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EndOfDayFillReportGroups" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_EndOfDayFillReportGroups_Facility" CHECK ("Facility" IN ('WP','EBS'))
);
CREATE TABLE "EndOfDayFillReportRecipients" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "EmailAddress" character varying(320) NOT NULL,
    "NormalizedEmailAddress" character varying(320) NOT NULL,
    "IsActive" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByUserId" integer,
    CONSTRAINT "PK_EndOfDayFillReportRecipients" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EndOfDayFillReportRecipients_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);
ALTER TABLE "Rooms" ADD COLUMN "EndOfDayFillReportGroupId" integer;
ALTER TABLE "Rooms" ADD CONSTRAINT "FK_Rooms_EndOfDayFillReportGroups_EndOfDayFillReportGroupId" FOREIGN KEY ("EndOfDayFillReportGroupId") REFERENCES "EndOfDayFillReportGroups" ("Id") ON DELETE SET NULL;
CREATE TABLE "EndOfDayFillReportSends" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
    "ReportGroupId" integer NOT NULL,
    "ReportGroupName" character varying(150) NOT NULL,
    "Facility" character varying(10) NOT NULL,
    "PacificReportDate" date NOT NULL,
    "RevisionNumber" integer NOT NULL,
    "SenderUserId" integer,
    "SenderEmail" character varying(320) NOT NULL,
    "SenderDisplayName" character varying(200) NOT NULL,
    "RecipientsJson" text NOT NULL,
    "PhysicalCountConfirmed" boolean NOT NULL,
    "SnapshotHash" character varying(64) NOT NULL,
    "SnapshotJson" text NOT NULL,
    "SuccessRevisionKey" character varying(200),
    "SuccessSnapshotKey" character varying(250),
    "Subject" character varying(500) NOT NULL,
    "HtmlBody" text NOT NULL,
    "TextBody" text NOT NULL,
    "Status" character varying(25) NOT NULL,
    "FailureReason" character varying(2000),
    "GmailMessageId" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "AttemptedAt" timestamp with time zone NOT NULL,
    "SentAt" timestamp with time zone,
    CONSTRAINT "PK_EndOfDayFillReportSends" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId" FOREIGN KEY ("ReportGroupId") REFERENCES "EndOfDayFillReportGroups" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EndOfDayFillReportSends_Users_SenderUserId" FOREIGN KEY ("SenderUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);
CREATE TABLE "EndOfDayFillUserGroupAssignments" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "UserId" integer NOT NULL,
    "ReportGroupId" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedByUserId" integer,
    CONSTRAINT "PK_EndOfDayFillUserGroupAssignments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EndOfDayFillUserGroupAssignments_EndOfDayFillReportGroups_ReportGroupId" FOREIGN KEY ("ReportGroupId") REFERENCES "EndOfDayFillReportGroups" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_EndOfDayFillUserGroupAssignments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_EndOfDayFillUserGroupAssignments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "EndOfDayFillSendReservations" (
    "ReportGroupId" integer NOT NULL,
    "PacificReportDate" date NOT NULL,
    "RevisionNumber" integer NOT NULL,
    "SnapshotHash" character varying(64) NOT NULL,
    "SendAttemptId" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EndOfDayFillSendReservations" PRIMARY KEY ("ReportGroupId"),
    CONSTRAINT "FK_EndOfDayFillSendReservations_EndOfDayFillReportGroups_ReportGroupId" FOREIGN KEY ("ReportGroupId") REFERENCES "EndOfDayFillReportGroups" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId" FOREIGN KEY ("SendAttemptId") REFERENCES "EndOfDayFillReportSends" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX "IX_EndOfDayFillReportGroups_Name" ON "EndOfDayFillReportGroups" ("Name");
CREATE INDEX "IX_Rooms_EndOfDayFillReportGroupId" ON "Rooms" ("EndOfDayFillReportGroupId");
CREATE UNIQUE INDEX "IX_EndOfDayFillReportRecipients_NormalizedEmailAddress" ON "EndOfDayFillReportRecipients" ("NormalizedEmailAddress");
CREATE INDEX "IX_EndOfDayFillReportRecipients_UpdatedByUserId" ON "EndOfDayFillReportRecipients" ("UpdatedByUserId");
CREATE INDEX "IX_EndOfDayFillReportSends_ReportGroupId_PacificReportDate_Status" ON "EndOfDayFillReportSends" ("ReportGroupId","PacificReportDate","Status");
CREATE INDEX "IX_EndOfDayFillReportSends_SenderUserId" ON "EndOfDayFillReportSends" ("SenderUserId");
CREATE UNIQUE INDEX "IX_EndOfDayFillReportSends_SuccessRevisionKey" ON "EndOfDayFillReportSends" ("SuccessRevisionKey") WHERE "SuccessRevisionKey" IS NOT NULL;
CREATE INDEX "IX_EndOfDayFillReportSends_SuccessSnapshotKey" ON "EndOfDayFillReportSends" ("SuccessSnapshotKey");
CREATE UNIQUE INDEX "IX_EndOfDayFillSendReservations_SendAttemptId" ON "EndOfDayFillSendReservations" ("SendAttemptId");
CREATE INDEX "IX_EndOfDayFillUserGroupAssignments_CreatedByUserId" ON "EndOfDayFillUserGroupAssignments" ("CreatedByUserId");
CREATE INDEX "IX_EndOfDayFillUserGroupAssignments_ReportGroupId" ON "EndOfDayFillUserGroupAssignments" ("ReportGroupId");
CREATE UNIQUE INDEX "IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId" ON "EndOfDayFillUserGroupAssignments" ("UserId","ReportGroupId");
\endif

INSERT INTO "EndOfDayFillReportGroups" ("Name","Facility","IsActive","CreatedAt","UpdatedAt") VALUES
 ('WP End of Day Fill','WP',TRUE,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP),
 ('EBS End of Day Fill','EBS',TRUE,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
ON CONFLICT ("Name") DO NOTHING;
INSERT INTO "EndOfDayFillReportRecipients" ("EmailAddress","NormalizedEmailAddress","IsActive","SortOrder","CreatedAt","UpdatedAt","UpdatedByUserId") VALUES
 ('wes@fruitandland.com','WES@FRUITANDLAND.COM',TRUE,10,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,NULL),
 ('jorge@wp-packing.com','JORGE@WP-PACKING.COM',TRUE,20,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,NULL),
 ('rob@earlbrownandsons.com','ROB@EARLBROWNANDSONS.COM',TRUE,30,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,NULL)
ON CONFLICT ("NormalizedEmailAddress") DO NOTHING;


\if :schema_exists
\echo 'Preserving authoritative Room master-data assignments on repeat apply.'
\else
WITH expected(facility, warehouse_code, room_code) AS (
    SELECT 'WP', 'dh', 'DH-' || n FROM generate_series(1, 22) AS n
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-01'
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-' || n FROM generate_series(3, 16) AS n
    UNION ALL SELECT 'WP', 'wp', 'WP-' || n FROM generate_series(4, 8) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'LAMB-' || n FROM generate_series(13, 17) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'EVANS-' || n FROM generate_series(1, 12) AS n
    UNION ALL SELECT 'EBS', 'ebs', room_code FROM (VALUES
        ('EVANS-BACKSIDE'), ('EVANS-BKT'), ('EVANS-HALLWAY1'), ('EVANS-HALLWAY2')) special(room_code)
    UNION ALL SELECT 'EBS', 'ebs', 'BM-' || n FROM generate_series(1, 6) AS n
)
UPDATE "Rooms" r SET "EndOfDayFillReportGroupId"=g."Id"
FROM "Warehouses" w, "EndOfDayFillReportGroups" g, expected e
WHERE w."Id"=r."WarehouseId" AND r."IsActive" AND w."IsActive"
  AND lower(btrim(w."Code"))=e.warehouse_code
  AND lower(btrim(r."Code"))=lower(e.room_code)
  AND g."Facility"=e.facility
  AND g."Name"=CASE e.facility WHEN 'WP' THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END
  AND r."EndOfDayFillReportGroupId" IS DISTINCT FROM g."Id";
\endif

INSERT INTO "EndOfDayFillUserGroupAssignments" ("UserId","ReportGroupId","CreatedAt","CreatedByUserId")
SELECT u."Id", g."Id", CURRENT_TIMESTAMP, NULL
FROM "Users" u CROSS JOIN "EndOfDayFillReportGroups" g
WHERE (lower(btrim(u."Email"))='jorge@wp-packing.com' AND g."Name"='WP End of Day Fill')
   OR (lower(btrim(u."Email"))='rob@earlbrownandsons.com' AND g."Name"='EBS End of Day Fill')
   OR (lower(btrim(u."Email"))='wes@fruitandland.com' AND g."Name" IN ('WP End of Day Fill','EBS End of Day Fill'))
ON CONFLICT ("UserId","ReportGroupId") DO NOTHING;

DO $capacity_guard$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM _end_of_day_fill_room_capacity_before before
        FULL JOIN "Rooms" current ON current."Id"=before."Id"
        WHERE before."Id" IS NULL OR current."Id" IS NULL OR before."CapacityBins" IS DISTINCT FROM current."CapacityBins") THEN
        RAISE EXCEPTION 'Room capacity fingerprint changed during End of Day Fill compatibility apply. Transaction rolled back.';
    END IF;
END $capacity_guard$;

-- Production migration history is intentionally untouched because this package is the bounded object-state compatibility path.
COMMIT;
\ir verify-end-of-day-fill-reporting.sql
