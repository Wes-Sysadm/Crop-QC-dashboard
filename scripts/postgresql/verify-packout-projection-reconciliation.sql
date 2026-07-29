\set ON_ERROR_STOP on

-- Read-only verification for the production compatibility update associated with
-- 20260729165910_AddPackoutProjectionReconciliation.

begin transaction read only;

select current_database() as database_name,
       current_setting('server_version') as postgresql_version,
       current_user as database_user,
       current_schema() as application_schema;

do $verification$
declare
    missing_objects text;
    missing_indexes text;
    missing_constraints text;
    invalid_count bigint;
begin
    select string_agg(expected.safe_name, ', ')
    into missing_objects
    from (values
        ('PackCodeDefinitions', 'PackCodeDefinitions', null::text),
        ('PackoutAnalysisConfigurations', 'PackoutAnalysisConfigurations', null::text),
        ('PackoutRuns', 'PackoutRuns', null::text),
        ('PackoutEmailAttempts', 'PackoutEmailAttempts', null::text),
        ('PackoutReportSources', 'PackoutReportSources', null::text),
        ('PackoutReportLines', 'PackoutReportLines', null::text),
        ('RunProjectionSources.TotalDefectPercentageSnapshot', 'RunProjectionSources', 'TotalDefectPercentageSnapshot'),
        ('RunProjections.IsLocked', 'RunProjections', 'IsLocked'),
        ('RunProjections.LockedAt', 'RunProjections', 'LockedAt'),
        ('RunProjections.LockedByUserId', 'RunProjections', 'LockedByUserId'),
        ('QcSamples.DefectInspectionStatus', 'QcSamples', 'DefectInspectionStatus'),
        ('BinsRunEntries.IsReconciled', 'BinsRunEntries', 'IsReconciled'),
        ('BinsRunEntries.ReconciledAt', 'BinsRunEntries', 'ReconciledAt'),
        ('BinsRunEntries.ReconciledByUserId', 'BinsRunEntries', 'ReconciledByUserId')
    ) as expected(safe_name, table_name, column_name)
    where (expected.column_name is null
           and to_regclass(format('%I.%I', current_schema(), expected.table_name)) is null)
       or (expected.column_name is not null
           and not exists (
               select 1 from information_schema.columns
               where table_schema = current_schema()
                 and table_name = expected.table_name
                 and column_name = expected.column_name));

    if missing_objects is not null then
        raise exception 'Required schema objects are missing: %.', missing_objects;
    end if;

    select string_agg(expected.index_name, ', ')
    into missing_indexes
    from (values
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
    ) as expected(index_name)
    where not exists (
        select 1
        from pg_indexes
        where schemaname = current_schema()
          and indexname = expected.index_name);

    if missing_indexes is not null then
        raise exception 'Required indexes are missing: %.', missing_indexes;
    end if;

    select string_agg(format('%s.%s', expected.table_name, expected.constraint_name), ', ')
    into missing_constraints
    from (values
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
    ) as expected(table_name, constraint_name)
    where not exists (
        select 1
        from pg_constraint as constraint_info
        join pg_class as table_info on table_info.oid = constraint_info.conrelid
        join pg_namespace as schema_info on schema_info.oid = table_info.relnamespace
        where schema_info.nspname = current_schema()
          and table_info.relname = expected.table_name
          and constraint_info.conname = left(expected.constraint_name, 63));

    if missing_constraints is not null then
        raise exception 'Required constraints are missing: %.', missing_constraints;
    end if;

    select count(*)
    into invalid_count
    from "QcSamples" as sample
    where sample."DefectInspectionStatus" is null
       or sample."DefectInspectionStatus" not in ('No defects found', 'Defects found')
       or sample."DefectInspectionStatus" is distinct from
          case when exists (
              select 1
              from "QcFruitReadings" as reading
              join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
              where reading."QcSampleId" = sample."Id")
          then 'Defects found'
          else 'No defects found'
          end;

    if invalid_count > 0 then
        raise exception '% QcSamples have an invalid or inconsistent defect inspection status.', invalid_count;
    end if;

    if (select count(*) from "PackoutAnalysisConfigurations" where "Id" = 1) <> 1 then
        raise exception 'Default packout-analysis configuration Id 1 does not exist exactly once.';
    end if;

    if exists (
        select 1
        from "PackCodeDefinitions"
        where btrim("Code") = ''
           or btrim("NormalizedCode") = ''
           or btrim("DisplayName") = ''
           or btrim("ProductCategory") = '') then
        raise exception 'PackCodeDefinitions contains blank required values.';
    end if;

    if exists (
        select 1
        from "PackCodeDefinitions"
        group by "NormalizedCode"
        having count(*) > 1) then
        raise exception 'PackCodeDefinitions contains duplicate normalized codes.';
    end if;

    if exists (
        select 1
        from "PackoutRuns"
        group by "FacilitySnapshot", "PackingDate", "RunNumber"
        having count(*) > 1) then
        raise exception 'PackoutRuns contains duplicate facility/date/run identities.';
    end if;

    if exists (
        select 1
        from "PackoutReportSources"
        group by "PackoutRunId", "Sha256"
        having count(*) > 1) then
        raise exception 'PackoutReportSources contains duplicate run/hash identities.';
    end if;

    if exists (
        select 1 from "RunProjections" as projection
        left join "Users" as app_user on app_user."Id" = projection."LockedByUserId"
        where projection."LockedByUserId" is not null and app_user."Id" is null)
       or exists (
        select 1 from "BinsRunEntries" as entry
        left join "Users" as app_user on app_user."Id" = entry."ReconciledByUserId"
        where entry."ReconciledByUserId" is not null and app_user."Id" is null)
       or exists (
        select 1 from "PackoutRuns" as run
        left join "RunProjections" as projection on projection."Id" = run."RunProjectionId"
        where projection."Id" is null)
       or exists (
        select 1 from "PackoutReportSources" as source
        left join "PackoutRuns" as run on run."Id" = source."PackoutRunId"
        where run."Id" is null)
       or exists (
        select 1 from "PackoutReportLines" as line
        left join "PackoutRuns" as run on run."Id" = line."PackoutRunId"
        where run."Id" is null)
       or exists (
        select 1 from "PackoutEmailAttempts" as attempt
        left join "PackoutRuns" as run on run."Id" = attempt."PackoutRunId"
        where run."Id" is null) then
        raise exception 'Orphaned packout reconciliation relationships were found.';
    end if;
