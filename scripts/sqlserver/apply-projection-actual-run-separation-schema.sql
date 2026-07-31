BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PackoutRuns]') AND [c].[name] = N'RunProjectionId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [PackoutRuns] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [PackoutRuns] ALTER COLUMN [RunProjectionId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    ALTER TABLE [PackoutRuns] ADD [ActualRunId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    ALTER TABLE [PackoutRuns] ADD [RunExpectationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE TABLE [RunExpectations] (
        [Id] bigint NOT NULL IDENTITY,
        [ActualRunId] bigint NOT NULL,
        [ActualRunRevisionId] bigint NOT NULL,
        [RevisionNumber] int NOT NULL,
        [FacilityWarehouseId] int NOT NULL,
        [FacilitySnapshot] nvarchar(50) NOT NULL,
        [RunAtSnapshot] datetimeoffset NOT NULL,
        [TotalBins] int NOT NULL,
        [GrossPounds] decimal(18,4) NOT NULL,
        [ExpectedPackoutPercent] decimal(8,4) NOT NULL,
        [ExpectedPackedPounds] decimal(18,4) NOT NULL,
        [ExpectedPackedBoxes] decimal(18,4) NOT NULL,
        [ExpectedWholeBoxes] int NOT NULL,
        [ExpectedCullPounds] decimal(18,4) NOT NULL,
        [ExpectedJuicePounds] decimal(18,4) NOT NULL,
        [ExpectedPeelerPounds] decimal(18,4) NOT NULL,
        [ExpectedWastePounds] decimal(18,4) NOT NULL,
        [ConfidencePercent] decimal(8,4) NOT NULL,
        [SizeDistributionSnapshotJson] nvarchar(max) NOT NULL,
        [GradeDistributionSnapshotJson] nvarchar(max) NOT NULL,
        [ConfigurationSnapshotJson] nvarchar(max) NOT NULL,
        [CalculationVersion] nvarchar(75) NOT NULL,
        [CalculatedAt] datetimeoffset NOT NULL,
        [CreatedByUserId] int NULL,
        CONSTRAINT [PK_RunExpectations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId] FOREIGN KEY ([ActualRunRevisionId]) REFERENCES [ActualRunRevisions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RunExpectations_ActualRuns_ActualRunId] FOREIGN KEY ([ActualRunId]) REFERENCES [ActualRuns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RunExpectations_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE TABLE [RunExpectationSources] (
        [Id] bigint NOT NULL IDENTITY,
        [RunExpectationId] bigint NOT NULL,
        [BinsRunEntryId] bigint NOT NULL,
        [WarehouseId] int NOT NULL,
        [RoomId] int NOT NULL,
        [FacilitySnapshot] nvarchar(50) NOT NULL,
        [RoomSnapshot] nvarchar(100) NOT NULL,
        [CropYearSnapshot] int NULL,
        [GrowerLotId] int NULL,
        [FruitProfileId] int NULL,
        [GrowerSnapshot] nvarchar(200) NOT NULL,
        [LotSnapshot] nvarchar(100) NOT NULL,
        [VarietySnapshot] nvarchar(100) NOT NULL,
        [ProductionTypeSnapshot] nvarchar(50) NOT NULL,
        [IsOrganicSnapshot] bit NOT NULL,
        [BinsContributed] int NOT NULL,
        [ContributionPercent] decimal(9,6) NOT NULL,
        [QcSampleId] bigint NULL,
        [QcSampleTakenAtSnapshot] datetimeoffset NULL,
        [QcFruitCountSnapshot] int NOT NULL,
        [QcMeasurementSnapshotJson] nvarchar(max) NOT NULL,
        [SizeDistributionSnapshotJson] nvarchar(max) NOT NULL,
        [GradeDistributionSnapshotJson] nvarchar(max) NOT NULL,
        [GrossPounds] decimal(18,4) NOT NULL,
        [ExpectedPackedPounds] decimal(18,4) NOT NULL,
        [ExpectedWholeBoxes] int NOT NULL,
        [ExpectedCullPounds] decimal(18,4) NOT NULL,
        [ConfidencePercent] decimal(8,4) NOT NULL,
        [WarningSnapshot] nvarchar(1000) NULL,
        CONSTRAINT [PK_RunExpectationSources] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId] FOREIGN KEY ([BinsRunEntryId]) REFERENCES [BinsRunEntries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RunExpectationSources_QcSamples_QcSampleId] FOREIGN KEY ([QcSampleId]) REFERENCES [QcSamples] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RunExpectationSources_RunExpectations_RunExpectationId] FOREIGN KEY ([RunExpectationId]) REFERENCES [RunExpectations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE TABLE [PackoutSourceAllocations] (
        [Id] bigint NOT NULL IDENTITY,
        [PackoutRunId] bigint NOT NULL,
        [RunExpectationSourceId] bigint NOT NULL,
        [BinsContributed] int NOT NULL,
        [ContributionPercent] decimal(9,6) NOT NULL,
        [AllocatedPackedPounds] decimal(18,6) NOT NULL,
        [AllocatedWholeBoxes] int NOT NULL,
        [AllocatedResidualPounds] decimal(18,6) NOT NULL,
        [AllocatedJuicePounds] decimal(18,6) NOT NULL,
        [AllocatedPeelerPounds] decimal(18,6) NOT NULL,
        [AllocatedWastePounds] decimal(18,6) NOT NULL,
        [PackCodeAllocationJson] nvarchar(max) NOT NULL,
        [SizeAllocationJson] nvarchar(max) NOT NULL,
        [GradeAllocationJson] nvarchar(max) NOT NULL,
        [AllocationVersion] nvarchar(75) NOT NULL,
        [CalculatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PackoutSourceAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId] FOREIGN KEY ([PackoutRunId]) REFERENCES [PackoutRuns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId] FOREIGN KEY ([RunExpectationSourceId]) REFERENCES [RunExpectationSources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_PackoutRuns_RunExpectationId] ON [PackoutRuns] ([RunExpectationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_PackoutRuns_ActualRunId] ON [PackoutRuns] ([ActualRunId]) WHERE [ActualRunId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId] ON [PackoutSourceAllocations] ([PackoutRunId], [RunExpectationSourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_PackoutSourceAllocations_RunExpectationSourceId] ON [PackoutSourceAllocations] ([RunExpectationSourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RunExpectations_ActualRunId_RevisionNumber] ON [RunExpectations] ([ActualRunId], [RevisionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RunExpectations_ActualRunRevisionId] ON [RunExpectations] ([ActualRunRevisionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_RunExpectations_CreatedByUserId] ON [RunExpectations] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_RunExpectationSources_BinsRunEntryId] ON [RunExpectationSources] ([BinsRunEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_RunExpectationSources_QcSampleId] ON [RunExpectationSources] ([QcSampleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RunExpectationSources_RunExpectationId_BinsRunEntryId] ON [RunExpectationSources] ([RunExpectationId], [BinsRunEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    CREATE INDEX [IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot] ON [RunExpectationSources] ([WarehouseId], [RoomId], [CropYearSnapshot], [LotSnapshot], [VarietySnapshot]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    ALTER TABLE [PackoutRuns] ADD CONSTRAINT [FK_PackoutRuns_ActualRuns_ActualRunId] FOREIGN KEY ([ActualRunId]) REFERENCES [ActualRuns] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    ALTER TABLE [PackoutRuns] ADD CONSTRAINT [FK_PackoutRuns_RunExpectations_RunExpectationId] FOREIGN KEY ([RunExpectationId]) REFERENCES [RunExpectations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731014107_SeparatePlanningProjectionsFromActualRuns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731014107_SeparatePlanningProjectionsFromActualRuns', N'9.0.9');
END;

COMMIT;
GO
