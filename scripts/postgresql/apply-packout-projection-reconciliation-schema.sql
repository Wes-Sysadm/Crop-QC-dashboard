\set ON_ERROR_STOP on

-- Explicit PostgreSQL compatibility update for databases created through either
-- EF migrations or the historical EnsureCreated/compatibility path.
--
-- Production use requires:
--   * a fresh verified production backup,
--   * reviewed preflight output,
--   * a successful restored-copy rehearsal, and
--   * explicit production authorization.
--
-- This script does not change __EFMigrationsHistory and never deletes operational
-- rows. It is transactional, idempotent, and safe to rerun after success.

begin;
set local lock_timeout = '15s';
set local statement_timeout = '10min';

select pg_advisory_xact_lock(hashtextextended(
    'CropQc:20260729165910_AddPackoutProjectionReconciliation',
    0));

do $precheck$
declare
    required_table text;
    missing_required text;
    row_count bigint;
    value bigint;
begin
    foreach required_table in array array[
        'Users',
        'Grades',
        'QcSamples',
        'QcFruitReadings',
        'QcFruitDefects',
        'RunProjections',
        'RunProjectionSources',
        'RoomInventoryAdjustments',
        'BinsRunEntries'
    ]
    loop
        if to_regclass(format('%I.%I', current_schema(), required_table)) is null then
            raise exception 'Required pre-update table % is missing; transaction rolled back.', required_table;
        end if;
    end loop;

    select string_agg(format('%I.%I', expected.table_name, expected.column_name), ', ')
    into missing_required
    from (values
        ('Users', 'Id'),
        ('Grades', 'Id'),
        ('QcSamples', 'Id'),
        ('QcFruitReadings', 'Id'),
        ('QcFruitReadings', 'QcSampleId'),
        ('QcFruitDefects', 'QcFruitReadingId'),
        ('RunProjections', 'Id'),
        ('RunProjectionSources', 'Id'),
        ('RoomInventoryAdjustments', 'Id'),
        ('BinsRunEntries', 'Id')
    ) as expected(table_name, column_name)
    where not exists (
        select 1
        from information_schema.columns
        where table_schema = current_schema()
          and table_name = expected.table_name
          and column_name = expected.column_name);

    if missing_required is not null then
        raise exception 'Required pre-update columns are missing: %; transaction rolled back.', missing_required;
    end if;

    -- An incomplete new table can be repaired automatically only while it is empty,
    -- or when all required non-null business columns are already present.
    for required_table, missing_required in
        select required.table_name,
               string_agg(required.column_name, ', ')
        from (values
            ('PackCodeDefinitions', 'Id'),
            ('PackCodeDefinitions', 'Code'),
            ('PackCodeDefinitions', 'NormalizedCode'),
            ('PackCodeDefinitions', 'DisplayName'),
            ('PackCodeDefinitions', 'ProductCategory'),
            ('PackCodeDefinitions', 'IsActive'),
            ('PackCodeDefinitions', 'CreatedAt'),
            ('PackoutAnalysisConfigurations', 'Id'),
            ('PackoutAnalysisConfigurations', 'AppleBinWeightPounds'),
            ('PackoutAnalysisConfigurations', 'PearBinWeightPounds'),
            ('PackoutAnalysisConfigurations', 'SizeScoreWeight'),
            ('PackoutAnalysisConfigurations', 'GradeScoreWeight'),
            ('PackoutAnalysisConfigurations', 'PackoutScoreWeight'),
            ('PackoutAnalysisConfigurations', 'JuiceScoreWeight'),
            ('PackoutAnalysisConfigurations', 'PeelerSlicerScoreWeight'),
            ('PackoutAnalysisConfigurations', 'WasteScoreWeight'),
            ('PackoutAnalysisConfigurations', 'CurrentCropYearHistoryWeight'),
            ('PackoutAnalysisConfigurations', 'PriorCropYearHistoryWeight'),
            ('PackoutAnalysisConfigurations', 'UpdatedAt'),
            ('PackoutRuns', 'Id'),
            ('PackoutRuns', 'RunProjectionId'),
            ('PackoutRuns', 'Status'),
            ('PackoutRuns', 'FacilitySnapshot'),
            ('PackoutRuns', 'PackingDate'),
            ('PackoutRuns', 'RunNumber'),
            ('PackoutRuns', 'LotNumberSnapshot'),
            ('PackoutRuns', 'VarietySnapshot'),
            ('PackoutRuns', 'IsOrganicSnapshot'),
            ('PackoutRuns', 'CropYearSnapshot'),
            ('PackoutRuns', 'DumpedBins'),
            ('PackoutRuns', 'PoundsPerBin'),
            ('PackoutRuns', 'DumpedPounds'),
            ('PackoutRuns', 'PackedProductPounds'),
            ('PackoutRuns', 'JuicePounds'),
            ('PackoutRuns', 'PeelerSlicerPounds'),
            ('PackoutRuns', 'WastePounds'),
            ('PackoutRuns', 'ReconciliationDifferencePounds'),
            ('PackoutRuns', 'HasReconciliationWarning'),
            ('PackoutRuns', 'ConcurrencyVersion'),
            ('PackoutRuns', 'CreatedAt'),
            ('PackoutRuns', 'UpdatedAt'),
            ('PackoutEmailAttempts', 'Id'),
            ('PackoutEmailAttempts', 'PackoutRunId'),
            ('PackoutEmailAttempts', 'Recipient'),
            ('PackoutEmailAttempts', 'AttemptedAt'),
            ('PackoutEmailAttempts', 'Succeeded'),
            ('PackoutEmailAttempts', 'IsUpdatedAnalysis'),
            ('PackoutReportSources', 'Id'),
            ('PackoutReportSources', 'PackoutRunId'),
            ('PackoutReportSources', 'OriginalFileName'),
            ('PackoutReportSources', 'ContentType'),
            ('PackoutReportSources', 'FileSizeBytes'),
            ('PackoutReportSources', 'Sha256'),
            ('PackoutReportSources', 'ParserName'),
            ('PackoutReportSources', 'ParsedAt'),
            ('PackoutReportLines', 'Id'),
            ('PackoutReportLines', 'PackoutRunId'),
            ('PackoutReportLines', 'SourceLineNumber'),
            ('PackoutReportLines', 'RawText'),
            ('PackoutReportLines', 'Confidence'),
            ('PackoutReportLines', 'RequiresReview'),
            ('PackoutReportLines', 'NegativeQuantityConfirmed'),
            ('PackoutReportLines', 'WasCorrected'),
            ('PackoutReportLines', 'CreatedAt')
        ) as required(table_name, column_name)
        where to_regclass(format('%I.%I', current_schema(), required.table_name)) is not null
          and not exists (
              select 1
              from information_schema.columns
              where table_schema = current_schema()
                and table_name = required.table_name
                and column_name = required.column_name)
        group by required.table_name
    loop
        execute format('select count(*) from %I', required_table) into row_count;
        if row_count > 0 then
            raise exception 'Partially created table % contains % row(s) but lacks required columns %; transaction rolled back for operator review.',
                required_table, row_count, missing_required;
        end if;
    end loop;

    if to_regclass(format('%I.%I', current_schema(), 'PackCodeDefinitions')) is not null
       and exists (
           select 1
           from information_schema.columns
           where table_schema = current_schema()
             and table_name = 'PackCodeDefinitions'
             and column_name = 'NormalizedCode') then
        execute '
            select count(*)
            from (
                select "NormalizedCode"
                from "PackCodeDefinitions"
                group by "NormalizedCode"
                having count(*) > 1
            ) as duplicates'
            into value;
        if value > 0 then
            raise exception 'PackCodeDefinitions contains duplicate normalized codes; transaction rolled back.';
        end if;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutAnalysisConfigurations')) is not null then
        execute 'select count(*) from "PackoutAnalysisConfigurations"' into row_count;
        if row_count > 0 then
            execute 'select count(*) from "PackoutAnalysisConfigurations" where "Id" = 1' into value;
            if value <> 1 then
                raise exception 'Existing packout configuration data does not contain exactly one Id 1 row; transaction rolled back.';
            end if;
        end if;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutRuns')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema() and table_name = 'PackoutRuns'
             and column_name in ('FacilitySnapshot', 'PackingDate', 'RunNumber')
           group by table_name
           having count(*) = 3) then
        execute '
            select count(*)
            from (
                select "FacilitySnapshot", "PackingDate", "RunNumber"
                from "PackoutRuns"
                group by "FacilitySnapshot", "PackingDate", "RunNumber"
                having count(*) > 1
            ) as duplicates'
            into value;
        if value > 0 then
            raise exception 'PackoutRuns contains duplicate facility/date/run identities; transaction rolled back.';
        end if;
    end if;

    if to_regclass(format('%I.%I', current_schema(), 'PackoutReportSources')) is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = current_schema() and table_name = 'PackoutReportSources'
             and column_name in ('PackoutRunId', 'Sha256')
           group by table_name
           having count(*) = 2) then
        execute '
            select count(*)
            from (
                select "PackoutRunId", "Sha256"
                from "PackoutReportSources"
                group by "PackoutRunId", "Sha256"
                having count(*) > 1
            ) as duplicates'
            into value;
        if value > 0 then
            raise exception 'PackoutReportSources contains duplicate run/hash identities; transaction rolled back.';
        end if;
    end if;
