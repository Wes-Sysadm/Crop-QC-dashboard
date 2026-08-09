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

CREATE TEMP TABLE _areas(area_key text PRIMARY KEY,legacy_area_key text NULL) ON COMMIT DROP;
INSERT INTO _areas VALUES
 ('dashboard',NULL),('daily-qc',NULL),('field-samples',NULL),('qc-reports','daily-qc'),('receipts',NULL),('current-lots',NULL),('bins-run',NULL),
 ('projection-planner','bins-run'),('projection-outcome','bins-run'),('actual-runs','bins-run'),('packout-results','projection-outcome'),
 ('historical-inventory-cleanup','data-cleanup'),('rooms',NULL),('room-transactions',NULL),('transfers','room-transactions'),('true-up','room-transactions'),
 ('inventory','current-lots'),('grower-lots',NULL),('crop-year-review',NULL),('master-data',NULL),('users',NULL),('permission-matrix','users'),
 ('qc-stations',NULL),('downloads',NULL),('configuration',NULL),('variety-colors',NULL),('backups',NULL),('orchard-recipients','configuration'),
 ('orchard-managers','configuration'),('facilities','master-data'),('varieties','master-data'),('grades','master-data'),('defects','master-data'),
 ('size-configuration','master-data'),('email-configuration','configuration'),('backup-history','backups'),('audit-history','master-data'),
 ('import-tools','master-data'),('export-tools','master-data'),('data-cleanup',NULL);

CREATE TEMP TABLE _mapping(email text PRIMARY KEY,display_name text NOT NULL,target_role text NOT NULL,expected_fingerprint text NOT NULL) ON COMMIT DROP;
INSERT INTO _mapping VALUES
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
      WHEN a.area_key='data-cleanup' AND NOT (lower(btrim(u."Email"))=ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN a.area_key='crop-year-review' AND NOT (lower(btrim(u."Email"))=ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN lower(coalesce(master."AccessLevel",'None'))='admin' THEN 'Admin'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None'))='edit' THEN 'Create'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')) IN ('none','view','create','admin') THEN initcap(lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')))
      ELSE 'None' END access_level
FROM "Users" u CROSS JOIN _areas a
LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
WHERE u."IsActive";

CREATE TEMP TABLE _legacy_fingerprints ON COMMIT DROP AS
SELECT email,display_name,md5(string_agg(area_key||'='||access_level,'|' ORDER BY area_key)) fingerprint,count(*) area_count
FROM _legacy_effective GROUP BY email,display_name;

CREATE TEMP TABLE _comparison ON COMMIT DROP AS
SELECT e.email,r."Name" role_name,e.area_key,e.access_level old_effective_level,
       CASE WHEN e.email='wes@fruitandland.com' OR r."Name"='Admin' THEN 'Admin'
            ELSE coalesce(rpa."AccessLevel",'None') END new_effective_level
FROM _legacy_effective e JOIN "Users" u ON u."Id"=e.user_id
JOIN "UserRoles" ur ON ur."UserId"=u."Id" JOIN "Roles" r ON r."Id"=ur."RoleId"
LEFT JOIN "RolePageAccesses" rpa ON rpa."RoleId"=r."Id" AND rpa."AreaKey"=e.area_key;

DO $verify$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NULL THEN RAISE EXCEPTION 'RolePageAccesses is missing.'; END IF;
    IF (SELECT count(*) FROM "Roles" WHERE "IsSystemRole" AND "IsActive" AND "Name" IN ('Viewer','QC Tech','QC Admin','Manager','Admin'))<>5 THEN RAISE EXCEPTION 'The five active built-in roles are not present exactly once.'; END IF;
    IF (SELECT count(*) FROM "Roles" WHERE NOT "IsSystemRole" AND "IsActive" AND "Name" IN ('Imported Access A','Imported Access B','Imported Access C','Imported Access D','Imported Access E'))<>5 THEN RAISE EXCEPTION 'The five reviewed editable imported roles are not present exactly once.'; END IF;
    IF EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc user') THEN RAISE EXCEPTION 'Legacy QC User role remains.'; END IF;
    IF EXISTS (SELECT 1 FROM "Roles" GROUP BY "NormalizedName" HAVING count(*)<>1) THEN RAISE EXCEPTION 'Normalized role names are not unique.'; END IF;
    IF EXISTS (SELECT 1 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" WHERE u."IsActive" GROUP BY u."Id" HAVING count(ur."RoleId")<>1) THEN RAISE EXCEPTION 'An active user does not have exactly one role.'; END IF;
    IF EXISTS (SELECT 1 FROM "UserRoles" GROUP BY "UserId" HAVING count(*)<>1) THEN RAISE EXCEPTION 'A user has multiple role assignments.'; END IF;
    IF EXISTS (SELECT 1 FROM "Roles" r LEFT JOIN "RolePageAccesses" a ON a."RoleId"=r."Id" WHERE r."IsActive" GROUP BY r."Id" HAVING count(a."Id")<>40) THEN RAISE EXCEPTION 'An active role does not have exactly 40 matrix cells.'; END IF;
    IF EXISTS (SELECT 1 FROM "RolePageAccesses" WHERE "AccessLevel" NOT IN ('None','View','Create','Admin')) THEN RAISE EXCEPTION 'An invalid access level exists.'; END IF;
    IF (SELECT count(*) FROM "RolePageAccesses" a JOIN "Roles" r ON r."Id"=a."RoleId" WHERE r."Name"='Admin' AND a."AccessLevel"='Admin')<>40 THEN RAISE EXCEPTION 'Admin is not full access.'; END IF;
    IF EXISTS (SELECT 1 FROM _mapping m FULL JOIN _legacy_fingerprints f USING(email) WHERE m.email IS NULL OR f.email IS NULL OR f.display_name<>m.display_name OR f.area_count<>40 OR f.fingerprint<>m.expected_fingerprint) THEN RAISE EXCEPTION 'Reviewed legacy identity or fingerprint changed.'; END IF;
    IF EXISTS (SELECT 1 FROM _mapping m JOIN "Users" u ON lower(btrim(u."Email"))=m.email LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id" LEFT JOIN "Roles" r ON r."Id"=ur."RoleId" GROUP BY m.email,m.target_role HAVING count(*)<>1 OR max(r."Name")<>m.target_role) THEN RAISE EXCEPTION 'A user is not assigned to the reviewed target role.'; END IF;
    IF (SELECT count(*) FROM _comparison WHERE old_effective_level<>new_effective_level)<>2
       OR EXISTS (SELECT 1 FROM _comparison WHERE old_effective_level<>new_effective_level AND (email<>'rob@earlbrownandsons.com' OR area_key NOT IN ('crop-year-review','data-cleanup') OR old_effective_level<>'None' OR new_effective_level<>'Admin')) THEN RAISE EXCEPTION 'Unexpected effective-access difference detected.'; END IF;
    IF (SELECT count(*) FROM "UserPageAccesses")<>480 THEN RAISE EXCEPTION 'Legacy UserPageAccess evidence count changed.'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportRecipients" WHERE "IsActive" AND lower("EmailAddress") IN ('wes@fruitandland.com','jorge@wp-packing.com','rob@earlbrownandsons.com'))<>3 THEN RAISE EXCEPTION 'End-of-Day Fill recipients changed.'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillUserGroupAssignments")<>4 THEN RAISE EXCEPTION 'End-of-Day Fill user assignments changed.'; END IF;
    IF (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP')<>42
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='EBS')<>27
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP' AND upper(r."Code") IN ('MCD-01','WP-4','WP-5','WP-6','WP-7','WP-8'))<>6 THEN RAISE EXCEPTION 'End-of-Day Fill Room assignments changed.'; END IF;
    IF (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportGroups") x)<>'4ed24ac0ab6c6ce1525799c5b427ad0d'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportRecipients") x)<>'25450543a5e2af47d5a3642fbd33983c'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x)<>'74c7bf40bfd8e94da81f6756d4c55de1'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x)<>'a168b825cd73c3104b6648bff75a138c'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportSends") x)<>'7c63ed8bee27486fefc73ad9da734a93'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),'')) FROM (SELECT * FROM "EndOfDayFillSendReservations") x)<>'d41d8cd98f00b204e9800998ecf8427e' THEN RAISE EXCEPTION 'End-of-Day Fill protected fingerprints changed from run 54.'; END IF;
