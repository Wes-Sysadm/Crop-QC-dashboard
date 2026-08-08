\set ON_ERROR_STOP on
\if :{?data_cleanup_allowed_emails}
\else
\set data_cleanup_allowed_emails 'wes@fruitandland.com'
\endif
\if :{?crop_year_review_allowed_emails}
\else
\set crop_year_review_allowed_emails 'wes@fruitandland.com'
\endif
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_tables integer;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'Users')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'Roles')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'UserRoles')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'UserPageAccesses')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroups')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportRecipients')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillUserGroupAssignments')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportSends')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillSendReservations')) IS NULL THEN
        RAISE EXCEPTION 'Required legacy user-access objects are missing. No changes were made.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='Rooms'
          AND column_name='EndOfDayFillReportGroupId') THEN
        RAISE EXCEPTION 'The deployed End-of-Day Fill room assignment column is missing. No changes were made.';
    END IF;

    SELECT count(*) INTO target_tables
    FROM (VALUES ('RolePageAccesses')) expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NOT NULL;

    IF target_tables = 0 AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='Roles' AND column_name IN ('IsActive','NormalizedName')) THEN
        RAISE EXCEPTION 'Partial role-based access schema detected. No changes were made.';
    END IF;
    IF target_tables = 1 AND (
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='Roles' AND column_name IN ('IsActive','NormalizedName')) <> 2 THEN
        RAISE EXCEPTION 'Partial role-based access schema detected. No changes were made.';
    END IF;
END $preflight$;

SELECT r."Id" AS role_id, r."Name" AS role_name, r."Description", r."IsSystemRole",
       count(DISTINCT ur."UserId") AS assigned_users
FROM "Roles" r
LEFT JOIN "UserRoles" ur ON ur."RoleId"=r."Id"
GROUP BY r."Id",r."Name",r."Description",r."IsSystemRole"
ORDER BY lower(r."Name"),r."Id";

SELECT u."Id" AS user_id, lower(btrim(u."Email")) AS email, u."DisplayName", u."IsActive",
       count(DISTINCT ur."RoleId") AS role_count,
       string_agg(DISTINCT r."Name", ', ' ORDER BY r."Name") AS roles,
       count(DISTINCT upa."Id") AS legacy_access_rows
FROM "Users" u
LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
LEFT JOIN "Roles" r ON r."Id"=ur."RoleId"
LEFT JOIN "UserPageAccesses" upa ON upa."UserId"=u."Id"
GROUP BY u."Id",u."Email",u."DisplayName",u."IsActive"
ORDER BY lower(u."Email"),u."Id";

SELECT count(*) AS legacy_user_page_access_rows FROM "UserPageAccesses";

SELECT g."Id",g."Name",g."Facility",g."IsActive",count(r."Id") AS assigned_rooms
FROM "EndOfDayFillReportGroups" g
LEFT JOIN "Rooms" r ON r."EndOfDayFillReportGroupId"=g."Id"
GROUP BY g."Id",g."Name",g."Facility",g."IsActive"
ORDER BY g."Facility",g."Id";

SELECT lower("EmailAddress") AS email,"IsActive","SortOrder"
FROM "EndOfDayFillReportRecipients"
ORDER BY "SortOrder",lower("EmailAddress"),"Id";

SELECT lower(u."Email") AS email,string_agg(g."Facility",',' ORDER BY g."Facility") AS report_groups
FROM "EndOfDayFillUserGroupAssignments" a
JOIN "Users" u ON u."Id"=a."UserId"
JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId"
GROUP BY u."Id",u."Email"
ORDER BY lower(u."Email"),u."Id";

SELECT count(*) AS room_rows,
       md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) AS room_assignment_capacity_fingerprint
FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x;
SELECT count(*) AS end_of_day_fill_sends FROM "EndOfDayFillReportSends";
SELECT count(*) AS end_of_day_fill_reservations FROM "EndOfDayFillSendReservations";

SELECT 'DataCleanup:AllowedEmails' AS legacy_gate,
       :'data_cleanup_allowed_emails' AS configured_emails
UNION ALL
SELECT 'CropYearReview:AllowedEmails', :'crop_year_review_allowed_emails';