end
$precheck$;

lock table "QcSamples",
           "QcFruitReadings",
           "QcFruitDefects",
           "RunProjections",
           "RunProjectionSources",
           "BinsRunEntries"
    in share row exclusive mode;

alter table "RunProjectionSources"
    add column if not exists "TotalDefectPercentageSnapshot" numeric(8,4) null;

alter table "RunProjections"
    add column if not exists "IsLocked" boolean not null default false,
    add column if not exists "LockedAt" timestamp with time zone null,
    add column if not exists "LockedByUserId" integer null;

alter table "BinsRunEntries"
    add column if not exists "IsReconciled" boolean not null default false,
    add column if not exists "ReconciledAt" timestamp with time zone null,
    add column if not exists "ReconciledByUserId" integer null;

alter table "QcSamples"
    add column if not exists "DefectInspectionStatus" character varying(50) null;

update "QcSamples" as sample
set "DefectInspectionStatus" =
    case when exists (
        select 1
        from "QcFruitReadings" as reading
        join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
        where reading."QcSampleId" = sample."Id")
    then 'Defects found'
    else 'No defects found'
    end
where "DefectInspectionStatus" is distinct from
    case when exists (
        select 1
        from "QcFruitReadings" as reading
        join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
        where reading."QcSampleId" = sample."Id")
    then 'Defects found'
    else 'No defects found'
    end;

