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

DO $verify$
DECLARE
    expected_areas constant integer := 40;
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RolePageAccesses')) IS NULL THEN
        RAISE EXCEPTION 'RolePageAccesses is missing.';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='Roles'
          AND column_name IN ('IsActive','NormalizedName')) <> 2 THEN
        RAISE EXCEPTION 'Required Role columns are missing.';
    END IF;
    IF (SELECT count(*) FROM "Roles"
        WHERE "IsSystemRole" AND "IsActive"
          AND "Name" IN ('Viewer','QC Tech','QC Admin','Manager','Admin')) <> 5 THEN
        RAISE EXCEPTION 'The five active built-in roles are not present exactly once.';
    END IF;
    IF EXISTS (SELECT 1 FROM "Roles" WHERE lower(btrim("Name"))='qc user') THEN
        RAISE EXCEPTION 'Legacy QC User role remains.';
    END IF;
    IF EXISTS (SELECT 1 FROM "Roles" GROUP BY "NormalizedName" HAVING count(*)<>1) THEN
        RAISE EXCEPTION 'Normalized role names are not unique.';
    END IF;
    IF EXISTS (SELECT 1 FROM "Users" u LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
               WHERE u."IsActive" GROUP BY u."Id" HAVING count(ur."RoleId")<>1) THEN
        RAISE EXCEPTION 'An active user does not have exactly one role.';
    END IF;
    IF EXISTS (SELECT 1 FROM "UserRoles" GROUP BY "UserId" HAVING count(*)<>1) THEN
        RAISE EXCEPTION 'A user has multiple role assignments.';
    END IF;
    IF EXISTS (SELECT 1 FROM "RolePageAccesses" WHERE "AccessLevel" NOT IN ('None','View','Create','Admin')) THEN
        RAISE EXCEPTION 'Invalid access level found.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "Roles" r LEFT JOIN "RolePageAccesses" a ON a."RoleId"=r."Id"
        WHERE r."IsActive" GROUP BY r."Id" HAVING count(a."Id")<>expected_areas) THEN
        RAISE EXCEPTION 'An active role does not have a complete 40-area matrix.';
    END IF;
    IF (SELECT count(*) FROM "RolePageAccesses" a JOIN "Roles" r ON r."Id"=a."RoleId"
        WHERE r."Name"='Admin' AND a."AccessLevel"='Admin') <> expected_areas THEN
        RAISE EXCEPTION 'Admin is not full access for all 40 areas.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname=current_schema()
                   AND tablename='UserRoles' AND indexname='IX_UserRoles_UserId' AND indexdef ILIKE '%UNIQUE%') THEN
        RAISE EXCEPTION 'Unique one-role-per-user index is missing.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname=current_schema()
                   AND tablename='Roles' AND indexname='IX_Roles_NormalizedName' AND indexdef ILIKE '%UNIQUE%')
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname=current_schema()
                   AND tablename='RolePageAccesses' AND indexname='IX_RolePageAccesses_RoleId_AreaKey' AND indexdef ILIKE '%UNIQUE%') THEN
        RAISE EXCEPTION 'A required role-access unique index is missing.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
        AND conname IN ('PK_RolePageAccesses','FK_RolePageAccesses_Roles_RoleId',
                        'FK_RolePageAccesses_Users_UpdatedByUserId','CK_RolePageAccesses_AccessLevel')) <> 4 THEN
        RAISE EXCEPTION 'A required RolePageAccesses key, foreign key, or check constraint is missing.';
    END IF;
END $verify$;

