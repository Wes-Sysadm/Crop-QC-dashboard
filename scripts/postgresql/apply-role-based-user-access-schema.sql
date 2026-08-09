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

CREATE TEMP TABLE _protected_state(
    object_name text PRIMARY KEY,
    row_count bigint NOT NULL,
    fingerprint text NOT NULL
) ON COMMIT DROP;

INSERT INTO _protected_state
SELECT 'LegacyUserPageAccesses',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "UserPageAccesses") x
UNION ALL SELECT 'EndOfDayFillReportGroups',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportGroups") x
UNION ALL SELECT 'EndOfDayFillReportRecipients',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportRecipients") x
UNION ALL SELECT 'EndOfDayFillUserGroupAssignments',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x
UNION ALL SELECT 'RoomEndOfDayFillAssignmentsAndCapacities',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x
UNION ALL SELECT 'EndOfDayFillReportSends',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportSends") x
UNION ALL SELECT 'EndOfDayFillSendReservations',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),'')) FROM (SELECT * FROM "EndOfDayFillSendReservations") x;

DO $reviewed_end_of_day_fill_state$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('EndOfDayFillReportGroups',2::bigint,'4ed24ac0ab6c6ce1525799c5b427ad0d'),
            ('EndOfDayFillReportRecipients',3::bigint,'25450543a5e2af47d5a3642fbd33983c'),
            ('EndOfDayFillUserGroupAssignments',4::bigint,'74c7bf40bfd8e94da81f6756d4c55de1'),
            ('RoomEndOfDayFillAssignmentsAndCapacities',69::bigint,'a168b825cd73c3104b6648bff75a138c'),
            ('EndOfDayFillReportSends',2::bigint,'7c63ed8bee27486fefc73ad9da734a93'),
            ('EndOfDayFillSendReservations',0::bigint,'d41d8cd98f00b204e9800998ecf8427e')
        ) expected(object_name,row_count,fingerprint)
        LEFT JOIN _protected_state actual USING(object_name)
        WHERE actual.object_name IS NULL OR actual.row_count<>expected.row_count OR actual.fingerprint<>expected.fingerprint
    )
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP')<>42
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='EBS')<>27
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP' AND upper(r."Code") IN ('MCD-01','WP-4','WP-5','WP-6','WP-7','WP-8'))<>6 THEN
        RAISE EXCEPTION 'End-of-Day Fill state no longer matches verified backup run 54. Regenerate before role DDL; transaction rolled back.';
    END IF;
END $reviewed_end_of_day_fill_state$;

DO $prerequisites$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'Users')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'Roles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserRoles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserPageAccesses')) IS NULL THEN
        RAISE EXCEPTION 'Required legacy user-access objects are missing. Transaction rolled back.';
    END IF;
    IF (to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL) IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='IsActive')
       OR (to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL) IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='NormalizedName') THEN
        RAISE EXCEPTION 'Partial role-based access object state detected. Transaction rolled back.';
    END IF;
END $prerequisites$;