do $status_check$
begin
    if exists (
        select 1
        from "QcSamples"
        where "DefectInspectionStatus" is null
           or "DefectInspectionStatus" not in ('No defects found', 'Defects found')) then
        raise exception 'QcSamples defect-status backfill did not produce valid values; transaction rolled back.';
    end if;
end
$status_check$;

alter table "QcSamples"
    alter column "DefectInspectionStatus" set default 'No defects found',
    alter column "DefectInspectionStatus" set not null;

create table if not exists "PackCodeDefinitions" (
    "Id" integer generated by default as identity,
    "Code" character varying(75) not null,
    "NormalizedCode" character varying(75) not null,
    "DisplayName" character varying(150) not null,
    "ProductCategory" character varying(50) not null,
    "NetWeightPounds" numeric(10,4) null,
    "SizeCategory" integer null,
    "GradeId" integer null,
    "IsActive" boolean not null,
    "CreatedAt" timestamp with time zone not null,
    "CreatedByUserId" integer null,
    "UpdatedAt" timestamp with time zone null,
    "UpdatedByUserId" integer null,
    constraint "PK_PackCodeDefinitions" primary key ("Id")
);

alter table "PackCodeDefinitions"
    add column if not exists "Id" integer generated by default as identity,
    add column if not exists "Code" character varying(75) not null,
    add column if not exists "NormalizedCode" character varying(75) not null,
    add column if not exists "DisplayName" character varying(150) not null,
    add column if not exists "ProductCategory" character varying(50) not null,
    add column if not exists "NetWeightPounds" numeric(10,4) null,
    add column if not exists "SizeCategory" integer null,
    add column if not exists "GradeId" integer null,
    add column if not exists "IsActive" boolean not null default true,
    add column if not exists "CreatedAt" timestamp with time zone not null,
    add column if not exists "CreatedByUserId" integer null,
    add column if not exists "UpdatedAt" timestamp with time zone null,
    add column if not exists "UpdatedByUserId" integer null;

create table if not exists "PackoutAnalysisConfigurations" (
    "Id" integer generated by default as identity,
    "AppleBinWeightPounds" numeric(10,2) not null default 880,
    "PearBinWeightPounds" numeric(10,2) not null default 920,
    "SizeScoreWeight" numeric(8,4) not null default 35,
    "GradeScoreWeight" numeric(8,4) not null default 35,
    "PackoutScoreWeight" numeric(8,4) not null default 21,
    "JuiceScoreWeight" numeric(8,4) not null default 3,
    "PeelerSlicerScoreWeight" numeric(8,4) not null default 3,
    "WasteScoreWeight" numeric(8,4) not null default 3,
    "CurrentCropYearHistoryWeight" numeric(8,4) not null default 80,
    "PriorCropYearHistoryWeight" numeric(8,4) not null default 20,
    "UpdatedAt" timestamp with time zone not null,
    "UpdatedByUserId" integer null,
    constraint "PK_PackoutAnalysisConfigurations" primary key ("Id")
);

alter table "PackoutAnalysisConfigurations"
    add column if not exists "Id" integer generated by default as identity,
    add column if not exists "AppleBinWeightPounds" numeric(10,2) not null default 880,
    add column if not exists "PearBinWeightPounds" numeric(10,2) not null default 920,
    add column if not exists "SizeScoreWeight" numeric(8,4) not null default 35,
    add column if not exists "GradeScoreWeight" numeric(8,4) not null default 35,
    add column if not exists "PackoutScoreWeight" numeric(8,4) not null default 21,
    add column if not exists "JuiceScoreWeight" numeric(8,4) not null default 3,
    add column if not exists "PeelerSlicerScoreWeight" numeric(8,4) not null default 3,
    add column if not exists "WasteScoreWeight" numeric(8,4) not null default 3,
    add column if not exists "CurrentCropYearHistoryWeight" numeric(8,4) not null default 80,
    add column if not exists "PriorCropYearHistoryWeight" numeric(8,4) not null default 20,
    add column if not exists "UpdatedAt" timestamp with time zone not null,
    add column if not exists "UpdatedByUserId" integer null;

