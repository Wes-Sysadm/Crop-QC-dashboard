\set ON_ERROR_STOP on

-- Read-only production preflight for
-- 20260731014107_SeparatePlanningProjectionsFromActualRuns.
-- This script reports fully missing, partially applied, and complete object state.
begin transaction read only;

select current_database() as database_name,
       current_setting('server_version') as postgresql_version,
       current_schema() as application_schema;

select "MigrationId", "ProductVersion"
from "__EFMigrationsHistory"
order by "MigrationId" desc
limit 10;

do $preflight$
declare
    missing_base text;
    duplicate_actual_run_packouts bigint := 0;
begin
    select string_agg(expected.name, ', ' order by expected.name)
    into missing_base
    from (values
        ('ActualRuns'),
        ('ActualRunRevisions'),
        ('BinsRunEntries'),
        ('PackoutRuns'),
        ('QcSamples'),
        ('Users')
    ) expected(name)
    where to_regclass(format('%I.%I', current_schema(), expected.name)) is null;

    if missing_base is not null then
        raise exception 'Required pre-existing table(s) are missing: %. No changes were attempted.', missing_base;
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = current_schema()
          and table_name = 'PackoutRuns'
          and column_name = 'ActualRunId'
    ) then
        execute '
            select count(*)
            from (
                select "ActualRunId"
                from "PackoutRuns"
                where "ActualRunId" is not null
                group by "ActualRunId"
                having count(*) > 1
            ) duplicates'
        into duplicate_actual_run_packouts;
    end if;

    if duplicate_actual_run_packouts > 0 then
        raise exception '% duplicate Actual Run packout relationship(s) require review before apply. No changes were attempted.',
            duplicate_actual_run_packouts;
    end if;
end
$preflight$;

with expected(display_name, table_name) as (
    values
        ('RunExpectations', 'RunExpectations'),
        ('RunExpectationSources', 'RunExpectationSources'),
        ('PackoutSourceAllocations', 'PackoutSourceAllocations')
)
select display_name,
       to_regclass(format('%I.%I', current_schema(), table_name)) is not null as present
from expected
order by display_name;

with expected(display_name, table_name, column_name, expected_nullable) as (
    values
        ('PackoutRuns.ActualRunId', 'PackoutRuns', 'ActualRunId', 'YES'),
        ('PackoutRuns.RunExpectationId', 'PackoutRuns', 'RunExpectationId', 'YES'),
        ('PackoutRuns.RunProjectionId', 'PackoutRuns', 'RunProjectionId', 'YES')
)
select expected.display_name,
       columns.column_name is not null as present,
       columns.is_nullable,
       expected.expected_nullable
from expected
left join information_schema.columns columns
  on columns.table_schema = current_schema()
 and columns.table_name = expected.table_name
 and columns.column_name = expected.column_name
order by expected.display_name;

with expected(table_name, index_name) as (
    values
        ('PackoutRuns', 'IX_PackoutRuns_RunExpectationId'),
        ('PackoutRuns', 'UX_PackoutRuns_ActualRunId'),
        ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId'),
        ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_RunExpectationSourceId'),
        ('RunExpectations', 'IX_RunExpectations_ActualRunId_RevisionNumber'),
        ('RunExpectations', 'IX_RunExpectations_ActualRunRevisionId'),
        ('RunExpectations', 'IX_RunExpectations_CreatedByUserId'),
        ('RunExpectationSources', 'IX_RunExpectationSources_BinsRunEntryId'),
        ('RunExpectationSources', 'IX_RunExpectationSources_QcSampleId'),
        ('RunExpectationSources', 'IX_RunExpectationSources_RunExpectationId_BinsRunEntryId'),
        ('RunExpectationSources', 'IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot')
)
select expected.index_name,
       indexes.indexname is not null as present
from expected
left join pg_indexes indexes
  on indexes.schemaname = current_schema()
 and indexes.tablename = expected.table_name
 and indexes.indexname = left(expected.index_name, 63)
order by expected.index_name;

with expected(table_name, constraint_name) as (
    values
        ('PackoutRuns', 'FK_PackoutRuns_ActualRuns_ActualRunId'),
        ('PackoutRuns', 'FK_PackoutRuns_RunExpectations_RunExpectationId'),
        ('RunExpectations', 'FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId'),
        ('RunExpectations', 'FK_RunExpectations_ActualRuns_ActualRunId'),
        ('RunExpectations', 'FK_RunExpectations_Users_CreatedByUserId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_QcSamples_QcSampleId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_RunExpectations_RunExpectationId'),
        ('PackoutSourceAllocations', 'FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId'),
        ('PackoutSourceAllocations', 'FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId')
)
select expected.constraint_name,
       constraints.conname is not null as present
from expected
left join pg_class tables
  on tables.relname = expected.table_name
 and tables.relnamespace = current_schema()::regnamespace
left join pg_constraint constraints
  on constraints.conrelid = tables.oid
 and constraints.conname = left(expected.constraint_name, 63)
 and constraints.contype = 'f'
order by expected.constraint_name;

select
    (select count(*) from "RunProjections") as planning_projections,
    (select count(*) from "ActualRuns") as actual_runs,
    (select count(*) from "PackoutRuns") as packout_results,
    (select count(*) from "BinsRunEntries") as bins_run_entries,
    (select count(*) from "RoomInventoryAdjustments") as room_adjustments;

rollback;