WITH areas(area_key,area_name,legacy_area_key) AS (VALUES
 ('dashboard','Dashboard',NULL),('daily-qc','Receipt QC',NULL),('field-samples','Field Samples',NULL),('qc-reports','QC Reports','daily-qc'),
 ('receipts','Receipts',NULL),('current-lots','Current Lots',NULL),('bins-run','Bins Run',NULL),('projection-planner','Projection Planner','bins-run'),
 ('projection-outcome','Planning Projection Reports','bins-run'),('actual-runs','Actual Runs','bins-run'),('packout-results','Packout Results','projection-outcome'),
 ('historical-inventory-cleanup','EBS Historical Cleanup','data-cleanup'),('rooms','Rooms',NULL),('room-transactions','Room Transactions',NULL),
 ('transfers','Transfers','room-transactions'),('true-up','True Up','room-transactions'),('inventory','Inventory','current-lots'),('grower-lots','Grower Lots',NULL),
 ('crop-year-review','Crop Year Review',NULL),('master-data','Master Data',NULL),('users','Users',NULL),('permission-matrix','Permission Matrix','users'),
 ('qc-stations','QC Stations',NULL),('downloads','Downloads',NULL),('configuration','Configuration',NULL),('variety-colors','Variety Colors',NULL),
 ('backups','Backups',NULL),('orchard-recipients','Orchard Recipients','configuration'),('orchard-managers','Orchard Managers','configuration'),
 ('facilities','Facilities','master-data'),('varieties','Varieties','master-data'),('grades','Grades','master-data'),('defects','Defects','master-data'),
 ('size-configuration','Size Configuration','master-data'),('email-configuration','Email Configuration','configuration'),('backup-history','Backup History','backups'),
 ('audit-history','Audit History','master-data'),('import-tools','Import Tools','master-data'),('export-tools','Export Tools','master-data'),('data-cleanup','Data Cleanup',NULL)
), users_and_roles AS (
 SELECT u."Id" user_id, lower(btrim(u."Email")) email, u."DisplayName", u."IsActive", r."Id" role_id, r."Name" role_name
 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" LEFT JOIN "Roles" r ON r."Id"=ur."RoleId"
), raw AS (
 SELECT ur.*,a.*,coalesce(direct."AccessLevel",legacy."AccessLevel",'None') raw_level,
        coalesce(master."AccessLevel",'None') master_level
 FROM users_and_roles ur CROSS JOIN areas a
 LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=ur.user_id AND lower(direct."AreaKey")=a.area_key
 LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=ur.user_id AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
 LEFT JOIN "UserPageAccesses" master ON master."UserId"=ur.user_id AND lower(master."AreaKey")='master-data'
), effective AS (
 SELECT *, CASE
   WHEN email='wes@fruitandland.com' THEN 'Admin'
   WHEN NOT "IsActive" THEN 'None'
   WHEN area_key='data-cleanup'
        AND NOT (email = ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
   WHEN area_key='crop-year-review'
        AND NOT (email = ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
   WHEN lower(master_level)='admin' THEN 'Admin'
   WHEN lower(raw_level)='edit' THEN 'Create'
   WHEN lower(raw_level) IN ('none','view','create','admin') THEN initcap(lower(raw_level))
   ELSE 'None' END old_effective_level
 FROM raw
)
SELECT role_name,email,"DisplayName",area_key,area_name,raw_level,master_level,old_effective_level
FROM effective
ORDER BY lower(role_name),lower(email),area_key;

WITH areas(area_key,legacy_area_key) AS (VALUES
 ('dashboard',NULL),('daily-qc',NULL),('field-samples',NULL),('qc-reports','daily-qc'),('receipts',NULL),('current-lots',NULL),('bins-run',NULL),
 ('projection-planner','bins-run'),('projection-outcome','bins-run'),('actual-runs','bins-run'),('packout-results','projection-outcome'),
 ('historical-inventory-cleanup','data-cleanup'),('rooms',NULL),('room-transactions',NULL),('transfers','room-transactions'),('true-up','room-transactions'),
 ('inventory','current-lots'),('grower-lots',NULL),('crop-year-review',NULL),('master-data',NULL),('users',NULL),('permission-matrix','users'),
 ('qc-stations',NULL),('downloads',NULL),('configuration',NULL),('variety-colors',NULL),('backups',NULL),('orchard-recipients','configuration'),
 ('orchard-managers','configuration'),('facilities','master-data'),('varieties','master-data'),('grades','master-data'),('defects','master-data'),
 ('size-configuration','master-data'),('email-configuration','configuration'),('backup-history','backups'),('audit-history','master-data'),
 ('import-tools','master-data'),('export-tools','master-data'),('data-cleanup',NULL)
), effective AS (
 SELECT r."Id" role_id,r."Name" role_name,lower(btrim(u."Email")) email,a.area_key,
 CASE WHEN a.area_key='data-cleanup'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN a.area_key='crop-year-review'
           AND NOT (lower(btrim(u."Email")) = ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN lower(coalesce(master."AccessLevel",'None'))='admin' THEN 'Admin'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None'))='edit' THEN 'Create'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')) IN ('none','view','create','admin') THEN initcap(lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')))
      ELSE 'None' END access_level
 FROM "Users" u JOIN "UserRoles" ur ON ur."UserId"=u."Id" JOIN "Roles" r ON r."Id"=ur."RoleId" CROSS JOIN areas a
 LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
 LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
 LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
 WHERE u."IsActive" AND lower(btrim(u."Email")) <> 'wes@fruitandland.com'
)
SELECT role_id,role_name,area_key,count(DISTINCT access_level) AS distinct_levels,
       string_agg(email||'='||access_level,', ' ORDER BY email) AS user_levels
FROM effective GROUP BY role_id,role_name,area_key HAVING count(DISTINCT access_level)>1
ORDER BY lower(role_name),area_key;

SELECT lower(btrim(u."Email")) AS email, u."DisplayName", u."IsActive", count(ur."RoleId") AS role_count,
       CASE WHEN count(ur."RoleId")=0 THEN 'missing role' ELSE 'multiple roles' END AS issue
FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
GROUP BY u."Id",u."Email",u."DisplayName",u."IsActive"
HAVING count(ur."RoleId")<>1
ORDER BY lower(u."Email");

SELECT CASE
 WHEN EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc user')
  AND EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc tech') THEN 'blocked: QC User and QC Tech both exist'
 WHEN EXISTS (SELECT 1 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" WHERE u."IsActive" GROUP BY u."Id" HAVING count(ur."RoleId")<>1) THEN 'blocked: active user role cardinality'
 ELSE 'review role conflict result above before apply' END AS role_based_access_preflight_status,
 CASE WHEN to_regclass(format('%I.%I', current_schema(), 'RolePageAccesses')) IS NULL THEN 'absent' ELSE 'present' END AS target_state;

ROLLBACK;