create table if not exists "PackoutRuns" (
    "Id" bigint generated by default as identity,
    "RunProjectionId" bigint not null,
    "BinsRunEntryId" bigint null,
    "Status" character varying(50) not null,
    "FacilitySnapshot" character varying(50) not null,
    "PackingDate" date not null,
    "RunNumber" integer not null,
    "LotNumberSnapshot" character varying(100) not null,
    "VarietySnapshot" character varying(100) not null,
    "IsOrganicSnapshot" boolean not null,
    "CropYearSnapshot" integer not null,
    "DumpedBins" numeric(18,4) not null,
    "PoundsPerBin" numeric(10,2) not null,
    "DumpedPounds" numeric(18,4) not null,
    "PackedProductPounds" numeric(18,4) not null,
    "JuicePounds" numeric(18,4) not null,
    "PeelerSlicerPounds" numeric(18,4) not null,
    "WastePounds" numeric(18,4) not null,
    "SupplementalJuicePounds" numeric(18,4) null,
    "SupplementalPeelerSlicerPounds" numeric(18,4) null,
    "SupplementalWastePounds" numeric(18,4) null,
    "ActualPackoutPercent" numeric(8,4) null,
    "ActualJuicePercent" numeric(8,4) null,
    "ActualPeelerSlicerPercent" numeric(8,4) null,
    "ActualWastePercent" numeric(8,4) null,
    "SizeAccuracyScore" numeric(8,4) null,
    "GradeAccuracyScore" numeric(8,4) null,
    "PackoutAccuracyScore" numeric(8,4) null,
    "JuiceAccuracyScore" numeric(8,4) null,
    "PeelerSlicerAccuracyScore" numeric(8,4) null,
    "WasteAccuracyScore" numeric(8,4) null,
    "OverallAccuracyScore" numeric(8,4) null,
    "ReconciliationDifferencePounds" numeric(18,4) not null,
    "HasReconciliationWarning" boolean not null,
    "ReviewNotes" character varying(2000) null,
    "ProjectionSnapshotJson" text null,
    "ActualDistributionSnapshotJson" text null,
    "AccuracySnapshotJson" text null,
    "ConfigurationSnapshotJson" text null,
    "CalculationVersion" character varying(50) null,
    "ConcurrencyVersion" bigint not null,
    "CreatedAt" timestamp with time zone not null,
    "CreatedByUserId" integer null,
    "UpdatedAt" timestamp with time zone not null,
    "UpdatedByUserId" integer null,
    "FinalizedAt" timestamp with time zone null,
    "FinalizedByUserId" integer null,
    "FinalReportFileName" character varying(255) null,
    "FinalReportSha256" character varying(64) null,
    "FinalEmailMessageId" character varying(250) null,
    "ReopenedAt" timestamp with time zone null,
    "ReopenedByUserId" integer null,
    "ReopenReason" character varying(1000) null,
    constraint "PK_PackoutRuns" primary key ("Id")
);

-- Nullable columns can be repaired on an existing partial table. Required
-- columns are added only after the empty-table precheck above has succeeded.
alter table "PackoutRuns"
    add column if not exists "Id" bigint generated by default as identity,
    add column if not exists "RunProjectionId" bigint not null,
    add column if not exists "BinsRunEntryId" bigint null,
    add column if not exists "Status" character varying(50) not null,
    add column if not exists "FacilitySnapshot" character varying(50) not null,
    add column if not exists "PackingDate" date not null,
    add column if not exists "RunNumber" integer not null,
    add column if not exists "LotNumberSnapshot" character varying(100) not null,
    add column if not exists "VarietySnapshot" character varying(100) not null,
    add column if not exists "IsOrganicSnapshot" boolean not null,
    add column if not exists "CropYearSnapshot" integer not null,
    add column if not exists "DumpedBins" numeric(18,4) not null,
    add column if not exists "PoundsPerBin" numeric(10,2) not null,
    add column if not exists "DumpedPounds" numeric(18,4) not null,
    add column if not exists "PackedProductPounds" numeric(18,4) not null,
    add column if not exists "JuicePounds" numeric(18,4) not null,
    add column if not exists "PeelerSlicerPounds" numeric(18,4) not null,
    add column if not exists "WastePounds" numeric(18,4) not null,
    add column if not exists "SupplementalJuicePounds" numeric(18,4) null,
    add column if not exists "SupplementalPeelerSlicerPounds" numeric(18,4) null,
    add column if not exists "SupplementalWastePounds" numeric(18,4) null,
    add column if not exists "ActualPackoutPercent" numeric(8,4) null,
    add column if not exists "ActualJuicePercent" numeric(8,4) null,
    add column if not exists "ActualPeelerSlicerPercent" numeric(8,4) null,
    add column if not exists "ActualWastePercent" numeric(8,4) null,
    add column if not exists "SizeAccuracyScore" numeric(8,4) null,
    add column if not exists "GradeAccuracyScore" numeric(8,4) null,
    add column if not exists "PackoutAccuracyScore" numeric(8,4) null,
    add column if not exists "JuiceAccuracyScore" numeric(8,4) null,
    add column if not exists "PeelerSlicerAccuracyScore" numeric(8,4) null,
    add column if not exists "WasteAccuracyScore" numeric(8,4) null,
    add column if not exists "OverallAccuracyScore" numeric(8,4) null,
    add column if not exists "ReconciliationDifferencePounds" numeric(18,4) not null,
    add column if not exists "HasReconciliationWarning" boolean not null,
    add column if not exists "ReviewNotes" character varying(2000) null,
    add column if not exists "ProjectionSnapshotJson" text null,
    add column if not exists "ActualDistributionSnapshotJson" text null,
    add column if not exists "AccuracySnapshotJson" text null,
    add column if not exists "ConfigurationSnapshotJson" text null,
    add column if not exists "CalculationVersion" character varying(50) null,
    add column if not exists "ConcurrencyVersion" bigint not null,
    add column if not exists "CreatedAt" timestamp with time zone not null,
    add column if not exists "CreatedByUserId" integer null,
    add column if not exists "UpdatedAt" timestamp with time zone not null,
    add column if not exists "UpdatedByUserId" integer null,
    add column if not exists "FinalizedAt" timestamp with time zone null,
    add column if not exists "FinalizedByUserId" integer null,
    add column if not exists "FinalReportFileName" character varying(255) null,
    add column if not exists "FinalReportSha256" character varying(64) null,
    add column if not exists "FinalEmailMessageId" character varying(250) null,
    add column if not exists "ReopenedAt" timestamp with time zone null,
    add column if not exists "ReopenedByUserId" integer null,
    add column if not exists "ReopenReason" character varying(1000) null;

