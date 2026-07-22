\set ON_ERROR_STOP on

-- Read-only production preflight for 20260721233604_AddOrchardReportRecipients.
-- This script is read-only and does not change data.

select current_database() as database_name,
       current_setting('server_version') as server_version,
       current_user as database_user;

select expected.object_kind,
       expected.parent_name,
       expected.object_name,
       case
           when expected.object_kind = 'table' then exists (
               select 1
               from information_schema.tables
               where table_schema = current_schema()
                 and table_name = expected.object_name)
           else exists (
               select 1
               from information_schema.columns
               where table_schema = current_schema()
                 and table_name = expected.parent_name
                 and column_name = expected.object_name)
       end as exists
from (values
    ('table', current_schema(), '__EFMigrationsHistory'),
    ('table', current_schema(), 'CanonicalOrchards'),
    ('table', current_schema(), 'OrchardReportRecipients'),
    ('column', 'Receipts', 'CanonicalOrchardBlockId'),
    ('column', 'CanonicalOrchardBlocks', 'CanonicalOrchardId')
) as expected(object_kind, parent_name, object_name)
order by expected.object_kind, expected.parent_name, expected.object_name;

select "NormalizedOrchardKey", count(*) as block_count, min("OrchardName") as selected_orchard_name
from "CanonicalOrchardBlocks"
group by "NormalizedOrchardKey"
order by "NormalizedOrchardKey";

select count(*) as blank_normalized_orchard_keys
from "CanonicalOrchardBlocks"
where "NormalizedOrchardKey" is null or btrim("NormalizedOrchardKey") = '';

do $$
declare
    migration_applied boolean := false;
    duplicate_parent_keys bigint := 0;
    duplicate_active_recipients bigint := 0;
begin
    if to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is not null then
        execute 'select exists (select 1 from "__EFMigrationsHistory" where "MigrationId" = $1)'
            into migration_applied
            using '20260721233604_AddOrchardReportRecipients';
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'CanonicalOrchards')) is not null then
        execute 'select count(*) from (select "NormalizedOrchardKey" from "CanonicalOrchards" group by "NormalizedOrchardKey" having count(*) > 1) duplicates'
            into duplicate_parent_keys;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'OrchardReportRecipients')) is not null then
        execute 'select count(*) from (select "CanonicalOrchardId", "NormalizedEmailAddress" from "OrchardReportRecipients" where not "IsDeleted" group by "CanonicalOrchardId", "NormalizedEmailAddress" having count(*) > 1) duplicates'
            into duplicate_active_recipients;
    end if;

    raise notice 'Migration 20260721233604 applied: %', migration_applied;
    raise notice 'Duplicate CanonicalOrchards normalized keys: %', duplicate_parent_keys;
    raise notice 'Duplicate active orchard recipient values: %', duplicate_active_recipients;
end $$;
