\set ON_ERROR_STOP on
\if :{?data_cleanup_allowed_emails}
\else
\set data_cleanup_allowed_emails 'wes@fruitandland.com'
\endif
\if :{?crop_year_review_allowed_emails}
\else
\set crop_year_review_allowed_emails 'wes@fruitandland.com'
\endif
BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260807210820_AddRoleBasedUserAccess',0));

CREATE TEMP TABLE _protected_end_of_day_fill_state(
    object_name text PRIMARY KEY,
    row_count bigint NOT NULL,
    fingerprint text NOT NULL
) ON COMMIT DROP;

INSERT INTO _protected_end_of_day_fill_state
SELECT 'EndOfDayFillReportGroups',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillReportGroups") x
UNION ALL
SELECT 'EndOfDayFillReportRecipients',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillReportRecipients") x
UNION ALL
SELECT 'EndOfDayFillUserGroupAssignments',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x
UNION ALL
SELECT 'RoomEndOfDayFillAssignmentsAndCapacities',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x
UNION ALL
SELECT 'EndOfDayFillReportSends',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillReportSends") x
UNION ALL
SELECT 'EndOfDayFillSendReservations',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),''))
FROM (SELECT * FROM "EndOfDayFillSendReservations") x;

DO $precheck$
DECLARE target_exists boolean;
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'Users')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'Roles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserRoles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserPageAccesses')) IS NULL THEN
        RAISE EXCEPTION 'Required legacy user access objects are missing. Transaction rolled back.';
    END IF;
    target_exists := to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL;
    IF target_exists IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='IsActive')
       OR target_exists IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='NormalizedName') THEN
        RAISE EXCEPTION 'Partial role-based access object state detected. Transaction rolled back.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
        WHERE u."IsActive" GROUP BY u."Id" HAVING count(ur."RoleId")<>1) THEN
        RAISE EXCEPTION 'Every active user must have exactly one reviewed role before conversion. Transaction rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM "UserRoles" GROUP BY "UserId" HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Multiple UserRole assignments must be resolved before conversion. Transaction rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc user')
       AND EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc tech') THEN
        RAISE EXCEPTION 'QC User and QC Tech both exist. Review the conflict before conversion. Transaction rolled back.';
    END IF;
END $precheck$;

SELECT to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL AS schema_exists \gset
\if :schema_exists
\echo 'Role-based access schema already exists; verifying idempotent state without rewriting configuration.'
DO $existing$
BEGIN
    IF (SELECT count(*) FROM "Roles" WHERE "IsSystemRole" AND "IsActive" AND "Name" IN ('Viewer','QC Tech','QC Admin','Manager','Admin')) <> 5
       OR EXISTS (SELECT 1 FROM "Roles" WHERE "Name"='QC User') THEN
        RAISE EXCEPTION 'Built-in role state is incomplete or ambiguous. Transaction rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM "RolePageAccesses" WHERE "AccessLevel" NOT IN ('None','View','Create','Admin')) THEN
        RAISE EXCEPTION 'Invalid role access level detected. Transaction rolled back.';
    END IF;
    IF (SELECT count(*) FROM "RolePageAccesses" admin_access JOIN "Roles" r ON r."Id"=admin_access."RoleId" WHERE r."Name"='Admin' AND admin_access."AccessLevel"='Admin') <> 40 THEN
        RAISE EXCEPTION 'Admin matrix is not complete full access. Transaction rolled back.';
    END IF;
END $existing$;
\else
CREATE TEMP TABLE _role_area_definition (
    area_key text PRIMARY KEY,
    legacy_area_key text NULL
) ON COMMIT DROP;
INSERT INTO _role_area_definition(area_key,legacy_area_key) VALUES
 ('dashboard',NULL),('daily-qc',NULL),('field-samples',NULL),('qc-reports','daily-qc'),('receipts',NULL),('current-lots',NULL),('bins-run',NULL),
 ('projection-planner','bins-run'),('projection-outcome','bins-run'),('actual-runs','bins-run'),('packout-results','projection-outcome'),
 ('historical-inventory-cleanup','data-cleanup'),('rooms',NULL),('room-transactions',NULL),('transfers','room-transactions'),('true-up','room-transactions'),
 ('inventory','current-lots'),('grower-lots',NULL),('crop-year-review',NULL),('master-data',NULL),('users',NULL),('permission-matrix','users'),
 ('qc-stations',NULL),('downloads',NULL),('configuration',NULL),('variety-colors',NULL),('backups',NULL),('orchard-recipients','configuration'),
 ('orchard-managers','configuration'),('facilities','master-data'),('varieties','master-data'),('grades','master-data'),('defects','master-data'),
 ('size-configuration','master-data'),('email-configuration','configuration'),('backup-history','backups'),('audit-history','master-data'),
 ('import-tools','master-data'),('export-tools','master-data'),('data-cleanup',NULL);