create table if not exists "PackoutEmailAttempts" (
    "Id" bigint generated by default as identity,
    "PackoutRunId" bigint not null,
    "Recipient" character varying(320) not null,
    "SenderUserId" integer null,
    "AttemptedAt" timestamp with time zone not null,
    "Succeeded" boolean not null,
    "MessageId" character varying(250) null,
    "SafeError" character varying(1000) null,
    "IsUpdatedAnalysis" boolean not null,
    constraint "PK_PackoutEmailAttempts" primary key ("Id")
);

alter table "PackoutEmailAttempts"
    add column if not exists "Id" bigint generated by default as identity,
    add column if not exists "PackoutRunId" bigint not null,
    add column if not exists "Recipient" character varying(320) not null,
    add column if not exists "SenderUserId" integer null,
    add column if not exists "AttemptedAt" timestamp with time zone not null,
    add column if not exists "Succeeded" boolean not null,
    add column if not exists "MessageId" character varying(250) null,
    add column if not exists "SafeError" character varying(1000) null,
    add column if not exists "IsUpdatedAnalysis" boolean not null;

create table if not exists "PackoutReportSources" (
    "Id" bigint generated by default as identity,
    "PackoutRunId" bigint not null,
    "OriginalFileName" character varying(255) not null,
    "ContentType" character varying(150) not null,
    "FileSizeBytes" bigint not null,
    "Sha256" character varying(64) not null,
    "ParserName" character varying(100) not null,
    "ParserVersion" character varying(50) null,
    "Confidence" numeric(6,5) null,
    "SafeDiagnostic" character varying(1000) null,
    "ParsedAt" timestamp with time zone not null,
    constraint "PK_PackoutReportSources" primary key ("Id")
);

alter table "PackoutReportSources"
    add column if not exists "Id" bigint generated by default as identity,
    add column if not exists "PackoutRunId" bigint not null,
    add column if not exists "OriginalFileName" character varying(255) not null,
    add column if not exists "ContentType" character varying(150) not null,
    add column if not exists "FileSizeBytes" bigint not null,
    add column if not exists "Sha256" character varying(64) not null,
    add column if not exists "ParserName" character varying(100) not null,
    add column if not exists "ParserVersion" character varying(50) null,
    add column if not exists "Confidence" numeric(6,5) null,
    add column if not exists "SafeDiagnostic" character varying(1000) null,
    add column if not exists "ParsedAt" timestamp with time zone not null;

create table if not exists "PackoutReportLines" (
    "Id" bigint generated by default as identity,
    "PackoutRunId" bigint not null,
    "PackoutReportSourceId" bigint null,
    "SourceLineNumber" integer not null,
    "RawText" character varying(2000) not null,
    "RawPackCode" character varying(100) null,
    "NormalizedPackCode" character varying(100) null,
    "PackCodeDefinitionId" integer null,
    "Quantity" numeric(18,4) null,
    "NetWeightPounds" numeric(10,4) null,
    "ExtendedWeightPounds" numeric(18,4) null,
    "SizeCategory" integer null,
    "GradeId" integer null,
    "ProductCategory" character varying(50) null,
    "Confidence" numeric(6,5) not null,
    "RequiresReview" boolean not null,
    "NegativeQuantityConfirmed" boolean not null,
    "WasCorrected" boolean not null,
    "CorrectionReason" character varying(1000) null,
    "CreatedAt" timestamp with time zone not null,
    "UpdatedAt" timestamp with time zone null,
    "UpdatedByUserId" integer null,
    constraint "PK_PackoutReportLines" primary key ("Id")
);