end
$verification$;

select count(*) as qc_sample_count,
       count(*) filter (where "DefectInspectionStatus" = 'Defects found') as defects_found_count,
       count(*) filter (where "DefectInspectionStatus" = 'No defects found') as no_defects_found_count
from "QcSamples";

select (select count(*) from "Receipts") as receipts,
       (select count(*) from "QcSamples") as qc_samples,
       (select count(*) from "QcFruitReadings") as qc_fruit_readings,
       (select count(*) from "QcFruitDefects") as qc_fruit_defects,
       (select count(*) from "RoomInventoryAdjustments") as room_inventory_adjustments,
       (select count(*) from "BinsRunEntries") as bins_run_entries,
       (select count(*) from "RunProjections") as run_projections,
       (select count(*) from "RunProjectionSources") as run_projection_sources,
       (select count(*) from "PackoutRuns") as packout_runs,
       (select count(*) from "PackoutReportSources") as packout_report_sources,
       (select count(*) from "PackoutReportLines") as packout_report_lines,
       (select count(*) from "PackoutEmailAttempts") as packout_email_attempts;

-- Compile and execute representative zero-row reads used by QC, Dashboard,
-- Bins Run, projections, and packout reconciliation without returning row data.
select sample."Id", sample."DefectInspectionStatus"
from "QcSamples" as sample
where false;

select entry."Id", entry."IsReconciled", entry."ReconciledAt", entry."ReconciledByUserId"
from "BinsRunEntries" as entry
where false;

select projection."Id", projection."IsLocked", projection."LockedAt", projection."LockedByUserId",
       source."TotalDefectPercentageSnapshot"
from "RunProjections" as projection
left join "RunProjectionSources" as source on source."RunProjectionId" = projection."Id"
where false;

select run."Id", run."Status", source."Sha256", line."ProductCategory", attempt."Succeeded",
       configuration."SizeScoreWeight", definition."NormalizedCode"
from "PackoutRuns" as run
left join "PackoutReportSources" as source on source."PackoutRunId" = run."Id"
left join "PackoutReportLines" as line on line."PackoutRunId" = run."Id"
left join "PackoutEmailAttempts" as attempt on attempt."PackoutRunId" = run."Id"
cross join "PackoutAnalysisConfigurations" as configuration
left join "PackCodeDefinitions" as definition on definition."Id" = line."PackCodeDefinitionId"
where false;

do $history$
declare
    recorded boolean := false;
begin
    if to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is null then
        raise notice 'Migration history state: ABSENT (expected on historical EnsureCreated databases).';
    else
        execute '
            select exists (
                select 1
                from "__EFMigrationsHistory"
                where "MigrationId" = $1)'
            into recorded
            using '20260729165910_AddPackoutProjectionReconciliation';
        raise notice 'Migration history state: %.',
            case
                when recorded then 'RECORDED'
                else 'NOT RECORDED (compatibility script does not forge migration history)'
            end;
    end if;
end
$history$;

rollback;