CREATE TEMP TABLE _role_legacy_effective ON COMMIT DROP AS
SELECT r."Id" role_id,r."Name" role_name,lower(btrim(u."Email")) email,a.area_key,
 CASE WHEN a.area_key='data-cleanup'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN a.area_key='crop-year-review'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN lower(coalesce(master."AccessLevel",'None'))='admin' THEN 'Admin'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None'))='edit' THEN 'Create'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')) IN ('none','view','create','admin') THEN initcap(lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')))
      ELSE 'None' END access_level
FROM "Users" u
JOIN "UserRoles" ur ON ur."UserId"=u."Id"
JOIN "Roles" r ON r."Id"=ur."RoleId"
CROSS JOIN _role_area_definition a
LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
WHERE u."IsActive" AND lower(btrim(u."Email")) <> 'wes@fruitandland.com';

DO $conflicts$
BEGIN
    IF EXISTS (
        SELECT 1 FROM _role_legacy_effective e
        WHERE lower(e.role_name)<>'admin'
        GROUP BY e.role_id,e.area_key HAVING count(DISTINCT e.access_level)>1) THEN
        RAISE EXCEPTION 'Users assigned to the same role have conflicting effective access. Review preflight output; transaction rolled back.';
    END IF;
END $conflicts$;

ALTER TABLE "Roles" ADD COLUMN "IsActive" boolean NOT NULL DEFAULT TRUE;
ALTER TABLE "Roles" ADD COLUMN "NormalizedName" character varying(100);
UPDATE "Roles" SET "NormalizedName"=upper(btrim("Name"));
ALTER TABLE "Roles" ALTER COLUMN "NormalizedName" SET NOT NULL;
DROP INDEX IF EXISTS "IX_Roles_Name";
CREATE UNIQUE INDEX "IX_Roles_NormalizedName" ON "Roles"("NormalizedName");

WITH renamed AS (
 UPDATE "Roles" SET "Name"='QC Tech',"NormalizedName"='QC TECH',"IsSystemRole"=TRUE,"IsActive"=TRUE
 WHERE lower(btrim("Name"))='qc user' RETURNING "Id"
)
INSERT INTO "AuditLogs"("Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
SELECT 'rename','roles',"Id"::text,'{"name":"QC User"}','{"name":"QC Tech"}','CropQc.Deployment',CURRENT_TIMESTAMP FROM renamed;

INSERT INTO "Roles"("Name","NormalizedName","Description","IsSystemRole","IsActive") VALUES
 ('Viewer','VIEWER','Read-only operational visibility.',TRUE,TRUE),
 ('QC Tech','QC TECH','Capture receiving samples and QC readings.',TRUE,TRUE),
 ('QC Admin','QC ADMIN','QC workflow and QC configuration administration without system security access.',TRUE,TRUE),
 ('Manager','MANAGER','Broad operational management without security administration.',TRUE,TRUE),
 ('Admin','ADMIN','Full dashboard and configuration access.',TRUE,TRUE)
ON CONFLICT ("NormalizedName") DO UPDATE SET "IsSystemRole"=TRUE,"IsActive"=TRUE;

CREATE TABLE "RolePageAccesses"(
 "Id" integer GENERATED BY DEFAULT AS IDENTITY,
 "RoleId" integer NOT NULL,
 "AreaKey" character varying(100) NOT NULL,
 "AccessLevel" character varying(25) NOT NULL,
 "UpdatedByUserId" integer NULL,
 "UpdatedAt" timestamp with time zone NOT NULL,
 CONSTRAINT "PK_RolePageAccesses" PRIMARY KEY("Id"),
 CONSTRAINT "FK_RolePageAccesses_Roles_RoleId" FOREIGN KEY("RoleId") REFERENCES "Roles"("Id") ON DELETE CASCADE,
 CONSTRAINT "FK_RolePageAccesses_Users_UpdatedByUserId" FOREIGN KEY("UpdatedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL,
 CONSTRAINT "CK_RolePageAccesses_AccessLevel" CHECK ("AccessLevel" IN ('None','View','Create','Admin'))
);
CREATE UNIQUE INDEX "IX_RolePageAccesses_RoleId_AreaKey" ON "RolePageAccesses"("RoleId","AreaKey");
CREATE INDEX "IX_RolePageAccesses_UpdatedByUserId" ON "RolePageAccesses"("UpdatedByUserId");
CREATE UNIQUE INDEX "IX_UserRoles_UserId" ON "UserRoles"("UserId");

INSERT INTO "RolePageAccesses"("RoleId","AreaKey","AccessLevel","UpdatedAt")
SELECT r."Id",a.area_key,
 CASE
  WHEN r."Name"='Admin' THEN 'Admin'
  WHEN max(e.access_level) IS NOT NULL THEN max(e.access_level)
  WHEN r."Name"='Viewer' THEN CASE WHEN a.area_key IN ('dashboard','daily-qc','field-samples','qc-reports','receipts','current-lots','rooms','inventory','grower-lots') THEN 'View' ELSE 'None' END
  WHEN r."Name"='QC Tech' THEN CASE WHEN a.area_key IN ('daily-qc','field-samples','receipts') THEN 'Create' WHEN a.area_key IN ('dashboard','qc-reports','current-lots','rooms','inventory','grower-lots') THEN 'View' ELSE 'None' END
  WHEN r."Name"='QC Admin' THEN CASE WHEN a.area_key IN ('daily-qc','field-samples','qc-reports','qc-stations','varieties','grades','defects','size-configuration','variety-colors','orchard-recipients','orchard-managers') THEN 'Admin' WHEN a.area_key='master-data' THEN 'View' WHEN a.area_key='receipts' THEN 'Create' WHEN a.area_key IN ('dashboard','current-lots','rooms','inventory','grower-lots') THEN 'View' ELSE 'None' END
  WHEN r."Name"='Manager' THEN CASE WHEN a.area_key='dashboard' THEN 'View' WHEN a.area_key IN ('downloads','audit-history') THEN 'View' WHEN a.area_key IN ('users','permission-matrix','configuration','backups','backup-history','email-configuration','data-cleanup','crop-year-review','historical-inventory-cleanup') THEN 'None' ELSE 'Admin' END
  ELSE 'None' END,
 CURRENT_TIMESTAMP
FROM "Roles" r CROSS JOIN _role_area_definition a
LEFT JOIN _role_legacy_effective e ON e.role_id=r."Id" AND e.area_key=a.area_key
GROUP BY r."Id",r."Name",a.area_key;
\endif

CREATE TEMP TABLE _protected_end_of_day_fill_state_after ON COMMIT DROP AS
SELECT 'EndOfDayFillReportGroups' object_name,count(*) row_count,md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) fingerprint
FROM (SELECT * FROM "EndOfDayFillReportGroups") x
UNION ALL
SELECT 'EndOfDayFillReportRecipients',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillReportRecipients") x
UNION ALL
SELECT 'EndOfDayFillUserGroupAssignments',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x
UNION ALL
SELECT 'RoomEndOfDayFillAssignmentsAndCapacities',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x
UNION ALL
SELECT 'EndOfDayFillReportSends',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),''))
FROM (SELECT * FROM "EndOfDayFillReportSends") x
UNION ALL
SELECT 'EndOfDayFillSendReservations',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),''))
FROM (SELECT * FROM "EndOfDayFillSendReservations") x;

DO $end_of_day_fill_preservation$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM _protected_end_of_day_fill_state before_state
        FULL JOIN _protected_end_of_day_fill_state_after after_state USING(object_name)
        WHERE before_state.row_count IS DISTINCT FROM after_state.row_count
           OR before_state.fingerprint IS DISTINCT FROM after_state.fingerprint) THEN
        RAISE EXCEPTION 'Role conversion changed protected End-of-Day Fill configuration or history. Transaction rolled back.';
    END IF;
END $end_of_day_fill_preservation$;

SELECT object_name,row_count,fingerprint
FROM _protected_end_of_day_fill_state_after
ORDER BY object_name;

COMMIT;
