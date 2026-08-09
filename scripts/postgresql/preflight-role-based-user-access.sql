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

DO $objects$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'Users')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'Roles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserRoles')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'UserPageAccesses')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'EndOfDayFillReportGroups')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'EndOfDayFillReportRecipients')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'EndOfDayFillUserGroupAssignments')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'EndOfDayFillReportSends')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'EndOfDayFillSendReservations')) IS NULL THEN
        RAISE EXCEPTION 'A required production object is missing. No changes were made.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='EndOfDayFillReportGroupId') THEN
        RAISE EXCEPTION 'The End-of-Day Fill Room assignment column is missing. No changes were made.';
    END IF;
    IF (to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL) IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='IsActive')
       OR (to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL) IS DISTINCT FROM EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Roles' AND column_name='NormalizedName') THEN
        RAISE EXCEPTION 'Partial role-based access schema detected. No changes were made.';
    END IF;
END $objects$;

DO $reviewed_end_of_day_fill_state$
BEGIN
    IF (SELECT count(*) FROM "EndOfDayFillReportGroups")<>2
       OR (SELECT count(*) FROM "EndOfDayFillReportGroups" WHERE "IsActive" AND "Facility"='WP')<>1
       OR (SELECT count(*) FROM "EndOfDayFillReportGroups" WHERE "IsActive" AND "Facility"='EBS')<>1
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP')<>42
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='EBS')<>27
       OR (SELECT count(*) FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility"='WP' AND upper(r."Code") IN ('MCD-01','WP-4','WP-5','WP-6','WP-7','WP-8'))<>6
       OR (SELECT count(*) FROM "EndOfDayFillReportRecipients")<>3
       OR (SELECT count(*) FROM "EndOfDayFillUserGroupAssignments")<>4
       OR (SELECT count(*) FROM "EndOfDayFillReportSends")<>2
       OR (SELECT count(*) FROM "EndOfDayFillSendReservations")<>0
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportGroups") x)<>'4ed24ac0ab6c6ce1525799c5b427ad0d'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportRecipients") x)<>'25450543a5e2af47d5a3642fbd33983c'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillUserGroupAssignments") x)<>'74c7bf40bfd8e94da81f6756d4c55de1'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x)<>'a168b825cd73c3104b6648bff75a138c'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) FROM (SELECT * FROM "EndOfDayFillReportSends") x)<>'7c63ed8bee27486fefc73ad9da734a93'
       OR (SELECT md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."ReportGroupId"),'')) FROM (SELECT * FROM "EndOfDayFillSendReservations") x)<>'d41d8cd98f00b204e9800998ecf8427e' THEN
        RAISE EXCEPTION 'End-of-Day Fill state no longer matches verified backup run 54. Regenerate the role package before DDL.';
    END IF;
END $reviewed_end_of_day_fill_state$;

