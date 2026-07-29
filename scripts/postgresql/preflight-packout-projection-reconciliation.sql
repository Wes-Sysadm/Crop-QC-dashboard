\set ON_ERROR_STOP on

-- Read-only production preflight for
-- 20260729165910_AddPackoutProjectionReconciliation.
-- This script is safe for an EnsureCreated/compatibility database and never
-- changes migration history, schema, or application rows.

begin transaction read only;

select current_database() as database_name,
       current_setting('server_version') as postgresql_version,
       current_user as database_user,
       current_schema() as application_schema;

select case
           when to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is null
               then 'ABSENT'
           else 'PRESENT'
       end as migration_history_status;

do $preflight$
begin
    if to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is null then
        raise notice '__EFMigrationsHistory is absent (compatible with the historical EnsureCreated path).';
    else
        raise notice '__EFMigrationsHistory contents follow in the next result set.';
    end if;
end
$preflight$;

select 'select "MigrationId", "ProductVersion" from "__EFMigrationsHistory" order by "MigrationId";'
where to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is not null
\gexec

-- Required checkpoint objects used by the compatibility update.
with expected(object_kind, parent_name, object_name) as (
    values
        ('table', current_schema(), 'Users'),
        ('table', current_schema(), 'Grades'),
        ('table', current_schema(), 'QcSamples'),
        ('table', current_schema(), 'QcFruitReadings'),
        ('table', current_schema(), 'QcFruitDefects'),
        ('table', current_schema(), 'RunProjections'),
        ('table', current_schema(), 'RunProjectionSources'),
        ('table', current_schema(), 'RoomInventoryAdjustments'),
        ('table', current_schema(), 'BinsRunEntries'),
        ('column', 'Users', 'Id'),
        ('column', 'Grades', 'Id'),
        ('column', 'QcSamples', 'Id'),
        ('column', 'QcFruitReadings', 'Id'),
        ('column', 'QcFruitReadings', 'QcSampleId'),
        ('column', 'QcFruitDefects', 'QcFruitReadingId'),
        ('column', 'RunProjections', 'Id'),
        ('column', 'RunProjectionSources', 'Id'),
        ('column', 'BinsRunEntries', 'Id')
)
select object_kind,
       parent_name,
       object_name,
       case
           when object_kind = 'table' then to_regclass(format('%I.%I', current_schema(), object_name)) is not null
           else exists (
               select 1
               from information_schema.columns as column_info
               where column_info.table_schema = current_schema()
                 and column_info.table_name = parent_name
                 and column_info.column_name = object_name)
       end as exists
from expected
order by object_kind, parent_name, object_name;

select table_name,
       column_name,
       data_type,
       character_maximum_length,
       numeric_precision,
       numeric_scale,
       is_nullable,
       column_default,
       identity_generation
from information_schema.columns
where table_schema = current_schema()
  and (
      table_name in (
          'PackCodeDefinitions',
          'PackoutAnalysisConfigurations',
          'PackoutRuns',
          'PackoutEmailAttempts',
          'PackoutReportSources',
          'PackoutReportLines')
      or (table_name = 'RunProjectionSources' and column_name = 'TotalDefectPercentageSnapshot')
      or (table_name = 'RunProjections' and column_name in ('IsLocked', 'LockedAt', 'LockedByUserId'))
      or (table_name = 'QcSamples' and column_name = 'DefectInspectionStatus')
      or (table_name = 'BinsRunEntries' and column_name in ('IsReconciled', 'ReconciledAt', 'ReconciledByUserId')))
order by table_name, ordinal_position;

-- Additive objects introduced by PR #154.
with expected(object_kind, parent_name, object_name) as (
    values
        ('table', current_schema(), 'PackCodeDefinitions'),
        ('table', current_schema(), 'PackoutAnalysisConfigurations'),
        ('table', current_schema(), 'PackoutRuns'),
        ('table', current_schema(), 'PackoutEmailAttempts'),
        ('table', current_schema(), 'PackoutReportSources'),
        ('table', current_schema(), 'PackoutReportLines'),
        ('column', 'RunProjectionSources', 'TotalDefectPercentageSnapshot'),
        ('column', 'RunProjections', 'IsLocked'),
        ('column', 'RunProjections', 'LockedAt'),
        ('column', 'RunProjections', 'LockedByUserId'),
        ('column', 'QcSamples', 'DefectInspectionStatus'),
        ('column', 'BinsRunEntries', 'IsReconciled'),
        ('column', 'BinsRunEntries', 'ReconciledAt'),
        ('column', 'BinsRunEntries', 'ReconciledByUserId')
)
select object_kind,
       parent_name,
       object_name,
       case
           when object_kind = 'table' then to_regclass(format('%I.%I', current_schema(), object_name)) is not null
           else exists (
               select 1
               from information_schema.columns as column_info
               where column_info.table_schema = current_schema()
                 and column_info.table_name = parent_name
                 and column_info.column_name = object_name)
       end as exists