CREATE TEMP TABLE _role_area_definition(
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

CREATE TEMP TABLE _reviewed_user_mapping(
    email text PRIMARY KEY,
    display_name text NOT NULL,
    target_role text NOT NULL,
    expected_fingerprint text NOT NULL
) ON COMMIT DROP;
INSERT INTO _reviewed_user_mapping VALUES
 ('ada@wp-packing.com','Ada Hernandez','QC Tech','38d291003ef3287eaf098fc161c4496d'),
 ('alexis@wp-packing.com','Alexis Ledezma','Imported Access A','4c094069afe868d0f2be67fd41965528'),
 ('dan@earlbrownandsons.com','Dan Kezele','Manager','4c094069afe868d0f2be67fd41965528'),
 ('hl@fruitandland.com','Harvest Log','Imported Access B','d9184fc3c77b3ceff3c9ff6f08f603c6'),
 ('james@fruitandland.com','James Foreman','Imported Access A','4c094069afe868d0f2be67fd41965528'),
 ('jorge@wp-packing.com','Jorge Ledezma','Imported Access A','4c094069afe868d0f2be67fd41965528'),
 ('kyle@fruitandland.com','Kyle Hendrickson','Imported Access C','7bb78e3a6bc3c6d779804438458849eb'),
 ('maria@wp-packing.com','Maria Ledezma','Imported Access D','5fa520cf854f0158aea6eb14f2e7b967'),
 ('maurya@fruitandland.com','Maurya Dunning','Manager','4c094069afe868d0f2be67fd41965528'),
 ('rob@earlbrownandsons.com','Robert Fulgham','Admin','4c094069afe868d0f2be67fd41965528'),
 ('shianne@earlbrownandsons.com','Shianne Allen','Imported Access E','38d476024d63b43fba825b0f450751be'),
 ('wes@fruitandland.com','Wes Cusick','Admin','85f8d6a96a71434b59e3dfdc5ef6318e');

CREATE TEMP TABLE _legacy_effective ON COMMIT DROP AS
SELECT u."Id" user_id,lower(btrim(u."Email")) email,u."DisplayName" display_name,a.area_key,
 CASE WHEN lower(btrim(u."Email"))='wes@fruitandland.com' THEN 'Admin'
      WHEN NOT u."IsActive" THEN 'None'
      WHEN a.area_key='data-cleanup'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN a.area_key='crop-year-review'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN lower(coalesce(master."AccessLevel",'None'))='admin' THEN 'Admin'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None'))='edit' THEN 'Create'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')) IN ('none','view','create','admin')
           THEN initcap(lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')))
      ELSE 'None' END access_level
FROM "Users" u CROSS JOIN _role_area_definition a
LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
WHERE u."IsActive";

CREATE TEMP TABLE _legacy_fingerprints ON COMMIT DROP AS
SELECT email,display_name,
 md5(string_agg(area_key||'='||access_level,'|' ORDER BY area_key)) fingerprint,
 count(*) area_count
FROM _legacy_effective GROUP BY email,display_name;

DO $reviewed_guard$
DECLARE schema_exists boolean := to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL;
BEGIN
    IF (SELECT count(*) FROM "Users" WHERE "IsActive") <> 12
       OR (SELECT count(*) FROM _reviewed_user_mapping) <> 12
       OR EXISTS (
          SELECT 1 FROM _reviewed_user_mapping m
          FULL JOIN _legacy_fingerprints f USING(email)
          WHERE m.email IS NULL OR f.email IS NULL OR f.display_name<>m.display_name
             OR f.area_count<>40 OR f.fingerprint<>m.expected_fingerprint) THEN
        RAISE EXCEPTION 'A reviewed active user identity or effective-access fingerprint changed. Reclassify before DDL; transaction rolled back.';
    END IF;
    IF (SELECT count(DISTINCT fingerprint) FROM _legacy_fingerprints
        WHERE email IN ('alexis@wp-packing.com','james@fruitandland.com','jorge@wp-packing.com')) <> 1 THEN
        RAISE EXCEPTION 'Alexis, James, and Jorge no longer share the reviewed Imported Access A matrix. Transaction rolled back.';
    END IF;
    IF NOT schema_exists AND ((SELECT count(*) FROM "Roles")<>4
       OR EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name")) NOT IN ('admin','manager','qc user','viewer'))
       OR EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc tech')) THEN
        RAISE EXCEPTION 'The pre-conversion role identities no longer match the reviewed baseline. Transaction rolled back.';
    END IF;
END $reviewed_guard$;

CREATE TEMP TABLE _previous_role_assignments ON COMMIT DROP AS
SELECT u."Id" user_id,lower(btrim(u."Email")) email,
       coalesce(string_agg(r."Name",', ' ORDER BY r."Name"),'') role_names
FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" LEFT JOIN "Roles" r ON r."Id"=ur."RoleId"
WHERE u."IsActive" GROUP BY u."Id",u."Email";

SELECT to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL AS schema_exists \gset
\if :schema_exists
\echo 'Role-based access schema already exists; verifying reviewed idempotent state without rewriting configuration.'
\else
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

INSERT INTO "Roles"("Name","NormalizedName","Description","IsSystemRole","IsActive")
SELECT name,upper(name),
 'Imported from the legacy per-user access matrix during the role-based authorization conversion. Review and rename or reassign in User Administration.',
 FALSE,TRUE
FROM unnest(ARRAY['Imported Access A','Imported Access B','Imported Access C','Imported Access D','Imported Access E']) name;

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

CREATE TEMP TABLE _role_matrix_source(role_name text PRIMARY KEY,email text NOT NULL) ON COMMIT DROP;
INSERT INTO _role_matrix_source VALUES
 ('QC Tech','ada@wp-packing.com'),('Manager','dan@earlbrownandsons.com'),
 ('Imported Access A','alexis@wp-packing.com'),('Imported Access B','hl@fruitandland.com'),
 ('Imported Access C','kyle@fruitandland.com'),('Imported Access D','maria@wp-packing.com'),
 ('Imported Access E','shianne@earlbrownandsons.com');

INSERT INTO "RolePageAccesses"("RoleId","AreaKey","AccessLevel","UpdatedAt")
SELECT r."Id",a.area_key,
 CASE
  WHEN r."Name"='Admin' THEN 'Admin'
  WHEN source.email IS NOT NULL THEN (SELECT e.access_level FROM _legacy_effective e WHERE e.email=source.email AND e.area_key=a.area_key)
  WHEN r."Name"='Viewer' THEN CASE WHEN a.area_key IN ('dashboard','daily-qc','field-samples','qc-reports','receipts','current-lots','rooms','inventory','grower-lots') THEN 'View' ELSE 'None' END
  WHEN r."Name"='QC Admin' THEN CASE WHEN a.area_key IN ('daily-qc','field-samples','qc-reports','qc-stations','varieties','grades','defects','size-configuration','variety-colors','orchard-recipients','orchard-managers') THEN 'Admin' WHEN a.area_key='master-data' THEN 'View' WHEN a.area_key='receipts' THEN 'Create' WHEN a.area_key IN ('dashboard','current-lots','rooms','inventory','grower-lots') THEN 'View' ELSE 'None' END
  ELSE 'None' END,
 CURRENT_TIMESTAMP
FROM "Roles" r CROSS JOIN _role_area_definition a
LEFT JOIN _role_matrix_source source ON source.role_name=r."Name";

DELETE FROM "UserRoles" ur USING "Users" u,_reviewed_user_mapping m
WHERE ur."UserId"=u."Id" AND u."IsActive" AND lower(btrim(u."Email"))=m.email;

INSERT INTO "UserRoles"("UserId","RoleId")
SELECT u."Id",r."Id" FROM _reviewed_user_mapping m
JOIN "Users" u ON lower(btrim(u."Email"))=m.email AND u."IsActive"
JOIN "Roles" r ON r."Name"=m.target_role;

INSERT INTO "AuditLogs"("Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
SELECT 'convert-role','user-role',u."Id"::text,
       jsonb_build_object('email',m.email,'roles',p.role_names)::text,
       jsonb_build_object('email',m.email,'role',m.target_role)::text,
       'CropQc.Deployment',CURRENT_TIMESTAMP
FROM _reviewed_user_mapping m JOIN "Users" u ON lower(btrim(u."Email"))=m.email
JOIN _previous_role_assignments p ON p.user_id=u."Id";

CREATE UNIQUE INDEX "IX_UserRoles_UserId" ON "UserRoles"("UserId");
\endif

DO $converted_state$
BEGIN
    IF (SELECT count(*) FROM "Roles" WHERE "IsSystemRole" AND "IsActive" AND "Name" IN ('Viewer','QC Tech','QC Admin','Manager','Admin'))<>5
       OR (SELECT count(*) FROM "Roles" WHERE NOT "IsSystemRole" AND "IsActive" AND "Name" LIKE 'Imported Access %')<>5
       OR EXISTS (SELECT 1 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" WHERE u."IsActive" GROUP BY u."Id" HAVING count(ur."RoleId")<>1)
       OR EXISTS (
          SELECT 1 FROM _reviewed_user_mapping m JOIN "Users" u ON lower(btrim(u."Email"))=m.email
          LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" LEFT JOIN "Roles" r ON r."Id"=ur."RoleId"
          GROUP BY m.email,m.target_role HAVING count(*)<>1 OR max(r."Name")<>m.target_role)
       OR EXISTS (SELECT 1 FROM "Roles" r LEFT JOIN "RolePageAccesses" a ON a."RoleId"=r."Id" WHERE r."IsActive" GROUP BY r."Id" HAVING count(a."Id")<>40)
       OR (SELECT count(*) FROM "RolePageAccesses" a JOIN "Roles" r ON r."Id"=a."RoleId" WHERE r."Name"='Admin' AND a."AccessLevel"='Admin')<>40 THEN
        RAISE EXCEPTION 'Converted role state does not match the reviewed mapping. Transaction rolled back.';
    END IF;
END $converted_state$;

CREATE TEMP TABLE _access_differences ON COMMIT DROP AS
SELECT e.email,r."Name" role_name,e.area_key,e.access_level old_effective_level,
       CASE WHEN e.email='wes@fruitandland.com' OR r."Name"='Admin' THEN 'Admin'
            ELSE coalesce(a."AccessLevel",'None') END new_effective_level
FROM _legacy_effective e
JOIN "Users" u ON u."Id"=e.user_id
JOIN "UserRoles" ur ON ur."UserId"=u."Id"
JOIN "Roles" r ON r."Id"=ur."RoleId"
LEFT JOIN "RolePageAccesses" a ON a."RoleId"=r."Id" AND a."AreaKey"=e.area_key
WHERE e.access_level IS DISTINCT FROM CASE WHEN e.email='wes@fruitandland.com' OR r."Name"='Admin' THEN 'Admin' ELSE coalesce(a."AccessLevel",'None') END;

DO $difference_guard$
BEGIN
    IF (SELECT count(*) FROM _access_differences)<>2
       OR EXISTS (SELECT 1 FROM _access_differences
                  WHERE email<>'rob@earlbrownandsons.com'
                     OR area_key NOT IN ('crop-year-review','data-cleanup')
                     OR old_effective_level<>'None' OR new_effective_level<>'Admin') THEN
        RAISE EXCEPTION 'Effective access changed outside Robert Fulgham''s two reviewed Admin normalizations. Transaction rolled back.';
    END IF;
END $difference_guard$;

CREATE TEMP TABLE _protected_state_after ON COMMIT DROP AS
SELECT 'LegacyUserPageAccesses' object_name,count(*) row_count,md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) fingerprint FROM (SELECT * FROM "UserPageAccesses") x
UNION ALL SELECT 'EndOfDayFillReportGroups',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportGroups") x
UNION ALL SELECT 'EndOfDayFillReportRecipients',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportRecipients") x
UNION ALL SELECT 'EndOfDayFillUserGroupAssignments',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x
UNION ALL SELECT 'RoomEndOfDayFillAssignmentsAndCapacities',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x
UNION ALL SELECT 'EndOfDayFillReportSends',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportSends") x
UNION ALL SELECT 'EndOfDayFillSendReservations',count(*),md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),'')) FROM (SELECT * FROM "EndOfDayFillSendReservations") x;

DO $preservation$
BEGIN
    IF EXISTS (
        SELECT 1 FROM _protected_state before_state FULL JOIN _protected_state_after after_state USING(object_name)
        WHERE before_state.row_count IS DISTINCT FROM after_state.row_count
           OR before_state.fingerprint IS DISTINCT FROM after_state.fingerprint) THEN
        RAISE EXCEPTION 'Role conversion changed legacy evidence or protected End-of-Day Fill state. Transaction rolled back.';
    END IF;
END $preservation$;

SELECT m.email,m.target_role,f.fingerprint FROM _reviewed_user_mapping m JOIN _legacy_fingerprints f USING(email) ORDER BY m.email;
SELECT email,role_name,area_key,old_effective_level,new_effective_level FROM _access_differences ORDER BY email,area_key;
SELECT object_name,row_count,fingerprint FROM _protected_state_after ORDER BY object_name;
COMMIT;