alter table "PackoutReportLines"
    add column if not exists "Id" bigint generated by default as identity,
    add column if not exists "PackoutRunId" bigint not null,
    add column if not exists "PackoutReportSourceId" bigint null,
    add column if not exists "SourceLineNumber" integer not null,
    add column if not exists "RawText" character varying(2000) not null,
    add column if not exists "RawPackCode" character varying(100) null,
    add column if not exists "NormalizedPackCode" character varying(100) null,
    add column if not exists "PackCodeDefinitionId" integer null,
    add column if not exists "Quantity" numeric(18,4) null,
    add column if not exists "NetWeightPounds" numeric(10,4) null,
    add column if not exists "ExtendedWeightPounds" numeric(18,4) null,
    add column if not exists "SizeCategory" integer null,
    add column if not exists "GradeId" integer null,
    add column if not exists "ProductCategory" character varying(50) null,
    add column if not exists "Confidence" numeric(6,5) not null,
    add column if not exists "RequiresReview" boolean not null,
    add column if not exists "NegativeQuantityConfirmed" boolean not null,
    add column if not exists "WasCorrected" boolean not null,
    add column if not exists "CorrectionReason" character varying(1000) null,
    add column if not exists "CreatedAt" timestamp with time zone not null,
    add column if not exists "UpdatedAt" timestamp with time zone null,
    add column if not exists "UpdatedByUserId" integer null;

do $identity_columns$
declare
    target_table text;
    row_count bigint;
    has_generator boolean;
begin
    foreach target_table in array array[
        'PackCodeDefinitions',
        'PackoutAnalysisConfigurations',
        'PackoutRuns',
        'PackoutEmailAttempts',
        'PackoutReportSources',
        'PackoutReportLines'
    ]
    loop
        select column_info.identity_generation is not null
               or column_info.column_default like 'nextval(%'
        into has_generator
        from information_schema.columns as column_info
        where column_info.table_schema = current_schema()
          and column_info.table_name = target_table
          and column_info.column_name = 'Id';

        if not coalesce(has_generator, false) then
            execute format('select count(*) from %I', target_table) into row_count;
            if row_count > 0 then
                raise exception 'Partially created table % has a non-generated Id column and contains data; transaction rolled back.',
                    target_table;
            end if;

            execute format(
                'alter table %I alter column "Id" add generated by default as identity',
                target_table);
        end if;
    end loop;
end
$identity_columns$;

do $primary_keys$
begin
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackCodeDefinitions"'::regclass and contype = 'p') then
        alter table "PackCodeDefinitions"
            add constraint "PK_PackCodeDefinitions" primary key ("Id");
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackoutAnalysisConfigurations"'::regclass and contype = 'p') then
        alter table "PackoutAnalysisConfigurations"
            add constraint "PK_PackoutAnalysisConfigurations" primary key ("Id");
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackoutRuns"'::regclass and contype = 'p') then
        alter table "PackoutRuns"
            add constraint "PK_PackoutRuns" primary key ("Id");
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackoutEmailAttempts"'::regclass and contype = 'p') then
        alter table "PackoutEmailAttempts"
            add constraint "PK_PackoutEmailAttempts" primary key ("Id");
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackoutReportSources"'::regclass and contype = 'p') then
        alter table "PackoutReportSources"
            add constraint "PK_PackoutReportSources" primary key ("Id");
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '"PackoutReportLines"'::regclass and contype = 'p') then
        alter table "PackoutReportLines"
            add constraint "PK_PackoutReportLines" primary key ("Id");
    end if;
end
$primary_keys$;

insert into "PackoutAnalysisConfigurations" (
    "Id",
    "AppleBinWeightPounds",
    "PearBinWeightPounds",
    "SizeScoreWeight",
    "GradeScoreWeight",
    "PackoutScoreWeight",
    "JuiceScoreWeight",
    "PeelerSlicerScoreWeight",
    "WasteScoreWeight",
    "CurrentCropYearHistoryWeight",
    "PriorCropYearHistoryWeight",
    "UpdatedAt",
    "UpdatedByUserId")
select 1, 880, 920, 35, 35, 21, 3, 3, 3, 80, 20, now(), null
where not exists (
    select 1
    from "PackoutAnalysisConfigurations"
    where "Id" = 1);

select setval(
    pg_get_serial_sequence(format('%I.%I', current_schema(), 'PackoutAnalysisConfigurations'), 'Id'),
    greatest((select coalesce(max("Id"), 1) from "PackoutAnalysisConfigurations"), 1),
    true);

create index if not exists "IX_RunProjections_LockedByUserId"
    on "RunProjections" ("LockedByUserId");
create index if not exists "IX_BinsRunEntries_ReconciledByUserId"
    on "BinsRunEntries" ("ReconciledByUserId");
create index if not exists "IX_PackCodeDefinitions_CreatedByUserId"
    on "PackCodeDefinitions" ("CreatedByUserId");
create index if not exists "IX_PackCodeDefinitions_GradeId"
    on "PackCodeDefinitions" ("GradeId");