from expected
order by object_kind, parent_name, object_name;

with expected(index_name) as (
    values
        ('IX_RunProjections_LockedByUserId'),
        ('IX_BinsRunEntries_ReconciledByUserId'),
        ('IX_PackCodeDefinitions_CreatedByUserId'),
        ('IX_PackCodeDefinitions_GradeId'),
        ('IX_PackCodeDefinitions_IsActive_ProductCategory'),
        ('IX_PackCodeDefinitions_NormalizedCode'),
        ('IX_PackCodeDefinitions_UpdatedByUserId'),
        ('IX_PackoutAnalysisConfigurations_UpdatedByUserId'),
        ('IX_PackoutEmailAttempts_PackoutRunId_AttemptedAt'),
        ('IX_PackoutEmailAttempts_SenderUserId'),
        ('IX_PackoutReportLines_GradeId'),
        ('IX_PackoutReportLines_NormalizedPackCode'),
        ('IX_PackoutReportLines_PackCodeDefinitionId'),
        ('IX_PackoutReportLines_PackoutReportSourceId'),
        ('IX_PackoutReportLines_PackoutRunId_ProductCategory'),
        ('IX_PackoutReportLines_UpdatedByUserId'),
        ('IX_PackoutReportSources_PackoutRunId_Sha256'),
        ('IX_PackoutRuns_BinsRunEntryId'),
        ('IX_PackoutRuns_CreatedByUserId'),
        ('IX_PackoutRuns_FacilitySnapshot_PackingDate_RunNumber'),
        ('IX_PackoutRuns_FinalizedByUserId'),
        ('IX_PackoutRuns_ReopenedByUserId'),
        ('IX_PackoutRuns_RunProjectionId_Status'),
        ('IX_PackoutRuns_UpdatedByUserId')
)
select index_name,
       exists (
           select 1
           from pg_indexes
           where schemaname = current_schema()
             and indexname = expected.index_name) as exists
from expected
order by index_name;

with expected(table_name, constraint_name) as (
    values
        ('PackCodeDefinitions', 'PK_PackCodeDefinitions'),
        ('PackoutAnalysisConfigurations', 'PK_PackoutAnalysisConfigurations'),
        ('PackoutRuns', 'PK_PackoutRuns'),
        ('PackoutEmailAttempts', 'PK_PackoutEmailAttempts'),
        ('PackoutReportSources', 'PK_PackoutReportSources'),
        ('PackoutReportLines', 'PK_PackoutReportLines'),
        ('BinsRunEntries', 'FK_BinsRunEntries_Users_ReconciledByUserId'),
        ('RunProjections', 'FK_RunProjections_Users_LockedByUserId'),
        ('PackCodeDefinitions', 'FK_PackCodeDefinitions_Grades_GradeId'),
        ('PackCodeDefinitions', 'FK_PackCodeDefinitions_Users_CreatedByUserId'),
        ('PackCodeDefinitions', 'FK_PackCodeDefinitions_Users_UpdatedByUserId'),
        ('PackoutAnalysisConfigurations', 'FK_PackoutAnalysisConfigurations_Users_UpdatedByUserId'),
        ('PackoutRuns', 'FK_PackoutRuns_BinsRunEntries_BinsRunEntryId'),
        ('PackoutRuns', 'FK_PackoutRuns_RunProjections_RunProjectionId'),
        ('PackoutRuns', 'FK_PackoutRuns_Users_CreatedByUserId'),
        ('PackoutRuns', 'FK_PackoutRuns_Users_FinalizedByUserId'),
        ('PackoutRuns', 'FK_PackoutRuns_Users_ReopenedByUserId'),
        ('PackoutRuns', 'FK_PackoutRuns_Users_UpdatedByUserId'),
        ('PackoutEmailAttempts', 'FK_PackoutEmailAttempts_PackoutRuns_PackoutRunId'),
        ('PackoutEmailAttempts', 'FK_PackoutEmailAttempts_Users_SenderUserId'),
        ('PackoutReportSources', 'FK_PackoutReportSources_PackoutRuns_PackoutRunId'),
        ('PackoutReportLines', 'FK_PackoutReportLines_Grades_GradeId'),
        ('PackoutReportLines', 'FK_PackoutReportLines_PackCodeDefinitions_PackCodeDefinitionId'),
        ('PackoutReportLines', 'FK_PackoutReportLines_PackoutReportSources_PackoutReportSourceId'),
        ('PackoutReportLines', 'FK_PackoutReportLines_PackoutRuns_PackoutRunId'),
        ('PackoutReportLines', 'FK_PackoutReportLines_Users_UpdatedByUserId')
)
select table_name,
       constraint_name,
       exists (
           select 1
           from pg_constraint as constraint_info
           join pg_class as table_info on table_info.oid = constraint_info.conrelid
           join pg_namespace as schema_info on schema_info.oid = table_info.relnamespace
           where schema_info.nspname = current_schema()
             and table_info.relname = expected.table_name
             and constraint_info.conname = left(expected.constraint_name, 63)) as exists