WITH areas(area_key,legacy_area_key) AS (VALUES
 ('dashboard',NULL),('daily-qc',NULL),('field-samples',NULL),('qc-reports','daily-qc'),('receipts',NULL),('current-lots',NULL),('bins-run',NULL),
 ('projection-planner','bins-run'),('projection-outcome','bins-run'),('actual-runs','bins-run'),('packout-results','projection-outcome'),
 ('historical-inventory-cleanup','data-cleanup'),('rooms',NULL),('room-transactions',NULL),('transfers','room-transactions'),('true-up','room-transactions'),
 ('inventory','current-lots'),('grower-lots',NULL),('crop-year-review',NULL),('master-data',NULL),('users',NULL),('permission-matrix','users'),
 ('qc-stations',NULL),('downloads',NULL),('configuration',NULL),('variety-colors',NULL),('backups',NULL),('orchard-recipients','configuration'),
 ('orchard-managers','configuration'),('facilities','master-data'),('varieties','master-data'),('grades','master-data'),('defects','master-data'),
 ('size-configuration','master-data'),('email-configuration','configuration'),('backup-history','backups'),('audit-history','master-data'),
 ('import-tools','master-data'),('export-tools','master-data'),('data-cleanup',NULL)
), mapping(email,display_name,target_role,expected_fingerprint) AS (VALUES
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
 ('wes@fruitandland.com','Wes Cusick','Admin','85f8d6a96a71434b59e3dfdc5ef6318e')
), effective AS (
 SELECT lower(btrim(u."Email")) email,u."DisplayName" display_name,a.area_key,
 CASE WHEN lower(btrim(u."Email"))='wes@fruitandland.com' THEN 'Admin'
      WHEN NOT u."IsActive" THEN 'None'
      WHEN a.area_key='data-cleanup' AND NOT (lower(btrim(u."Email"))=ANY(regexp_split_to_array(lower(:'data_cleanup_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN a.area_key='crop-year-review' AND NOT (lower(btrim(u."Email"))=ANY(regexp_split_to_array(lower(:'crop_year_review_allowed_emails'),'\s*,\s*'))) THEN 'None'
      WHEN lower(coalesce(master."AccessLevel",'None'))='admin' THEN 'Admin'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None'))='edit' THEN 'Create'
      WHEN lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')) IN ('none','view','create','admin') THEN initcap(lower(coalesce(direct."AccessLevel",legacy."AccessLevel",'None')))
      ELSE 'None' END access_level
 FROM "Users" u CROSS JOIN areas a
 LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
 LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
 LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
 WHERE u."IsActive"
), fingerprints AS (
 SELECT email,display_name,md5(string_agg(area_key||'='||access_level,'|' ORDER BY area_key)) fingerprint,count(*) area_count,
 count(*) FILTER (WHERE access_level='Admin') admin_count,count(*) FILTER (WHERE access_level='Create') create_count,
 count(*) FILTER (WHERE access_level='View') view_count,count(*) FILTER (WHERE access_level='None') none_count
 FROM effective GROUP BY email,display_name
), validity AS (
 SELECT (SELECT count(*) FROM "Users" WHERE "IsActive")=12
   AND (SELECT count(*) FROM mapping)=12
   AND NOT EXISTS (SELECT 1 FROM mapping m FULL JOIN fingerprints f USING(email) WHERE m.email IS NULL OR f.email IS NULL OR m.display_name<>f.display_name OR f.area_count<>40 OR m.expected_fingerprint<>f.fingerprint)
   AND (SELECT count(DISTINCT fingerprint) FROM fingerprints WHERE email IN ('alexis@wp-packing.com','james@fruitandland.com','jorge@wp-packing.com'))=1
   AND (to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NOT NULL OR ((SELECT count(*) FROM "Roles")=4 AND NOT EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name")) NOT IN ('admin','manager','qc user','viewer')))) ok
), guard AS (SELECT 1/(CASE WHEN ok THEN 1 ELSE 0 END) reviewed_guard FROM validity)
SELECT m.email,f.display_name,m.target_role,f.fingerprint,f.admin_count,f.create_count,f.view_count,f.none_count,g.reviewed_guard
FROM mapping m JOIN fingerprints f USING(email) CROSS JOIN guard g ORDER BY m.target_role,m.email;

SELECT g."Name",g."Facility",g."IsActive",count(r."Id") AS assigned_rooms FROM "EndOfDayFillReportGroups" g LEFT JOIN "Rooms" r ON r."EndOfDayFillReportGroupId"=g."Id" GROUP BY g."Id",g."Name",g."Facility",g."IsActive" ORDER BY g."Facility";
SELECT lower("EmailAddress") email,"IsActive","SortOrder" FROM "EndOfDayFillReportRecipients" ORDER BY "SortOrder";
SELECT lower(u."Email") email,string_agg(g."Facility",',' ORDER BY g."Facility") report_groups FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId" GROUP BY u."Id",u."Email" ORDER BY lower(u."Email");
SELECT count(*) room_rows,md5(coalesce(string_agg(row_to_json(x)::text,E'\n' ORDER BY x."Id"),'')) room_assignment_capacity_fingerprint FROM (SELECT "Id","CapacityBins","EndOfDayFillReportGroupId" FROM "Rooms") x;
SELECT count(*) legacy_user_page_access_rows FROM "UserPageAccesses";
SELECT count(*) end_of_day_fill_sends FROM "EndOfDayFillReportSends";
SELECT count(*) end_of_day_fill_reservations FROM "EndOfDayFillSendReservations";
SELECT 'passed: exact reviewed fingerprints; five deterministic imported roles preserve unresolved access' role_based_access_preflight_status,
       CASE WHEN to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NULL THEN 'absent' ELSE 'present' END target_state;
ROLLBACK;