END $verify$;

SELECT m.email,m.target_role,f.fingerprint FROM _mapping m JOIN _legacy_fingerprints f USING(email) ORDER BY m.email;
SELECT email,role_name,area_key,old_effective_level,new_effective_level,
       CASE WHEN email='rob@earlbrownandsons.com' AND area_key IN ('crop-year-review','data-cleanup') THEN 'reviewed Admin normalization' ELSE 'BLOCKING' END disposition
FROM _comparison WHERE old_effective_level<>new_effective_level ORDER BY email,area_key;
SELECT r."Name",r."IsSystemRole",r."IsActive",
       (SELECT count(*) FROM "UserRoles" ur WHERE ur."RoleId"=r."Id") assigned_users,
       (SELECT count(*) FROM "RolePageAccesses" a WHERE a."RoleId"=r."Id") matrix_cells,
       (SELECT md5(string_agg(a."AreaKey"||'='||a."AccessLevel",'|' ORDER BY a."AreaKey")) FROM "RolePageAccesses" a WHERE a."RoleId"=r."Id") matrix_fingerprint
FROM "Roles" r ORDER BY r."Name";
SELECT lower(u."Email") email,u."DisplayName",r."Name" role FROM "Users" u JOIN "UserRoles" ur ON ur."UserId"=u."Id" JOIN "Roles" r ON r."Id"=ur."RoleId" WHERE u."IsActive" ORDER BY lower(u."Email");
SELECT count(*) preserved_legacy_user_page_access_rows FROM "UserPageAccesses";
SELECT g."Name",g."Facility",count(r."Id") preserved_assigned_rooms FROM "EndOfDayFillReportGroups" g LEFT JOIN "Rooms" r ON r."EndOfDayFillReportGroupId"=g."Id" GROUP BY g."Id",g."Name",g."Facility" ORDER BY g."Facility";
SELECT lower("EmailAddress") preserved_recipient,"IsActive","SortOrder" FROM "EndOfDayFillReportRecipients" ORDER BY "SortOrder";
SELECT lower(u."Email") email,string_agg(g."Facility",',' ORDER BY g."Facility") preserved_report_groups FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId" GROUP BY u."Id",u."Email" ORDER BY lower(u."Email");
SELECT count(*) room_rows,md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) preserved_room_assignment_capacity_fingerprint FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x;
SELECT count(*) preserved_end_of_day_fill_sends FROM "EndOfDayFillReportSends";
SELECT count(*) preserved_end_of_day_fill_reservations FROM "EndOfDayFillSendReservations";
SELECT 'passed' role_based_access_verification;
ROLLBACK;