from expected
order by table_name, constraint_name;

-- Historical sample counts that determine the deterministic status backfill.
select count(*) as qc_sample_count,
       count(*) filter (where exists (
           select 1
           from "QcFruitReadings" as reading
           join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
           where reading."QcSampleId" = sample."Id")) as samples_with_defects,
       count(*) filter (where not exists (
           select 1
           from "QcFruitReadings" as reading
           join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
           where reading."QcSampleId" = sample."Id")) as samples_without_defects
from "QcSamples" as sample;

-- Counts used to compare preservation before and after the restored-copy test.
select (select count(*) from "Receipts") as receipts,
       (select count(*) from "QcSamples") as qc_samples,
       (select count(*) from "QcFruitReadings") as qc_fruit_readings,
       (select count(*) from "QcFruitDefects") as qc_fruit_defects,
       (select count(*) from "RoomInventoryAdjustments") as room_inventory_adjustments,
       (select count(*) from "BinsRunEntries") as bins_run_entries,
       (select count(*) from "RunProjections") as run_projections,
       (select count(*) from "RunProjectionSources") as run_projection_sources;

do $preflight$
declare
    new_object_total integer := 14;
    new_object_present integer := 0;
    migration_recorded boolean := false;
    value bigint := 0;
begin
    if to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is not null then
        execute 'select exists (select 1 from "__EFMigrationsHistory" where "MigrationId" = $1)'
            into migration_recorded
            using '20260729165910_AddPackoutProjectionReconciliation';
    end if;

    select count(*)
    into new_object_present
    from (
        select to_regclass(format('%I.%I', current_schema(), object_name)) is not null as present
        from unnest(array[
            'PackCodeDefinitions',
            'PackoutAnalysisConfigurations',
            'PackoutRuns',
            'PackoutEmailAttempts',
            'PackoutReportSources',
            'PackoutReportLines'
        ]) as object_name
        union all
        select exists (
            select 1
            from information_schema.columns
            where table_schema = current_schema()
              and table_name = expected.table_name
              and column_name = expected.column_name)
        from (values
            ('RunProjectionSources', 'TotalDefectPercentageSnapshot'),
            ('RunProjections', 'IsLocked'),
            ('RunProjections', 'LockedAt'),
            ('RunProjections', 'LockedByUserId'),
            ('QcSamples', 'DefectInspectionStatus'),
            ('BinsRunEntries', 'IsReconciled'),
            ('BinsRunEntries', 'ReconciledAt'),
            ('BinsRunEntries', 'ReconciledByUserId')
        ) as expected(table_name, column_name)
    ) as object_state
    where object_state.present;

    raise notice 'Migration history records 20260729165910: %', migration_recorded;
    raise notice 'Schema classification: % (% of % core additive objects present).',
        case
            when new_object_present = 0 then 'NOT APPLIED'
            when new_object_present = new_object_total then 'FULLY APPLIED'
            else 'PARTIALLY APPLIED'
        end,
        new_object_present,
        new_object_total;

    if exists (
        select 1
        from information_schema.columns
        where table_schema = current_schema()
          and table_name = 'QcSamples'
          and column_name = 'DefectInspectionStatus') then
        execute '
            select count(*)
            from "QcSamples"
            where "DefectInspectionStatus" is null
               or "DefectInspectionStatus" not in (''No defects found'', ''Defects found'')'
            into value;
        raise notice 'QcSamples with null or invalid DefectInspectionStatus: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackCodeDefinitions')) is not null then
        execute '
            select count(*)
            from (
                select "NormalizedCode"
                from "PackCodeDefinitions"
                group by "NormalizedCode"
                having count(*) > 1
            ) as duplicates'
            into value;
        raise notice 'Duplicate normalized pack codes: %', value;

        execute '
            select count(*)
            from "PackCodeDefinitions"
            where btrim("Code") = ''''
               or btrim("NormalizedCode") = ''''
               or btrim("DisplayName") = ''''
               or btrim("ProductCategory") = '''''
            into value;
        raise notice 'Pack-code rows with blank required values: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutAnalysisConfigurations')) is not null then
        execute 'select count(*) from "PackoutAnalysisConfigurations" where "Id" = 1' into value;
        raise notice 'Default packout-analysis configuration rows with Id 1: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutRuns')) is not null then
        execute '
            select count(*)
            from (
                select "FacilitySnapshot", "PackingDate", "RunNumber"
                from "PackoutRuns"
                group by "FacilitySnapshot", "PackingDate", "RunNumber"
                having count(*) > 1
            ) as duplicates'
            into value;
        raise notice 'Duplicate packout run identities: %', value;

        execute '
            select count(*)
            from (
                select "BinsRunEntryId"
                from "PackoutRuns"
                where "BinsRunEntryId" is not null
                group by "BinsRunEntryId"
                having count(*) > 1
            ) as duplicates'
            into value;
        raise notice 'Duplicate non-null packout BinsRunEntry links: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutReportSources')) is not null then
        execute '
            select count(*)
            from (
                select "PackoutRunId", "Sha256"
                from "PackoutReportSources"
                group by "PackoutRunId", "Sha256"
                having count(*) > 1
            ) as duplicates'
            into value;
        raise notice 'Duplicate packout report source hashes per run: %', value;
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = current_schema()
          and table_name = 'RunProjections'
          and column_name = 'LockedByUserId') then
        execute '
            select count(*)
            from "RunProjections" as projection
            left join "Users" as app_user on app_user."Id" = projection."LockedByUserId"
            where projection."LockedByUserId" is not null
              and app_user."Id" is null'
            into value;
        raise notice 'Orphaned RunProjections.LockedByUserId values: %', value;
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = current_schema()
          and table_name = 'BinsRunEntries'
          and column_name = 'ReconciledByUserId') then
        execute '
            select count(*)
            from "BinsRunEntries" as entry
            left join "Users" as app_user on app_user."Id" = entry."ReconciledByUserId"
            where entry."ReconciledByUserId" is not null
              and app_user."Id" is null'
            into value;
        raise notice 'Orphaned BinsRunEntries.ReconciledByUserId values: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutRuns')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema()
             and table_name = 'PackoutRuns'
             and column_name in ('RunProjectionId', 'BinsRunEntryId')
           group by table_name
           having count(*) = 2) then
        execute '
            select count(*)
            from "PackoutRuns" as run
            left join "RunProjections" as projection on projection."Id" = run."RunProjectionId"
            left join "BinsRunEntries" as entry on entry."Id" = run."BinsRunEntryId"
            where projection."Id" is null
               or (run."BinsRunEntryId" is not null and entry."Id" is null)'
            into value;
        raise notice 'Orphaned PackoutRuns projection or Bins Run relationships: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutEmailAttempts')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema()
             and table_name = 'PackoutEmailAttempts'
             and column_name = 'PackoutRunId') then
        execute '
            select count(*)
            from "PackoutEmailAttempts" as attempt
            left join "PackoutRuns" as run on run."Id" = attempt."PackoutRunId"
            where run."Id" is null'
            into value;
        raise notice 'Orphaned PackoutEmailAttempts.PackoutRunId values: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutReportSources')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema()
             and table_name = 'PackoutReportSources'
             and column_name = 'PackoutRunId') then
        execute '
            select count(*)
            from "PackoutReportSources" as source
            left join "PackoutRuns" as run on run."Id" = source."PackoutRunId"
            where run."Id" is null'
            into value;
        raise notice 'Orphaned PackoutReportSources.PackoutRunId values: %', value;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutReportLines')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema()
             and table_name = 'PackoutReportLines'
             and column_name = 'PackoutRunId') then
        execute '
            select count(*)
            from "PackoutReportLines" as line
            left join "PackoutRuns" as run on run."Id" = line."PackoutRunId"
            where run."Id" is null'
            into value;
        raise notice 'Orphaned PackoutReportLines.PackoutRunId values: %', value;
    end if;
end
$preflight$;

rollback;