WITH areas(area_key,legacy_area_key) AS (VALUES
 ('dashboard',NULL),('daily-qc',NULL),('field-samples',NULL),('qc-reports','daily-qc'),('receipts',NULL),('current-lots',NULL),('bins-run',NULL),
 ('projection-planner','bins-run'),('projection-outcome','bins-run'),('actual-runs','bins-run'),('packout-results','projection-outcome'),
 ('historical-inventory-cleanup','data-cleanup'),('rooms',NULL),('room-transactions',NULL),('transfers','room-transactions'),('true-up','room-transactions'),
 ('inventory','current-lots'),('grower-lots',NULL),('crop-year-review',NULL),('master-data',NULL),('users',NULL),('permission-matrix','users'),
 ('qc-stations',NULL),('downloads',NULL),('configuration',NULL),('variety-colors',NULL),('backups',NULL),('orchard-recipients','configuration'),
 ('orchard-managers','configuration'),('facilities','master-data'),('varieties','master-data'),('grades','master-data'),('defects','master-data'),
 ('size-configuration','master-data'),('email-configuration','configuration'),('backup-history','backups'),('audit-history','master-data'),
 ('import-tools','master-data'),('export-tools','master-data'),('data-cleanup',NULL)
), comparison AS (
 SELECT lower(btrim(u."Email")) email,r."Name" role_name,a.area_key,
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
       ELSE 'None' END old_effective_level,
  CASE WHEN lower(btrim(u."Email"))='wes@fruitandland.com' THEN 'Admin'
       WHEN NOT u."IsActive" OR NOT r."IsActive" THEN 'None'
       WHEN r."Name"='Admin' THEN 'Admin'
       ELSE coalesce(role_direct."AccessLevel",role_legacy."AccessLevel",'None') END new_effective_level
 FROM "Users" u
 JOIN "UserRoles" ur ON ur."UserId"=u."Id"
 JOIN "Roles" r ON r."Id"=ur."RoleId"
 CROSS JOIN areas a
 LEFT JOIN "UserPageAccesses" direct ON direct."UserId"=u."Id" AND lower(direct."AreaKey")=a.area_key
 LEFT JOIN "UserPageAccesses" legacy ON legacy."UserId"=u."Id" AND a.legacy_area_key IS NOT NULL AND lower(legacy."AreaKey")=a.legacy_area_key
 LEFT JOIN "UserPageAccesses" master ON master."UserId"=u."Id" AND lower(master."AreaKey")='master-data'
 LEFT JOIN "RolePageAccesses" role_direct ON role_direct."RoleId"=r."Id" AND lower(role_direct."AreaKey")=a.area_key
 LEFT JOIN "RolePageAccesses" role_legacy ON role_legacy."RoleId"=r."Id" AND a.legacy_area_key IS NOT NULL AND lower(role_legacy."AreaKey")=a.legacy_area_key
)
SELECT email,role_name,area_key,old_effective_level,new_effective_level,disposition
FROM (
 SELECT *,CASE WHEN role_name='Admin' AND new_effective_level='Admin'
               THEN 'expected Admin full-access normalization' ELSE 'BLOCKING unexpected difference' END disposition
 FROM comparison WHERE old_effective_level<>new_effective_level
) differences
ORDER BY email,area_key;

SELECT r."Id",r."Name",r."IsSystemRole",r."IsActive",count(DISTINCT ur."UserId") AS assigned_users,
       count(DISTINCT a."Id") AS matrix_cells,
       count(DISTINCT a."Id") FILTER (WHERE a."AccessLevel"='Admin') AS admin_cells,
       count(DISTINCT a."Id") FILTER (WHERE a."AccessLevel"='Create') AS create_cells,
       count(DISTINCT a."Id") FILTER (WHERE a."AccessLevel"='View') AS view_cells,
       count(DISTINCT a."Id") FILTER (WHERE a."AccessLevel"='None') AS none_cells
FROM "Roles" r
LEFT JOIN "UserRoles" ur ON ur."RoleId"=r."Id"
LEFT JOIN "RolePageAccesses" a ON a."RoleId"=r."Id"
GROUP BY r."Id",r."Name",r."IsSystemRole",r."IsActive"
ORDER BY lower(r."Name"),r."Id";

SELECT u."Id",lower(btrim(u."Email")) AS email,u."DisplayName",u."IsActive",r."Name" AS role
FROM "Users" u
LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
LEFT JOIN "Roles" r ON r."Id"=ur."RoleId"
ORDER BY lower(u."Email"),u."Id";

SELECT count(*) AS preserved_legacy_user_page_access_rows FROM "UserPageAccesses";
SELECT 'passed' AS role_based_access_verification;
ROLLBACK;