create index if not exists "IX_PackCodeDefinitions_IsActive_ProductCategory"
    on "PackCodeDefinitions" ("IsActive", "ProductCategory");
create unique index if not exists "IX_PackCodeDefinitions_NormalizedCode"
    on "PackCodeDefinitions" ("NormalizedCode");
create index if not exists "IX_PackCodeDefinitions_UpdatedByUserId"
    on "PackCodeDefinitions" ("UpdatedByUserId");
create index if not exists "IX_PackoutAnalysisConfigurations_UpdatedByUserId"
    on "PackoutAnalysisConfigurations" ("UpdatedByUserId");
create index if not exists "IX_PackoutEmailAttempts_PackoutRunId_AttemptedAt"
    on "PackoutEmailAttempts" ("PackoutRunId", "AttemptedAt");
create index if not exists "IX_PackoutEmailAttempts_SenderUserId"
    on "PackoutEmailAttempts" ("SenderUserId");
create index if not exists "IX_PackoutReportLines_GradeId"
    on "PackoutReportLines" ("GradeId");
create index if not exists "IX_PackoutReportLines_NormalizedPackCode"
    on "PackoutReportLines" ("NormalizedPackCode");
create index if not exists "IX_PackoutReportLines_PackCodeDefinitionId"
    on "PackoutReportLines" ("PackCodeDefinitionId");
create index if not exists "IX_PackoutReportLines_PackoutReportSourceId"
    on "PackoutReportLines" ("PackoutReportSourceId");
create index if not exists "IX_PackoutReportLines_PackoutRunId_ProductCategory"
    on "PackoutReportLines" ("PackoutRunId", "ProductCategory");
create index if not exists "IX_PackoutReportLines_UpdatedByUserId"
    on "PackoutReportLines" ("UpdatedByUserId");
create unique index if not exists "IX_PackoutReportSources_PackoutRunId_Sha256"
    on "PackoutReportSources" ("PackoutRunId", "Sha256");
create unique index if not exists "IX_PackoutRuns_BinsRunEntryId"
    on "PackoutRuns" ("BinsRunEntryId")
    where "BinsRunEntryId" is not null;
create index if not exists "IX_PackoutRuns_CreatedByUserId"
    on "PackoutRuns" ("CreatedByUserId");
create unique index if not exists "IX_PackoutRuns_FacilitySnapshot_PackingDate_RunNumber"
    on "PackoutRuns" ("FacilitySnapshot", "PackingDate", "RunNumber");
create index if not exists "IX_PackoutRuns_FinalizedByUserId"
    on "PackoutRuns" ("FinalizedByUserId");
create index if not exists "IX_PackoutRuns_ReopenedByUserId"
    on "PackoutRuns" ("ReopenedByUserId");
create index if not exists "IX_PackoutRuns_RunProjectionId_Status"
    on "PackoutRuns" ("RunProjectionId", "Status");
create index if not exists "IX_PackoutRuns_UpdatedByUserId"
    on "PackoutRuns" ("UpdatedByUserId");

do $foreign_keys$
begin
    if not exists (select 1 from pg_constraint where conrelid = '"BinsRunEntries"'::regclass
                   and conname = left('FK_BinsRunEntries_Users_ReconciledByUserId', 63)) then
        alter table "BinsRunEntries"
            add constraint "FK_BinsRunEntries_Users_ReconciledByUserId"
            foreign key ("ReconciledByUserId") references "Users" ("Id") on delete set null;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"RunProjections"'::regclass
                   and conname = left('FK_RunProjections_Users_LockedByUserId', 63)) then
        alter table "RunProjections"
            add constraint "FK_RunProjections_Users_LockedByUserId"
            foreign key ("LockedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackCodeDefinitions"'::regclass
                   and conname = left('FK_PackCodeDefinitions_Grades_GradeId', 63)) then
        alter table "PackCodeDefinitions"
            add constraint "FK_PackCodeDefinitions_Grades_GradeId"
            foreign key ("GradeId") references "Grades" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackCodeDefinitions"'::regclass
                   and conname = left('FK_PackCodeDefinitions_Users_CreatedByUserId', 63)) then
        alter table "PackCodeDefinitions"
            add constraint "FK_PackCodeDefinitions_Users_CreatedByUserId"
            foreign key ("CreatedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackCodeDefinitions"'::regclass
                   and conname = left('FK_PackCodeDefinitions_Users_UpdatedByUserId', 63)) then
        alter table "PackCodeDefinitions"
            add constraint "FK_PackCodeDefinitions_Users_UpdatedByUserId"
            foreign key ("UpdatedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutAnalysisConfigurations"'::regclass
                   and conname = left('FK_PackoutAnalysisConfigurations_Users_UpdatedByUserId', 63)) then
        alter table "PackoutAnalysisConfigurations"
            add constraint "FK_PackoutAnalysisConfigurations_Users_UpdatedByUserId"
            foreign key ("UpdatedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_BinsRunEntries_BinsRunEntryId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_BinsRunEntries_BinsRunEntryId"
            foreign key ("BinsRunEntryId") references "BinsRunEntries" ("Id") on delete restrict;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_RunProjections_RunProjectionId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_RunProjections_RunProjectionId"
            foreign key ("RunProjectionId") references "RunProjections" ("Id") on delete restrict;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_Users_CreatedByUserId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_Users_CreatedByUserId"
            foreign key ("CreatedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_Users_FinalizedByUserId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_Users_FinalizedByUserId"
            foreign key ("FinalizedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_Users_ReopenedByUserId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_Users_ReopenedByUserId"
            foreign key ("ReopenedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutRuns"'::regclass
                   and conname = left('FK_PackoutRuns_Users_UpdatedByUserId', 63)) then
        alter table "PackoutRuns"
            add constraint "FK_PackoutRuns_Users_UpdatedByUserId"
            foreign key ("UpdatedByUserId") references "Users" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutEmailAttempts"'::regclass
                   and conname = left('FK_PackoutEmailAttempts_PackoutRuns_PackoutRunId', 63)) then
        alter table "PackoutEmailAttempts"
            add constraint "FK_PackoutEmailAttempts_PackoutRuns_PackoutRunId"
            foreign key ("PackoutRunId") references "PackoutRuns" ("Id") on delete cascade;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutEmailAttempts"'::regclass
                   and conname = left('FK_PackoutEmailAttempts_Users_SenderUserId', 63)) then
        alter table "PackoutEmailAttempts"
            add constraint "FK_PackoutEmailAttempts_Users_SenderUserId"
            foreign key ("SenderUserId") references "Users" ("Id") on delete set null;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportSources"'::regclass
                   and conname = left('FK_PackoutReportSources_PackoutRuns_PackoutRunId', 63)) then
        alter table "PackoutReportSources"
            add constraint "FK_PackoutReportSources_PackoutRuns_PackoutRunId"
            foreign key ("PackoutRunId") references "PackoutRuns" ("Id") on delete cascade;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportLines"'::regclass
                   and conname = left('FK_PackoutReportLines_Grades_GradeId', 63)) then
        alter table "PackoutReportLines"
            add constraint "FK_PackoutReportLines_Grades_GradeId"
            foreign key ("GradeId") references "Grades" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportLines"'::regclass
                   and conname = left('FK_PackoutReportLines_PackCodeDefinitions_PackCodeDefinitionId', 63)) then
        alter table "PackoutReportLines"
            add constraint "FK_PackoutReportLines_PackCodeDefinitions_PackCodeDefinitionId"
            foreign key ("PackCodeDefinitionId") references "PackCodeDefinitions" ("Id");
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportLines"'::regclass
                   and conname = left('FK_PackoutReportLines_PackoutReportSources_PackoutReportSourceId', 63)) then
        alter table "PackoutReportLines"
            add constraint "FK_PackoutReportLines_PackoutReportSources_PackoutReportSourceId"
            foreign key ("PackoutReportSourceId") references "PackoutReportSources" ("Id") on delete set null;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportLines"'::regclass
                   and conname = left('FK_PackoutReportLines_PackoutRuns_PackoutRunId', 63)) then
        alter table "PackoutReportLines"
            add constraint "FK_PackoutReportLines_PackoutRuns_PackoutRunId"
            foreign key ("PackoutRunId") references "PackoutRuns" ("Id") on delete cascade;
    end if;
    if not exists (select 1 from pg_constraint where conrelid = '"PackoutReportLines"'::regclass
                   and conname = left('FK_PackoutReportLines_Users_UpdatedByUserId', 63)) then
        alter table "PackoutReportLines"
            add constraint "FK_PackoutReportLines_Users_UpdatedByUserId"
            foreign key ("UpdatedByUserId") references "Users" ("Id");
    end if;
end
$foreign_keys$;

do $verify_before_commit$
declare
    missing_objects text;
    missing_indexes text;
    missing_constraints text;
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
        raise exception 'Post-apply object verification failed for %; transaction rolled back.', missing_objects;
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
        select 1 from pg_indexes
        where schemaname = current_schema()
          and indexname = expected.index_name);

    if missing_indexes is not null then
        raise exception 'Post-apply index verification failed for %; transaction rolled back.', missing_indexes;
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
        raise exception 'Post-apply constraint verification failed for %; transaction rolled back.', missing_constraints;
    end if;

    if (select count(*) from "PackoutAnalysisConfigurations" where "Id" = 1) <> 1 then
        raise exception 'Default packout-analysis configuration verification failed; transaction rolled back.';
    end if;

    if exists (
        select 1
        from "QcSamples" as sample
        where sample."DefectInspectionStatus" is null
           or sample."DefectInspectionStatus" is distinct from
              case when exists (
                  select 1
                  from "QcFruitReadings" as reading
                  join "QcFruitDefects" as defect on defect."QcFruitReadingId" = reading."Id"
                  where reading."QcSampleId" = sample."Id")
              then 'Defects found'
              else 'No defects found'
              end) then
        raise exception 'Defect-status verification disagrees with actual defect rows; transaction rolled back.';
    end if;
end
$verify_before_commit$;

commit;
