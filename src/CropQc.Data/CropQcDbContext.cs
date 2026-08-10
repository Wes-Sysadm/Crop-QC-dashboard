using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data;

public sealed class CropQcDbContext(DbContextOptions<CropQcDbContext> options) : DbContext(options)
{
    private bool synchronizingDefectInspectionStatus;
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGoogleCredential> UserGoogleCredentials => Set<UserGoogleCredential>();
    public DbSet<UserPageAccess> UserPageAccesses => Set<UserPageAccess>();
    public DbSet<UserEmploymentHistory> UserEmploymentHistory => Set<UserEmploymentHistory>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RolePageAccess> RolePageAccesses => Set<RolePageAccess>();
    public DbSet<PasswordPolicy> PasswordPolicies => Set<PasswordPolicy>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<GrowerLot> GrowerLots => Set<GrowerLot>();
    public DbSet<CanonicalGrower> CanonicalGrowers => Set<CanonicalGrower>();
    public DbSet<CanonicalGrowerAlias> CanonicalGrowerAliases => Set<CanonicalGrowerAlias>();
    public DbSet<CanonicalGrowerNumber> CanonicalGrowerNumbers => Set<CanonicalGrowerNumber>();
    public DbSet<CanonicalOrchard> CanonicalOrchards => Set<CanonicalOrchard>();
    public DbSet<CanonicalOrchardBlock> CanonicalOrchardBlocks => Set<CanonicalOrchardBlock>();
    public DbSet<CanonicalOrchardAlias> CanonicalOrchardAliases => Set<CanonicalOrchardAlias>();
    public DbSet<OrchardReportRecipient> OrchardReportRecipients => Set<OrchardReportRecipient>();
    public DbSet<OrchardManagerContact> OrchardManagerContacts => Set<OrchardManagerContact>();
    public DbSet<OrchardManagerAssignment> OrchardManagerAssignments => Set<OrchardManagerAssignment>();
    public DbSet<OrchardContactImportBatch> OrchardContactImportBatches => Set<OrchardContactImportBatch>();
    public DbSet<OrchardContactImportRow> OrchardContactImportRows => Set<OrchardContactImportRow>();
    public DbSet<OrchardBlockAlias> OrchardBlockAliases => Set<OrchardBlockAlias>();
    public DbSet<FruitProfile> FruitProfiles => Set<FruitProfile>();
    public DbSet<VarietyColorConfiguration> VarietyColorConfigurations => Set<VarietyColorConfiguration>();
    public DbSet<SampleType> SampleTypes => Set<SampleType>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<DefectType> DefectTypes => Set<DefectType>();
    public DbSet<StarchScale> StarchScales => Set<StarchScale>();
    public DbSet<StarchScaleValue> StarchScaleValues => Set<StarchScaleValue>();
    public DbSet<FruitSizeConversionThreshold> FruitSizeConversionThresholds => Set<FruitSizeConversionThreshold>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptInventoryOverride> ReceiptInventoryOverrides => Set<ReceiptInventoryOverride>();
    public DbSet<RoomDepletion> RoomDepletions => Set<RoomDepletion>();
    public DbSet<RoomInventoryAdjustment> RoomInventoryAdjustments => Set<RoomInventoryAdjustment>();
    public DbSet<InventoryDiagnosticAcknowledgment> InventoryDiagnosticAcknowledgments => Set<InventoryDiagnosticAcknowledgment>();
    public DbSet<RoomTransfer> RoomTransfers => Set<RoomTransfer>();
    public DbSet<BinsRunEntry> BinsRunEntries => Set<BinsRunEntry>();
    public DbSet<ActualRun> ActualRuns => Set<ActualRun>();
    public DbSet<ActualRunRevision> ActualRunRevisions => Set<ActualRunRevision>();
    public DbSet<ActualRunOverrideRequest> ActualRunOverrideRequests => Set<ActualRunOverrideRequest>();
    public DbSet<ActualRunOverrideRequestLine> ActualRunOverrideRequestLines => Set<ActualRunOverrideRequestLine>();
    public DbSet<RunExpectation> RunExpectations => Set<RunExpectation>();
    public DbSet<RunExpectationSource> RunExpectationSources => Set<RunExpectationSource>();
    public DbSet<RunProjection> RunProjections => Set<RunProjection>();
    public DbSet<RunProjectionSource> RunProjectionSources => Set<RunProjectionSource>();
    public DbSet<RunProjectionSizeResult> RunProjectionSizeResults => Set<RunProjectionSizeResult>();
    public DbSet<RunProjectionGradeResult> RunProjectionGradeResults => Set<RunProjectionGradeResult>();
    public DbSet<PackoutAnalysisConfiguration> PackoutAnalysisConfigurations => Set<PackoutAnalysisConfiguration>();
    public DbSet<PackCodeDefinition> PackCodeDefinitions => Set<PackCodeDefinition>();
    public DbSet<PackoutRun> PackoutRuns => Set<PackoutRun>();
    public DbSet<PackoutReportSource> PackoutReportSources => Set<PackoutReportSource>();
    public DbSet<PackoutReportLine> PackoutReportLines => Set<PackoutReportLine>();
    public DbSet<PackoutEmailAttempt> PackoutEmailAttempts => Set<PackoutEmailAttempt>();
    public DbSet<PackoutSourceAllocation> PackoutSourceAllocations => Set<PackoutSourceAllocation>();
    public DbSet<CommercialPackPlan> CommercialPackPlans => Set<CommercialPackPlan>();
    public DbSet<CommercialPackDefinition> CommercialPackDefinitions => Set<CommercialPackDefinition>();
    public DbSet<CommercialPackEligibleSize> CommercialPackEligibleSizes => Set<CommercialPackEligibleSize>();
    public DbSet<CommercialPackFruitProfileRestriction> CommercialPackFruitProfileRestrictions => Set<CommercialPackFruitProfileRestriction>();
    public DbSet<CommercialPackPlanItem> CommercialPackPlanItems => Set<CommercialPackPlanItem>();
    public DbSet<QcSample> QcSamples => Set<QcSample>();
    public DbSet<QcFruitReading> QcFruitReadings => Set<QcFruitReading>();
    public DbSet<QcFruitDefect> QcFruitDefects => Set<QcFruitDefect>();
    public DbSet<QcPhoto> QcPhotos => Set<QcPhoto>();
    public DbSet<QcSummaryEmailLog> QcSummaryEmailLogs => Set<QcSummaryEmailLog>();
    public DbSet<QcStation> QcStations => Set<QcStation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OfflineSyncItem> OfflineSyncItems => Set<OfflineSyncItem>();
    public DbSet<DashboardConfiguration> DashboardConfigurations => Set<DashboardConfiguration>();
    public DbSet<BackupRunRecord> BackupRunRecords => Set<BackupRunRecord>();
    public DbSet<BackupOperationLease> BackupOperationLeases => Set<BackupOperationLease>();
    public DbSet<BackupNightlyRunGuard> BackupNightlyRunGuards => Set<BackupNightlyRunGuard>();
    public DbSet<BackupNotificationRecord> BackupNotificationRecords => Set<BackupNotificationRecord>();
    public DbSet<ReceiptDeletionAudit> ReceiptDeletionAudits => Set<ReceiptDeletionAudit>();
    public DbSet<ReceiptPurgeOperation> ReceiptPurgeOperations => Set<ReceiptPurgeOperation>();
    public DbSet<FieldSampleDeletionAudit> FieldSampleDeletionAudits => Set<FieldSampleDeletionAudit>();
    public DbSet<EndOfDayFillReportGroup> EndOfDayFillReportGroups => Set<EndOfDayFillReportGroup>();
    public DbSet<EndOfDayFillReportRecipient> EndOfDayFillReportRecipients => Set<EndOfDayFillReportRecipient>();
    public DbSet<EndOfDayFillUserGroupAssignment> EndOfDayFillUserGroupAssignments => Set<EndOfDayFillUserGroupAssignment>();
    public DbSet<EndOfDayFillReportSend> EndOfDayFillReportSends => Set<EndOfDayFillReportSend>();
    public DbSet<EndOfDayFillSendReservation> EndOfDayFillSendReservations => Set<EndOfDayFillSendReservation>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (synchronizingDefectInspectionStatus)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        var affectedSamples = ResolveTrackedDefectAffectedSamples();
        var affectedSampleIds = ResolveDefectAffectedSampleIds();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        foreach (var sample in affectedSamples.Where(x => x.Id > 0)) affectedSampleIds.Add(sample.Id);
        SynchronizeDefectInspectionStatuses(affectedSampleIds);
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (synchronizingDefectInspectionStatus)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var affectedSamples = ResolveTrackedDefectAffectedSamples();
        var affectedSampleIds = await ResolveDefectAffectedSampleIdsAsync(cancellationToken);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var sample in affectedSamples.Where(x => x.Id > 0)) affectedSampleIds.Add(sample.Id);
        await SynchronizeDefectInspectionStatusesAsync(affectedSampleIds, cancellationToken);
        return result;
    }

    private List<QcSample> ResolveTrackedDefectAffectedSamples() =>
        ChangeTracker.Entries<QcFruitDefect>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity.QcFruitReading?.QcSample)
            .Where(x => x is not null)
            .Cast<QcSample>()
            .Distinct()
            .ToList();

    private HashSet<long> ResolveDefectAffectedSampleIds()
    {
        var changed = ChangeTracker.Entries<QcFruitDefect>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();
        var ids = changed
            .Where(x => x.QcFruitReading?.QcSampleId > 0)
            .Select(x => x.QcFruitReading.QcSampleId)
            .ToHashSet();
        var readingIds = changed.Select(x => x.QcFruitReadingId).Where(x => x > 0).Distinct().ToList();
        foreach (var sampleId in QcFruitReadings.AsNoTracking()
                     .Where(x => readingIds.Contains(x.Id))
                     .Select(x => x.QcSampleId))
        {
            ids.Add(sampleId);
        }
        return ids;
    }

    private async Task<HashSet<long>> ResolveDefectAffectedSampleIdsAsync(CancellationToken cancellationToken)
    {
        var changed = ChangeTracker.Entries<QcFruitDefect>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();
        var ids = changed
            .Where(x => x.QcFruitReading?.QcSampleId > 0)
            .Select(x => x.QcFruitReading.QcSampleId)
            .ToHashSet();
        var readingIds = changed.Select(x => x.QcFruitReadingId).Where(x => x > 0).Distinct().ToList();
        if (readingIds.Count > 0)
        {
            foreach (var sampleId in await QcFruitReadings.AsNoTracking()
                         .Where(x => readingIds.Contains(x.Id))
                         .Select(x => x.QcSampleId)
                         .ToListAsync(cancellationToken))
            {
                ids.Add(sampleId);
            }
        }
        return ids;
    }

    private void SynchronizeDefectInspectionStatuses(IReadOnlySet<long> sampleIds)
    {
        if (sampleIds.Count == 0) return;
        synchronizingDefectInspectionStatus = true;
        try
        {
            var samples = QcSamples
                .Include(x => x.FruitReadings)
                .ThenInclude(x => x.Defects)
                .Where(x => sampleIds.Contains(x.Id))
                .ToList();
            foreach (var sample in samples)
            {
                sample.DefectInspectionStatus = DefectInspectionStatuses.FromDefectCount(
                    sample.FruitReadings.Sum(x => x.Defects.Count));
            }
            base.SaveChanges();
        }
        finally
        {
            synchronizingDefectInspectionStatus = false;
        }
    }

    private async Task SynchronizeDefectInspectionStatusesAsync(IReadOnlySet<long> sampleIds, CancellationToken cancellationToken)
    {
        if (sampleIds.Count == 0) return;
        synchronizingDefectInspectionStatus = true;
        try
        {
            var samples = await QcSamples
                .Include(x => x.FruitReadings)
                .ThenInclude(x => x.Defects)
                .Where(x => sampleIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            foreach (var sample in samples)
            {
                sample.DefectInspectionStatus = DefectInspectionStatuses.FromDefectCount(
                    sample.FruitReadings.Sum(x => x.Defects.Count));
            }
            await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            synchronizingDefectInspectionStatus = false;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuth(modelBuilder);
        ConfigureMasterData(modelBuilder, IsPostgreSqlProvider());
        ConfigureQc(modelBuilder, IsPostgreSqlProvider());
        ConfigureCommercialPacks(modelBuilder);
        ConfigureRunProjections(modelBuilder);
        ConfigureRunExpectations(modelBuilder, IsPostgreSqlProvider());
        ConfigurePackoutReconciliation(modelBuilder);
        ConfigureAudit(modelBuilder);
        ConfigureDashboardConfiguration(modelBuilder);
        ConfigureBackups(modelBuilder);
        ConfigureReceiptDeletion(modelBuilder);
        ConfigureFieldSampleDeletion(modelBuilder);
        ConfigureEndOfDayFill(modelBuilder);
        SeedData(modelBuilder);
    }

    private static void ConfigureEndOfDayFill(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EndOfDayFillReportGroup>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Facility).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_EndOfDayFillReportGroups_Facility", "\"Facility\" IN ('WP', 'EBS')"));
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasIndex(x => x.EndOfDayFillReportGroupId);
            entity.HasOne(x => x.EndOfDayFillReportGroup)
                .WithMany(x => x.Rooms)
                .HasForeignKey(x => x.EndOfDayFillReportGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EndOfDayFillReportRecipient>(entity =>
        {
            entity.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmailAddress).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.NormalizedEmailAddress).IsUnique();
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EndOfDayFillUserGroupAssignment>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.ReportGroupId }).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.UserAssignments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ReportGroup).WithMany(x => x.UserAssignments).HasForeignKey(x => x.ReportGroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EndOfDayFillReportSend>(entity =>
        {
            entity.Property(x => x.ReportGroupName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Facility).HasMaxLength(10).IsRequired();
            entity.Property(x => x.SenderEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.SenderDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RecipientsJson).HasMaxLength(10000).IsRequired();
            entity.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SnapshotJson).HasMaxLength(500000).IsRequired();
            entity.Property(x => x.SuccessRevisionKey).HasMaxLength(200);
            entity.Property(x => x.SuccessSnapshotKey).HasMaxLength(250);
            entity.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            entity.Property(x => x.HtmlBody).HasMaxLength(1000000).IsRequired();
            entity.Property(x => x.TextBody).HasMaxLength(500000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(25).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(2000);
            entity.Property(x => x.GmailMessageId).HasMaxLength(500);
            entity.HasIndex(x => new { x.ReportGroupId, x.PacificReportDate, x.Status });
            entity.HasIndex(x => x.SuccessRevisionKey).IsUnique();
            entity.HasIndex(x => x.SuccessSnapshotKey);
            entity.HasOne(x => x.ReportGroup).WithMany(x => x.Sends).HasForeignKey(x => x.ReportGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SenderUser).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EndOfDayFillSendReservation>(entity =>
        {
            entity.HasKey(x => x.ReportGroupId);
            entity.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.ReportGroup).WithOne().HasForeignKey<EndOfDayFillSendReservation>(x => x.ReportGroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SendAttempt).WithOne().HasForeignKey<EndOfDayFillSendReservation>(x => x.SendAttemptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EndOfDayFillReportGroup>().HasData(
            new EndOfDayFillReportGroup { Id = 1, Name = "WP End of Day Fill", Facility = "WP", IsActive = true, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
            new EndOfDayFillReportGroup { Id = 2, Name = "EBS End of Day Fill", Facility = "EBS", IsActive = true, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        modelBuilder.Entity<EndOfDayFillReportRecipient>().HasData(
            new EndOfDayFillReportRecipient { Id = 1, EmailAddress = "wes@fruitandland.com", NormalizedEmailAddress = "WES@FRUITANDLAND.COM", IsActive = true, SortOrder = 10, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
            new EndOfDayFillReportRecipient { Id = 2, EmailAddress = "jorge@wp-packing.com", NormalizedEmailAddress = "JORGE@WP-PACKING.COM", IsActive = true, SortOrder = 20, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
            new EndOfDayFillReportRecipient { Id = 3, EmailAddress = "rob@earlbrownandsons.com", NormalizedEmailAddress = "ROB@EARLBROWNANDSONS.COM", IsActive = true, SortOrder = 30, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
    }

    private bool IsPostgreSqlProvider() =>
        Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static void ConfigureRunProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunProjection>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProjectionMode).HasMaxLength(25).HasDefaultValue(RunProjectionModes.Inventory).IsRequired();
            entity.Property(x => x.FacilityCodeSnapshot).HasMaxLength(25);
            entity.Property(x => x.PackPlanCodeSnapshot).HasMaxLength(50);
            entity.Property(x => x.PackPlanNameSnapshot).HasMaxLength(150);
            entity.Property(x => x.PackPlanTypeSnapshot).HasMaxLength(50);
            entity.Property(x => x.PackCalculationVersion).HasMaxLength(25);
            entity.Property(x => x.DeletionReason).HasMaxLength(1000);
            entity.Property(x => x.DeletedFromStatus).HasMaxLength(50);
            entity.Property(x => x.ApplePoundsPerBin).HasPrecision(10, 2);
            entity.Property(x => x.PearPoundsPerBin).HasPrecision(10, 2);
            entity.Property(x => x.StandardBoxWeightPounds).HasPrecision(10, 2);
            entity.Property(x => x.PeelerCullShare).HasPrecision(5, 4).HasDefaultValue(0.35m);
            entity.Property(x => x.JuiceCullShare).HasPrecision(5, 4).HasDefaultValue(0.40m);
            entity.Property(x => x.WasteCullShare).HasPrecision(5, 4).HasDefaultValue(0.25m);
            entity.Property(x => x.CullCalculationVersion).HasMaxLength(25).HasDefaultValue("1.0").IsRequired();
            entity.Property(x => x.TotalProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.TotalProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.TotalPackedProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.TotalPackedProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.TotalCullProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.TotalCullProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            entity.Property(x => x.CancelReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PlannedRunDate, x.Status });
            entity.HasIndex(x => new { x.CropYear, x.PlannedRunDate });
            entity.HasIndex(x => new { x.FacilityWarehouseId, x.PlannedRunDate, x.IsDeleted, x.Status });
            entity.HasIndex(x => new { x.CropYear, x.FacilityWarehouseId, x.IsDeleted });
            entity.HasIndex(x => x.DeletionOperationId);
            entity.HasIndex(x => x.SourceProjectionId);
            entity.HasIndex(x => x.CommercialPackPlanId);
            entity.HasOne(x => x.FacilityWarehouse)
                .WithMany()
                .HasForeignKey(x => x.FacilityWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceProjection)
                .WithMany(x => x.DerivedProjections)
                .HasForeignKey(x => x.SourceProjectionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CommercialPackPlan)
                .WithMany()
                .HasForeignKey(x => x.CommercialPackPlanId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CancelledByUser).WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RunProjectionSource>(entity =>
        {
            entity.Property(x => x.SourceType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.InventoryKey).HasMaxLength(250);
            entity.Property(x => x.GrowerLotKeySnapshot).HasMaxLength(300);
            entity.Property(x => x.SelectedQcSourceType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.Commodity).HasMaxLength(25).IsRequired();
            entity.Property(x => x.PoundsPerBinUsed).HasPrecision(10, 2);
            entity.Property(x => x.ProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.ProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedPackoutPercent).HasPrecision(5, 2);
            entity.Property(x => x.ExpectedCullPercent).HasPrecision(5, 2);
            entity.Property(x => x.PackedProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.PackedProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.CullProjectedPounds).HasPrecision(18, 2);
            entity.Property(x => x.CullProjectedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.SourceLabelSnapshot).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FacilitySnapshot).HasMaxLength(50);
            entity.Property(x => x.RoomSnapshot).HasMaxLength(100);
            entity.Property(x => x.LotSnapshot).HasMaxLength(100);
            entity.Property(x => x.OrchardSnapshot).HasMaxLength(200);
            entity.Property(x => x.GrowerSnapshot).HasMaxLength(200);
            entity.Property(x => x.GrowerNumberSnapshot).HasMaxLength(50);
            entity.Property(x => x.BlockSnapshot).HasMaxLength(150);
            entity.Property(x => x.VarietySnapshot).HasMaxLength(150).IsRequired();
            entity.Property(x => x.AverageWeightGramsSnapshot).HasPrecision(10, 2);
            entity.Property(x => x.AveragePressureLbsSnapshot).HasPrecision(10, 2);
            entity.Property(x => x.GradeSummarySnapshot).HasMaxLength(1000);
            entity.Property(x => x.DefectSummarySnapshot).HasMaxLength(1000);
            entity.Property(x => x.TotalDefectPercentageSnapshot).HasPrecision(8, 4);
            entity.Property(x => x.ProjectionWarning).HasMaxLength(1000);
            entity.Property(x => x.QcSampleTypeSnapshot).HasMaxLength(100);
            entity.Property(x => x.QcSampleStatusSnapshot).HasMaxLength(50);
            entity.Property(x => x.CalculationVersion).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => new { x.RunProjectionId, x.SortOrder });
            entity.HasIndex(x => x.InventoryKey);
            entity.HasIndex(x => x.GrowerLotKeySnapshot);
            entity.HasIndex(x => new { x.CanonicalOrchardBlockId, x.FruitProfileId });
            entity.HasIndex(x => x.SourceProjectionSourceId);
            entity.HasOne(x => x.RunProjection).WithMany(x => x.Sources).HasForeignKey(x => x.RunProjectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Receipt).WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SourceInventoryAdjustment).WithMany().HasForeignKey(x => x.SourceInventoryAdjustmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CanonicalOrchardBlock).WithMany().HasForeignKey(x => x.CanonicalOrchardBlockId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.FruitProfile).WithMany().HasForeignKey(x => x.FruitProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FieldSample).WithMany().HasForeignKey(x => x.FieldSampleId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SelectedQcSample).WithMany().HasForeignKey(x => x.SelectedQcSampleId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ActualBinsRunEntry).WithMany().HasForeignKey(x => x.ActualBinsRunEntryId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SourceProjectionSource)
                .WithMany(x => x.DerivedSources)
                .HasForeignKey(x => x.SourceProjectionSourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RunProjectionSizeResult>(entity =>
        {
            entity.Property(x => x.Commodity).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Percentage).HasPrecision(9, 6);
            entity.Property(x => x.UnroundedProjectedBoxes).HasPrecision(18, 6);
            entity.Property(x => x.PackedProjectedBoxes).HasPrecision(18, 6);
            entity.Property(x => x.CullProjectedBoxes).HasPrecision(18, 6);
            entity.HasIndex(x => new { x.RunProjectionSourceId, x.Commodity, x.SizeCategory }).IsUnique();
            entity.HasOne(x => x.RunProjectionSource).WithMany(x => x.SizeResults).HasForeignKey(x => x.RunProjectionSourceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunProjectionGradeResult>(entity =>
        {
            entity.Property(x => x.GradeCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Percentage).HasPrecision(9, 6);
            entity.Property(x => x.GrossProjectedBoxes).HasPrecision(18, 6);
            entity.Property(x => x.PackedProjectedBoxes).HasPrecision(18, 6);
            entity.Property(x => x.CullProjectedBoxes).HasPrecision(18, 6);
            entity.HasIndex(x => new { x.RunProjectionSourceId, x.GradeCode }).IsUnique();
            entity.HasOne(x => x.RunProjectionSource).WithMany(x => x.GradeResults).HasForeignKey(x => x.RunProjectionSourceId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePackoutReconciliation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PackoutAnalysisConfiguration>(entity =>
        {
            entity.Property(x => x.AppleBinWeightPounds).HasPrecision(10, 2).HasDefaultValue(880m);
            entity.Property(x => x.PearBinWeightPounds).HasPrecision(10, 2).HasDefaultValue(920m);
            entity.Property(x => x.SizeScoreWeight).HasPrecision(8, 4).HasDefaultValue(35m);
            entity.Property(x => x.GradeScoreWeight).HasPrecision(8, 4).HasDefaultValue(35m);
            entity.Property(x => x.PackoutScoreWeight).HasPrecision(8, 4).HasDefaultValue(21m);
            entity.Property(x => x.JuiceScoreWeight).HasPrecision(8, 4).HasDefaultValue(3m);
            entity.Property(x => x.PeelerSlicerScoreWeight).HasPrecision(8, 4).HasDefaultValue(3m);
            entity.Property(x => x.WasteScoreWeight).HasPrecision(8, 4).HasDefaultValue(3m);
            entity.Property(x => x.CurrentCropYearHistoryWeight).HasPrecision(8, 4).HasDefaultValue(80m);
            entity.Property(x => x.PriorCropYearHistoryWeight).HasPrecision(8, 4).HasDefaultValue(20m);
        });

        modelBuilder.Entity<PackCodeDefinition>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(75).IsRequired();
            entity.Property(x => x.NormalizedCode).HasMaxLength(75).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ProductCategory).HasMaxLength(50).IsRequired();
            entity.Property(x => x.NetWeightPounds).HasPrecision(10, 4);
            entity.HasIndex(x => x.NormalizedCode).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.ProductCategory });
        });

        modelBuilder.Entity<PackoutRun>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.FacilitySnapshot).HasMaxLength(50).IsRequired();
            entity.Property(x => x.LotNumberSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VarietySnapshot).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DumpedBins).HasPrecision(18, 4);
            entity.Property(x => x.PoundsPerBin).HasPrecision(10, 2);
            entity.Property(x => x.DumpedPounds).HasPrecision(18, 4);
            entity.Property(x => x.PackedProductPounds).HasPrecision(18, 4);
            entity.Property(x => x.JuicePounds).HasPrecision(18, 4);
            entity.Property(x => x.PeelerSlicerPounds).HasPrecision(18, 4);
            entity.Property(x => x.WastePounds).HasPrecision(18, 4);
            entity.Property(x => x.SupplementalJuicePounds).HasPrecision(18, 4);
            entity.Property(x => x.SupplementalPeelerSlicerPounds).HasPrecision(18, 4);
            entity.Property(x => x.SupplementalWastePounds).HasPrecision(18, 4);
            entity.Property(x => x.ActualPackoutPercent).HasPrecision(8, 4);
            entity.Property(x => x.ActualJuicePercent).HasPrecision(8, 4);
            entity.Property(x => x.ActualPeelerSlicerPercent).HasPrecision(8, 4);
            entity.Property(x => x.ActualWastePercent).HasPrecision(8, 4);
            entity.Property(x => x.SizeAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.GradeAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.PackoutAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.JuiceAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.PeelerSlicerAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.WasteAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.OverallAccuracyScore).HasPrecision(8, 4);
            entity.Property(x => x.ReconciliationDifferencePounds).HasPrecision(18, 4);
            entity.Property(x => x.ReviewNotes).HasMaxLength(2000);
            entity.Property(x => x.CalculationVersion).HasMaxLength(50);
            entity.Property(x => x.FinalReportFileName).HasMaxLength(255);
            entity.Property(x => x.FinalReportSha256).HasMaxLength(64);
            entity.Property(x => x.FinalEmailMessageId).HasMaxLength(250);
            entity.Property(x => x.ReopenReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.FacilitySnapshot, x.PackingDate, x.RunNumber }).IsUnique();
            entity.HasIndex(x => new { x.RunProjectionId, x.Status });
            entity.HasIndex(x => x.RunExpectationId);
            entity.HasIndex(x => x.BinsRunEntryId).IsUnique();
            entity.HasOne(x => x.RunProjection)
                .WithMany(x => x.LegacyPackoutRuns)
                .HasForeignKey(x => x.RunProjectionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ActualRun)
                .WithMany(x => x.PackoutRuns)
                .HasForeignKey(x => x.ActualRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RunExpectation)
                .WithMany(x => x.PackoutRuns)
                .HasForeignKey(x => x.RunExpectationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BinsRunEntry)
                .WithOne()
                .HasForeignKey<PackoutRun>(x => x.BinsRunEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackoutReportSource>(entity =>
        {
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ParserName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ParserVersion).HasMaxLength(50);
            entity.Property(x => x.Confidence).HasPrecision(6, 5);
            entity.Property(x => x.SafeDiagnostic).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PackoutRunId, x.Sha256 }).IsUnique();
            entity.HasOne(x => x.PackoutRun)
                .WithMany(x => x.Sources)
                .HasForeignKey(x => x.PackoutRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PackoutReportLine>(entity =>
        {
            entity.Property(x => x.RawText).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.RawPackCode).HasMaxLength(100);
            entity.Property(x => x.NormalizedPackCode).HasMaxLength(100);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.NetWeightPounds).HasPrecision(10, 4);
            entity.Property(x => x.ExtendedWeightPounds).HasPrecision(18, 4);
            entity.Property(x => x.ProductCategory).HasMaxLength(50);
            entity.Property(x => x.Confidence).HasPrecision(6, 5);
            entity.Property(x => x.CorrectionReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PackoutRunId, x.ProductCategory });
            entity.HasIndex(x => x.NormalizedPackCode);
            entity.HasOne(x => x.PackoutRun)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.PackoutRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PackoutReportSource)
                .WithMany()
                .HasForeignKey(x => x.PackoutReportSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PackoutEmailAttempt>(entity =>
        {
            entity.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
            entity.Property(x => x.MessageId).HasMaxLength(250);
            entity.Property(x => x.SafeError).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PackoutRunId, x.AttemptedAt });
            entity.HasOne(x => x.PackoutRun)
                .WithMany(x => x.EmailAttempts)
                .HasForeignKey(x => x.PackoutRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SenderUser)
                .WithMany()
                .HasForeignKey(x => x.SenderUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureRunExpectations(ModelBuilder modelBuilder, bool isPostgreSqlProvider)
    {
        modelBuilder.Entity<RunExpectation>(entity =>
        {
            entity.Property(x => x.FacilitySnapshot).HasMaxLength(50).IsRequired();
            entity.Property(x => x.GrossPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedPackoutPercent).HasPrecision(8, 4);
            entity.Property(x => x.ExpectedPackedPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedPackedBoxes).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedCullPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedJuicePounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedPeelerPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedWastePounds).HasPrecision(18, 4);
            entity.Property(x => x.ConfidencePercent).HasPrecision(8, 4);
            entity.Property(x => x.CalculationVersion).HasMaxLength(75).IsRequired();
            entity.HasIndex(x => x.ActualRunRevisionId).IsUnique();
            entity.HasIndex(x => new { x.ActualRunId, x.RevisionNumber }).IsUnique();
            entity.HasOne(x => x.ActualRun)
                .WithMany(x => x.Expectations)
                .HasForeignKey(x => x.ActualRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ActualRunRevision)
                .WithOne(x => x.RunExpectation)
                .HasForeignKey<RunExpectation>(x => x.ActualRunRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RunExpectationSource>(entity =>
        {
            entity.Property(x => x.FacilitySnapshot).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RoomSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GrowerSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VarietySnapshot).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProductionTypeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ContributionPercent).HasPrecision(9, 6);
            entity.Property(x => x.GrossPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedPackedPounds).HasPrecision(18, 4);
            entity.Property(x => x.ExpectedCullPounds).HasPrecision(18, 4);
            entity.Property(x => x.ConfidencePercent).HasPrecision(8, 4);
            entity.Property(x => x.WarningSnapshot).HasMaxLength(1000);
            entity.HasIndex(x => new { x.RunExpectationId, x.BinsRunEntryId }).IsUnique();
            entity.HasIndex(x => new { x.WarehouseId, x.RoomId, x.CropYearSnapshot, x.LotSnapshot, x.VarietySnapshot });
            entity.HasOne(x => x.RunExpectation)
                .WithMany(x => x.Sources)
                .HasForeignKey(x => x.RunExpectationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.BinsRunEntry)
                .WithMany()
                .HasForeignKey(x => x.BinsRunEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.QcSample)
                .WithMany()
                .HasForeignKey(x => x.QcSampleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackoutSourceAllocation>(entity =>
        {
            entity.Property(x => x.ContributionPercent).HasPrecision(9, 6);
            entity.Property(x => x.AllocatedPackedPounds).HasPrecision(18, 6);
            entity.Property(x => x.AllocatedResidualPounds).HasPrecision(18, 6);
            entity.Property(x => x.AllocatedJuicePounds).HasPrecision(18, 6);
            entity.Property(x => x.AllocatedPeelerPounds).HasPrecision(18, 6);
            entity.Property(x => x.AllocatedWastePounds).HasPrecision(18, 6);
            entity.Property(x => x.AllocationVersion).HasMaxLength(75).IsRequired();
            entity.HasIndex(x => new { x.PackoutRunId, x.RunExpectationSourceId }).IsUnique();
            entity.HasOne(x => x.PackoutRun)
                .WithMany(x => x.SourceAllocations)
                .HasForeignKey(x => x.PackoutRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.RunExpectationSource)
                .WithMany()
                .HasForeignKey(x => x.RunExpectationSourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        var currentPackoutIndex = modelBuilder.Entity<PackoutRun>()
            .HasIndex(x => x.ActualRunId)
            .IsUnique()
            .HasDatabaseName("UX_PackoutRuns_ActualRunId");
        currentPackoutIndex.HasFilter(isPostgreSqlProvider
            ? "\"ActualRunId\" IS NOT NULL"
            : "[ActualRunId] IS NOT NULL");
    }

    private static void ConfigureCommercialPacks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommercialPackPlan>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Commodity).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PlanType).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.Commodity, x.IsActive });
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CommercialPackDefinition>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Commodity).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PackType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PackageWeightPounds).HasPrecision(10, 4);
            entity.Property(x => x.MixRule).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.Commodity, x.IsActive });
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CommercialPackEligibleSize>(entity =>
        {
            entity.Property(x => x.TargetPercent).HasPrecision(7, 4);
            entity.Property(x => x.MinimumPercent).HasPrecision(7, 4);
            entity.Property(x => x.MaximumPercent).HasPrecision(7, 4);
            entity.HasIndex(x => new { x.CommercialPackDefinitionId, x.SizeCategory }).IsUnique();
            entity.HasOne(x => x.CommercialPackDefinition)
                .WithMany(x => x.EligibleSizes)
                .HasForeignKey(x => x.CommercialPackDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommercialPackFruitProfileRestriction>(entity =>
        {
            entity.HasKey(x => new { x.CommercialPackDefinitionId, x.FruitProfileId });
            entity.HasOne(x => x.CommercialPackDefinition)
                .WithMany(x => x.FruitProfileRestrictions)
                .HasForeignKey(x => x.CommercialPackDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommercialPackPlanItem>(entity =>
        {
            entity.HasKey(x => new { x.CommercialPackPlanId, x.CommercialPackDefinitionId });
            entity.HasIndex(x => new { x.CommercialPackPlanId, x.Priority });
            entity.HasOne(x => x.CommercialPackPlan)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.CommercialPackPlanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CommercialPackDefinition)
                .WithMany(x => x.PlanItems)
                .HasForeignKey(x => x.CommercialPackDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureBackups(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackupRunRecord>(entity =>
        {
            entity.Property(x => x.BackupType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.EnvironmentName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DatabaseProvider).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DeployedCommit).HasMaxLength(64);
            entity.Property(x => x.RequestedBy).HasMaxLength(320);
            entity.Property(x => x.RetentionCategory).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PackageFileName).HasMaxLength(260);
            entity.Property(x => x.PackageStorageKey).HasMaxLength(500);
            entity.Property(x => x.PackageWebUrl).HasMaxLength(2000);
            entity.Property(x => x.ManifestFileName).HasMaxLength(260);
            entity.Property(x => x.ManifestStorageKey).HasMaxLength(500);
            entity.Property(x => x.Sha256).HasMaxLength(64);
            entity.Property(x => x.ErrorSummary).HasMaxLength(2000);
            entity.Property(x => x.FailureStage).HasMaxLength(100);
            entity.Property(x => x.ScheduledPacificDate).HasMaxLength(10);
            entity.HasIndex(x => new { x.Status, x.StartedAt });
            entity.HasIndex(x => new { x.RetentionCategory, x.StartedAt });
            entity.HasIndex(x => x.ScheduledPacificDate);
        });

        modelBuilder.Entity<BackupOperationLease>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasData(new BackupOperationLease { Id = 1 });
        });

        modelBuilder.Entity<BackupNightlyRunGuard>(entity =>
        {
            entity.HasKey(x => x.PacificDate);
            entity.Property(x => x.PacificDate).HasMaxLength(10);
            entity.Property(x => x.Result).HasMaxLength(100);
            entity.HasIndex(x => x.BackupRunId);
        });

        modelBuilder.Entity<BackupNotificationRecord>(entity =>
        {
            entity.Property(x => x.NotificationType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.MessageId).HasMaxLength(500);
            entity.Property(x => x.ErrorSummary).HasMaxLength(1000);
            entity.HasIndex(x => new { x.BackupRunId, x.NotificationType }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasOne(x => x.BackupRun)
                .WithMany()
                .HasForeignKey(x => x.BackupRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureReceiptDeletion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReceiptDeletionAudit>(entity =>
        {
            entity.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DeletedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Result).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.OperationId);
            entity.HasIndex(x => new { x.CropYear, x.DeletedAt });
            entity.HasIndex(x => x.DeletedReceiptId);
        });

        modelBuilder.Entity<ReceiptPurgeOperation>(entity =>
        {
            entity.Property(x => x.RequestedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ErrorSummary).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TargetCropYear, x.StartedAt });
            entity.HasIndex(x => x.BackupRunId);
        });
    }

    private static void ConfigureFieldSampleDeletion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldSampleDeletionAudit>(entity =>
        {
            entity.Property(x => x.DeletedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DeletedAtPacific).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Result).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasIndex(x => new { x.DeletedFieldSampleId, x.DeletedAt });
            entity.HasIndex(x => x.BackupRunId);
            entity.HasOne<BackupRunRecord>()
                .WithMany()
                .HasForeignKey(x => x.BackupRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAuth(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.GoogleSubjectId).HasMaxLength(200);
            entity.Property(x => x.Domain).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.EmploymentFacility).HasMaxLength(25).HasDefaultValue(EmploymentFacilities.Unassigned).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.GoogleSubjectId);
            entity.HasIndex(x => x.EmploymentFacility);
            entity.HasOne(x => x.EmploymentUpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.EmploymentUpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserEmploymentHistory>(entity =>
        {
            entity.Property(x => x.PreviousEmploymentFacility).HasMaxLength(25).IsRequired();
            entity.Property(x => x.EmploymentFacility).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasOne(x => x.User)
                .WithMany(x => x.EmploymentHistory)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChangedByUser)
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserGoogleCredential>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.AccessTokenEncrypted).HasMaxLength(4000);
            entity.Property(x => x.RefreshTokenEncrypted).HasMaxLength(4000);
            entity.Property(x => x.Scope).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany(x => x.GoogleCredentials)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPageAccess>(entity =>
        {
            entity.Property(x => x.AreaKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AccessLevel).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.AreaKey }).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany(x => x.PageAccesses)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasIndex(x => x.UserId).IsUnique();
        });

        modelBuilder.Entity<RolePageAccess>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_RolePageAccesses_AccessLevel",
                "\"AccessLevel\" IN ('None', 'View', 'Create', 'Admin')"));
            entity.Property(x => x.AreaKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AccessLevel).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => new { x.RoleId, x.AreaKey }).IsUnique();
            entity.HasOne(x => x.Role)
                .WithMany(x => x.PageAccesses)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.Property(x => x.PermissionKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.RoleId, x.PermissionKey }).IsUnique();
        });
    }

    private static void ConfigureMasterData(ModelBuilder modelBuilder, bool isPostgreSqlProvider)
    {
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SubLocation).HasMaxLength(100);
            entity.Property(x => x.CropQcRoomName).HasMaxLength(100);
            entity.Property(x => x.CompuTechRoomCode).HasMaxLength(100);
            entity.Property(x => x.DisplayName).HasMaxLength(150);
            entity.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.WarehouseId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.WarehouseId, x.CompuTechRoomCode });
            entity.HasOne(x => x.Warehouse)
                .WithMany(x => x.Rooms)
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GrowerLot>(entity =>
        {
            entity.Property(x => x.Grower).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => new { x.Grower, x.LotNumber }).IsUnique();
        });

        modelBuilder.Entity<CanonicalGrower>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedKey).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.NormalizedKey);
            entity.HasOne(x => x.MergedIntoCanonicalGrower)
                .WithMany()
                .HasForeignKey(x => x.MergedIntoCanonicalGrowerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CanonicalGrowerAlias>(entity =>
        {
            entity.Property(x => x.AliasName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedAliasKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SourceSystem).HasMaxLength(100);
            entity.HasIndex(x => x.NormalizedAliasKey);
            entity.HasIndex(x => new { x.CanonicalGrowerId, x.NormalizedAliasKey });
            entity.HasOne(x => x.CanonicalGrower)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.CanonicalGrowerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CanonicalGrowerNumber>(entity =>
        {
            entity.Property(x => x.GrowerNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.NormalizedGrowerNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceSystem).HasMaxLength(100);
            entity.Property(x => x.Facility).HasMaxLength(100);
            entity.HasIndex(x => x.NormalizedGrowerNumber);
            entity.HasIndex(x => new { x.CanonicalGrowerId, x.NormalizedGrowerNumber });
            entity.HasOne(x => x.CanonicalGrower)
                .WithMany(x => x.GrowerNumbers)
                .HasForeignKey(x => x.CanonicalGrowerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CanonicalOrchardBlock>(entity =>
        {
            entity.Property(x => x.OrchardName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CanonicalBlockName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.NormalizedOrchardKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedBlockKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => new { x.NormalizedOrchardKey, x.NormalizedBlockKey }).IsUnique();
            entity.HasOne(x => x.CanonicalOrchard)
                .WithMany(x => x.Blocks)
                .HasForeignKey(x => x.CanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CanonicalGrower)
                .WithMany()
                .HasForeignKey(x => x.CanonicalGrowerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CanonicalOrchard>(entity =>
        {
            entity.Property(x => x.OrchardName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedOrchardKey).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.NormalizedOrchardKey).IsUnique();
        });

        modelBuilder.Entity<CanonicalOrchardAlias>(entity =>
        {
            entity.Property(x => x.AliasText).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedAlias).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(1000);
            entity.HasIndex(x => x.NormalizedAlias);
            entity.HasIndex(x => new { x.CanonicalOrchardId, x.NormalizedAlias }).IsUnique();
            entity.HasOne(x => x.CanonicalOrchard)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.CanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrchardReportRecipient>(entity =>
        {
            entity.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmailAddress).HasMaxLength(320).IsRequired();
            var uniqueRecipient = entity.HasIndex(x => new { x.CanonicalOrchardId, x.NormalizedEmailAddress }).IsUnique();
            uniqueRecipient.HasFilter(isPostgreSqlProvider ? "\"IsDeleted\" = FALSE" : "[IsDeleted] = 0");
            entity.HasIndex(x => new { x.CanonicalOrchardId, x.IsActive, x.IsDeleted });
            entity.HasOne(x => x.CanonicalOrchard)
                .WithMany(x => x.ReportRecipients)
                .HasForeignKey(x => x.CanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrchardManagerContact>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.NormalizedEmailAddress).HasMaxLength(320);
            entity.Property(x => x.Phone).HasMaxLength(50);
            entity.Property(x => x.NormalizedPhone).HasMaxLength(25);
            entity.Property(x => x.CommunicationNote).HasMaxLength(1000);
            entity.Property(x => x.SourceWorkbook).HasMaxLength(260).IsRequired();
            entity.Property(x => x.SourceWorksheet).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.NormalizedEmailAddress);
            entity.HasIndex(x => new { x.NormalizedDisplayName, x.NormalizedPhone });
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrchardManagerAssignment>(entity =>
        {
            entity.HasIndex(x => new { x.CanonicalOrchardId, x.OrchardManagerContactId }).IsUnique();
            entity.HasOne(x => x.CanonicalOrchard)
                .WithMany(x => x.ManagerAssignments)
                .HasForeignKey(x => x.CanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OrchardManagerContact)
                .WithMany(x => x.OrchardAssignments)
                .HasForeignKey(x => x.OrchardManagerContactId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OrchardReportRecipient)
                .WithMany()
                .HasForeignKey(x => x.OrchardReportRecipientId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SourceImportRow)
                .WithMany()
                .HasForeignKey(x => x.SourceImportRowId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrchardContactImportBatch>(entity =>
        {
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.WorkbookSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.WorksheetName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ImportReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.WorkbookSha256, x.WorksheetName });
            entity.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.AppliedByUser).WithMany().HasForeignKey(x => x.AppliedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.VerifiedBackupRun).WithMany().HasForeignKey(x => x.VerifiedBackupRunId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrchardContactImportRow>(entity =>
        {
            entity.Property(x => x.OriginalOrchardCell).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ParsedOrchardToken).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ManagerDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NormalizedManagerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.NormalizedEmailAddress).HasMaxLength(320);
            entity.Property(x => x.Phone).HasMaxLength(50);
            entity.Property(x => x.NormalizedPhone).HasMaxLength(25);
            entity.Property(x => x.PhysicalAddress).HasMaxLength(1000);
            entity.Property(x => x.CommunicationNote).HasMaxLength(1000);
            entity.Property(x => x.SourceStatusNote).HasMaxLength(2000);
            entity.Property(x => x.MatchMethod).HasMaxLength(50).IsRequired();
            entity.Property(x => x.MatchScore).HasPrecision(6, 4);
            entity.Property(x => x.Warning).HasMaxLength(2000);
            entity.Property(x => x.ReviewDecision).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(2000);
            entity.Property(x => x.AppliedAction).HasMaxLength(1000);
            entity.HasIndex(x => new { x.OrchardContactImportBatchId, x.WorkbookRowNumber });
            entity.HasIndex(x => new { x.OrchardContactImportBatchId, x.ReviewDecision });
            entity.HasOne(x => x.OrchardContactImportBatch)
                .WithMany(x => x.Rows)
                .HasForeignKey(x => x.OrchardContactImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SuggestedCanonicalOrchard)
                .WithMany()
                .HasForeignKey(x => x.SuggestedCanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ApprovedCanonicalOrchard)
                .WithMany()
                .HasForeignKey(x => x.ApprovedCanonicalOrchardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.OrchardManagerContact).WithMany().HasForeignKey(x => x.OrchardManagerContactId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.OrchardReportRecipient).WithMany().HasForeignKey(x => x.OrchardReportRecipientId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrchardBlockAlias>(entity =>
        {
            entity.Property(x => x.AliasName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.NormalizedAliasKey).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => new { x.CanonicalOrchardBlockId, x.NormalizedAliasKey }).IsUnique();
            entity.HasOne(x => x.CanonicalOrchardBlock)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.CanonicalOrchardBlockId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FruitProfile>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.VarietyCode).HasMaxLength(25).IsRequired();
            entity.Property(x => x.FruitType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.ProductionType).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => x.VarietyCode).IsUnique();
        });

        modelBuilder.Entity<VarietyColorConfiguration>(entity =>
        {
            entity.Property(x => x.FruitProfileId);
            entity.Property(x => x.VarietyKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VarietyName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.HexColor).HasMaxLength(7).IsRequired();
            entity.HasIndex(x => x.VarietyKey).IsUnique();
            entity.HasIndex(x => x.FruitProfileId);
            entity.HasOne(x => x.FruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SampleType>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Grade>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<DefectType>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<StarchScale>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FruitType).HasMaxLength(25);
            entity.HasIndex(x => new { x.Name, x.FruitType, x.FruitProfileId }).IsUnique();
        });

        modelBuilder.Entity<StarchScaleValue>(entity =>
        {
            entity.Property(x => x.Value).HasPrecision(4, 1);
            entity.HasIndex(x => new { x.StarchScaleId, x.Value }).IsUnique();
            entity.HasOne(x => x.StarchScale)
                .WithMany(x => x.Values)
                .HasForeignKey(x => x.StarchScaleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FruitSizeConversionThreshold>(entity =>
        {
            entity.Property(x => x.FruitType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.MinimumWeightGrams).HasPrecision(8, 4);
            entity.HasIndex(x => new { x.FruitType, x.SizeCategory }).IsUnique();
            entity.HasIndex(x => new { x.FruitType, x.MinimumWeightGrams });
        });
    }

    private static void ConfigureQc(ModelBuilder modelBuilder, bool isPostgreSqlProvider)
    {
        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.Property(x => x.CompuTechReceiptId).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReceiptType).HasMaxLength(50).HasDefaultValue("Truck receipt").IsRequired();
            entity.Property(x => x.GrowerNumber).HasMaxLength(50);
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DeleteReason).HasMaxLength(1000);
            entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            entity.HasIndex(x => x.CompuTechReceiptId);
            entity.HasIndex(x => new { x.CropYear, x.IsDeleted });
            entity.HasOne(x => x.Warehouse)
                .WithMany(x => x.Receipts)
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FruitProfile)
                .WithMany(x => x.Receipts)
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.GrowerLot)
                .WithMany()
                .HasForeignKey(x => x.GrowerLotId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CanonicalOrchardBlock)
                .WithMany()
                .HasForeignKey(x => x.CanonicalOrchardBlockId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReceiptInventoryOverride>(entity =>
        {
            entity.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.OperationKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.VoidConfirmationDetails).HasMaxLength(1000);
            entity.Property(x => x.BeforeReceiptSnapshotJson).IsRequired();
            entity.Property(x => x.AfterReceiptSnapshotJson).IsRequired();
            entity.Property(x => x.AffectedInventorySnapshotJson).IsRequired();
            entity.HasIndex(x => x.OperationKey).IsUnique();
            entity.HasIndex(x => new { x.ReceiptId, x.CreatedAt });
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.InventoryOverrides)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AdministratorUser)
                .WithMany()
                .HasForeignKey(x => x.AdministratorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoomDepletion>(entity =>
        {
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Destination).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.VoidReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.RoomId, x.IsVoided, x.DepletedAt });
            entity.HasIndex(x => new { x.ReceiptId, x.IsVoided });
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.RoomDepletions)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.VoidedByUser)
                .WithMany()
                .HasForeignKey(x => x.VoidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RoomInventoryAdjustment>(entity =>
        {
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.VarietyCode).HasMaxLength(50);
            entity.Property(x => x.AdjustmentType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(150);
            entity.Property(x => x.SourceRoomCode).HasMaxLength(100);
            entity.Property(x => x.SourceSubLocation).HasMaxLength(100);
            entity.Property(x => x.InventoryStatus).HasMaxLength(100);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.InventoryOperationKey).HasMaxLength(200);
            entity.HasIndex(x => x.WarehouseId);
            entity.HasIndex(x => new { x.RoomId, x.AdjustmentAt });
            entity.HasIndex(x => new { x.ReceiptId, x.AdjustmentAt });
            entity.HasIndex(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.LotNumber, x.VarietyCode, x.AdjustmentAt });
            entity.HasIndex(x => new { x.ActualRunId, x.ActualRunRevisionId });
            entity.HasIndex(x => x.ReceiptInventoryOverrideId);
            var operationKeyIndex = entity.HasIndex(x => x.InventoryOperationKey).IsUnique();
            operationKeyIndex.HasFilter(isPostgreSqlProvider
                ? "\"InventoryOperationKey\" IS NOT NULL"
                : "[InventoryOperationKey] IS NOT NULL");
            var transferSideIndex = entity.HasIndex(x => new { x.RoomTransferId, x.AdjustmentType }).IsUnique();
            transferSideIndex.HasFilter(isPostgreSqlProvider
                ? "\"RoomTransferId\" IS NOT NULL"
                : "[RoomTransferId] IS NOT NULL");
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.RoomInventoryAdjustments)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RoomDepletion)
                .WithMany()
                .HasForeignKey(x => x.RoomDepletionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.GrowerLot)
                .WithMany()
                .HasForeignKey(x => x.GrowerLotId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.FruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RoomTransfer)
                .WithMany(x => x.InventoryAdjustments)
                .HasForeignKey(x => x.RoomTransferId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReceiptInventoryOverride)
                .WithMany(x => x.InventoryAdjustments)
                .HasForeignKey(x => x.ReceiptInventoryOverrideId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ActualRun)
                .WithMany()
                .HasForeignKey(x => x.ActualRunId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ActualRunRevision)
                .WithMany(x => x.InventoryAdjustments)
                .HasForeignKey(x => x.ActualRunRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BinsRunEntry>(entity =>
        {
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.VarietyCode).HasMaxLength(50);
            entity.Property(x => x.InventoryStatus).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.ReverseReason).HasMaxLength(1000);
            entity.Property(x => x.TransactionType).HasMaxLength(25).HasDefaultValue(ActualRunTransactionTypes.Legacy).IsRequired();
            entity.Property(x => x.OverrideReason).HasMaxLength(1000);
            entity.Property(x => x.ReportingFacilityAssignmentSource).HasMaxLength(50);
            entity.Property(x => x.ReportingFacilityCodeSnapshot).HasMaxLength(25);
            entity.Property(x => x.ProductionTypeSnapshot).HasMaxLength(50);
            entity.Property(x => x.GrowerNumberSnapshot).HasMaxLength(50);
            entity.Property(x => x.ReportingVarietyCodeSnapshot).HasMaxLength(100);
            entity.HasIndex(x => new { x.RoomId, x.RunAt });
            entity.HasIndex(x => new { x.ReceiptId, x.IsReversed });
            entity.HasIndex(x => new { x.ActualRunId, x.ActualRunRevisionId, x.TransactionType });
            entity.HasIndex(x => x.InventoryAdjustmentId)
                .IsUnique()
                .HasDatabaseName("UX_BinsRunEntries_InventoryAdjustmentId_Invariant");
            entity.HasIndex(x => x.ReversesBinsRunEntryId);
            entity.HasIndex(x => new { x.ReportingFacilityWarehouseId, x.ReportingCropYearSnapshot, x.RunAt });
            entity.HasOne(x => x.Receipt)
                .WithMany()
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SourceInventoryAdjustment)
                .WithMany()
                .HasForeignKey(x => x.SourceInventoryAdjustmentId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.InventoryAdjustment)
                .WithMany()
                .HasForeignKey(x => x.InventoryAdjustmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Warehouse)
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.GrowerLot)
                .WithMany()
                .HasForeignKey(x => x.GrowerLotId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.FruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FruitProfileId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReversedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReversedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReconciledByUser)
                .WithMany()
                .HasForeignKey(x => x.ReconciledByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ActualRun)
                .WithMany(x => x.Entries)
                .HasForeignKey(x => x.ActualRunId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ActualRunRevision)
                .WithMany(x => x.Entries)
                .HasForeignKey(x => x.ActualRunRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReversesBinsRunEntry)
                .WithMany()
                .HasForeignKey(x => x.ReversesBinsRunEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OverrideApprovedByUser)
                .WithMany()
                .HasForeignKey(x => x.OverrideApprovedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReportingFacilityWarehouse)
                .WithMany()
                .HasForeignKey(x => x.ReportingFacilityWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReportingFacilityAssignedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReportingFacilityAssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RoomTransfer>(entity =>
        {
            entity.Property(x => x.OperationKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.VarietyCode).HasMaxLength(50);
            entity.Property(x => x.InventoryStatus).HasMaxLength(100);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.ReverseReason).HasMaxLength(1000);
            entity.HasIndex(x => x.OperationKey).IsUnique();
            entity.HasIndex(x => x.ReversesRoomTransferId).IsUnique();
            entity.HasIndex(x => new { x.SourceRoomId, x.TransferredAt });
            entity.HasIndex(x => new { x.DestinationRoomId, x.TransferredAt });
            entity.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceRoom).WithMany().HasForeignKey(x => x.SourceRoomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DestinationRoom).WithMany().HasForeignKey(x => x.DestinationRoomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.GrowerLotId);
            entity.HasOne(x => x.FruitProfile).WithMany().HasForeignKey(x => x.FruitProfileId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReversedByUser).WithMany().HasForeignKey(x => x.ReversedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ReversesRoomTransfer)
                .WithOne(x => x.ReversalTransfer)
                .HasForeignKey<RoomTransfer>(x => x.ReversesRoomTransferId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActualRun>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.CancellationReason).HasMaxLength(1000);
            entity.Property(x => x.RunFacilityAssignmentSource).HasMaxLength(50);
            entity.Property(x => x.RunFacilityCodeSnapshot).HasMaxLength(25);
            entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Status, x.RunAt });
            entity.HasIndex(x => x.RunProjectionId);
            entity.HasIndex(x => new { x.RunFacilityWarehouseId, x.Status, x.RunAt });
            entity.HasOne(x => x.RunProjection).WithMany().HasForeignKey(x => x.RunProjectionId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.CanceledByUser).WithMany().HasForeignKey(x => x.CanceledByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RunFacilityWarehouse).WithMany().HasForeignKey(x => x.RunFacilityWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RunFacilityAssignedByUser).WithMany().HasForeignKey(x => x.RunFacilityAssignedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ActualRunRevision>(entity =>
        {
            entity.Property(x => x.OperationType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.OperationKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.HasIndex(x => x.OperationKey).IsUnique();
            entity.HasIndex(x => new { x.ActualRunId, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.ActualRunId, x.IsCurrent });
            entity.HasOne(x => x.ActualRun).WithMany(x => x.Revisions).HasForeignKey(x => x.ActualRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ActualRunOverrideRequest>(entity =>
        {
            entity.Property(x => x.OperationType).HasMaxLength(25).IsRequired();
            entity.Property(x => x.OperationKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.ApprovalReason).HasMaxLength(1000);
            entity.Property(x => x.RunFacilityCodeSnapshot).HasMaxLength(25);
            entity.Property(x => x.RunFacilityAssignmentSource).HasMaxLength(50);
            entity.HasIndex(x => x.OperationKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.RequestedAt });
            entity.HasOne(x => x.ActualRun).WithMany().HasForeignKey(x => x.ActualRunId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RunProjection).WithMany().HasForeignKey(x => x.RunProjectionId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ApprovedByUser).WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RunFacilityWarehouse).WithMany().HasForeignKey(x => x.RunFacilityWarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActualRunOverrideRequestLine>(entity =>
        {
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PoolStart).HasMaxLength(20);
            entity.Property(x => x.VarietyCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.InventoryStatus).HasMaxLength(100);
            entity.HasIndex(x => new { x.ActualRunOverrideRequestId, x.RoomId, x.LotNumber, x.VarietyCode });
            entity.HasOne(x => x.ActualRunOverrideRequest).WithMany(x => x.Lines).HasForeignKey(x => x.ActualRunOverrideRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QcSample>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.StarchStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PhotoStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EmailStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DefectInspectionStatus)
                .HasMaxLength(50)
                .HasDefaultValue(DefectInspectionStatuses.NoDefectsFound)
                .IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.DeleteReason).HasMaxLength(1000);
            entity.Property(x => x.FieldSampleGrowerName).HasMaxLength(200);
            entity.Property(x => x.FieldSampleGrowerNumber).HasMaxLength(50);
            entity.Property(x => x.FieldSampleOriginalBlockName).HasMaxLength(150);
            entity.Property(x => x.FieldSampleBlockResolution).HasMaxLength(50);
            entity.Property(x => x.FieldSampleAutosaveVersion).HasDefaultValue(0L);
            var receiptSequenceIndex = entity.HasIndex(x => new { x.ReceiptId, x.SampleSequenceNumber }).IsUnique();
            receiptSequenceIndex.HasFilter(isPostgreSqlProvider ? "\"ReceiptId\" IS NOT NULL" : "[ReceiptId] IS NOT NULL");
            entity.HasIndex(x => new { x.ReceiptId, x.IsDeleted });
            entity.HasIndex(x => new { x.CanonicalOrchardBlockId, x.SampleTypeId, x.SampleTakenAt });
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.Samples)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SampleType)
                .WithMany(x => x.Samples)
                .HasForeignKey(x => x.SampleTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FieldSampleFruitProfile)
                .WithMany()
                .HasForeignKey(x => x.FieldSampleFruitProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CanonicalOrchardBlock)
                .WithMany(x => x.FieldSamples)
                .HasForeignKey(x => x.CanonicalOrchardBlockId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QcFruitReading>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_QcFruitReadings_RowNumber_1_50", isPostgreSqlProvider
                    ? "\"RowNumber\" >= 1 AND \"RowNumber\" <= 50"
                    : "[RowNumber] >= 1 AND [RowNumber] <= 50");
                table.HasCheckConstraint("CK_QcFruitReadings_CompletedRequiresCoreFields", isPostgreSqlProvider
                    ? "(\"IsCompleted\" = FALSE) OR (\"Pressure1Lbs\" IS NOT NULL AND \"Pressure2Lbs\" IS NOT NULL AND \"WeightGrams\" IS NOT NULL AND \"GradeId\" IS NOT NULL)"
                    : "([IsCompleted] = 0) OR ([Pressure1Lbs] IS NOT NULL AND [Pressure2Lbs] IS NOT NULL AND [WeightGrams] IS NOT NULL AND [GradeId] IS NOT NULL)");
            });
            entity.Property(x => x.Pressure1Lbs).HasPrecision(6, 2);
            entity.Property(x => x.Pressure1Source).HasMaxLength(50);
            entity.Property(x => x.Pressure2Lbs).HasPrecision(6, 2);
            entity.Property(x => x.Pressure2Source).HasMaxLength(50);
            entity.Property(x => x.WeightGrams).HasPrecision(8, 4);
            entity.Property(x => x.SizeStatus).HasMaxLength(25).HasDefaultValue("NotCalculated").IsRequired();
            entity.Property(x => x.DefectsInspected).HasDefaultValue(false);
            entity.Property(x => x.FieldVersion).HasDefaultValue(0L);
            entity.HasIndex(x => new { x.QcSampleId, x.RowNumber }).IsUnique();
            entity.HasOne(x => x.Grade)
                .WithMany()
                .HasForeignKey(x => x.GradeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StarchScaleValue)
                .WithMany()
                .HasForeignKey(x => x.StarchScaleValueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QcFruitDefect>(entity =>
        {
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.QcFruitReadingId, x.DefectTypeId }).IsUnique();
            entity.HasOne(x => x.DefectType)
                .WithMany()
                .HasForeignKey(x => x.DefectTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QcPhoto>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_QcPhotos_ReceiptOrSample", isPostgreSqlProvider
                    ? "(\"ReceiptId\" IS NOT NULL AND \"QcSampleId\" IS NULL) OR (\"ReceiptId\" IS NULL AND \"QcSampleId\" IS NOT NULL)"
                    : "([ReceiptId] IS NOT NULL AND [QcSampleId] IS NULL) OR ([ReceiptId] IS NULL AND [QcSampleId] IS NOT NULL)");
            });
            entity.Property(x => x.PhotoType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PhotoSource).HasMaxLength(50).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.StorageProvider).HasMaxLength(50).HasDefaultValue("Legacy").IsRequired();
            entity.Property(x => x.DriveId).HasMaxLength(200);
            entity.Property(x => x.FileId).HasMaxLength(200);
            entity.Property(x => x.FolderId).HasMaxLength(200);
            entity.Property(x => x.SharePointDriveId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SharePointItemId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.WebUrl).HasMaxLength(1000);
            entity.Property(x => x.DeleteReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.SharePointDriveId, x.SharePointItemId }).IsUnique();
            entity.HasIndex(x => new { x.StorageProvider, x.FileId });
            entity.HasIndex(x => new { x.QcSampleId, x.IsDeleted });
            entity.HasIndex(x => new { x.ReceiptId, x.IsDeleted });
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.QcSample)
                .WithMany(x => x.Photos)
                .HasForeignKey(x => x.QcSampleId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DeletedByUser)
                .WithMany()
                .HasForeignKey(x => x.DeletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QcSummaryEmailLog>(entity =>
        {
            entity.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ToAddress).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ReplyToAddress).HasMaxLength(320);
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.MessageId).HasMaxLength(500);
            entity.Property(x => x.ResendReason).HasMaxLength(1000);
            entity.Property(x => x.OverrideReason).HasMaxLength(1000);
            entity.Property(x => x.ReportSnapshotReference).HasMaxLength(1000);
            entity.HasIndex(x => x.ReceiptId);
            entity.HasIndex(x => x.QcSampleId);
            entity.HasOne(x => x.Receipt)
                .WithMany(x => x.SummaryEmailLogs)
                .HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.QcSample)
                .WithMany(x => x.SummaryEmailLogs)
                .HasForeignKey(x => x.QcSampleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QcStation>(entity =>
        {
            entity.Property(x => x.StationCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.StationName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.WarehouseCode).HasMaxLength(25);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.DeviceIdentifier).HasMaxLength(200);
            entity.Property(x => x.ApiKeyHash).HasMaxLength(200);
            entity.Property(x => x.ApiKeyLastFour).HasMaxLength(12);
            entity.Property(x => x.LastSeenIp).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.StationCode).IsUnique();
        });

        modelBuilder.Entity<OfflineSyncItem>(entity =>
        {
            entity.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.LocalEntityId).HasMaxLength(150).IsRequired();
            entity.Property(x => x.SyncStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.HasIndex(x => new { x.QcStationId, x.EntityName, x.LocalEntityId }).IsUnique();
        });
    }

    private static void ConfigureAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.EntityKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.SourceApplication).HasMaxLength(100);
            entity.HasIndex(x => new { x.EntityName, x.EntityKey });
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<InventoryDiagnosticAcknowledgment>(entity =>
        {
            entity.Property(x => x.DiagnosticKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DiagnosticType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DiagnosticCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DiagnosticMessage).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.DiagnosticSnapshotJson).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.DismissedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.RestoredByEmail).HasMaxLength(320);
            entity.HasIndex(x => x.DiagnosticKey)
                .HasDatabaseName("IX_InventoryDiagnosticAck_Key")
                .IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.RoomInventoryAdjustmentId })
                .HasDatabaseName("IX_InventoryDiagnosticAck_ActiveAdjustment");
            entity.HasIndex(x => x.RoomInventoryAdjustmentId)
                .HasDatabaseName("IX_InventoryDiagnosticAck_Adjustment");
            entity.HasIndex(x => x.DismissedByUserId)
                .HasDatabaseName("IX_InventoryDiagnosticAck_DismissedBy");
            entity.HasIndex(x => x.RestoredByUserId)
                .HasDatabaseName("IX_InventoryDiagnosticAck_RestoredBy");
            entity.HasOne(x => x.RoomInventoryAdjustment)
                .WithMany(x => x.DiagnosticAcknowledgments)
                .HasForeignKey(x => x.RoomInventoryAdjustmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_InventoryDiagnosticAck_Adjustment");
            entity.HasOne(x => x.DismissedByUser)
                .WithMany()
                .HasForeignKey(x => x.DismissedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_InventoryDiagnosticAck_DismissedBy");
            entity.HasOne(x => x.RestoredByUser)
                .WithMany()
                .HasForeignKey(x => x.RestoredByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_InventoryDiagnosticAck_RestoredBy");
        });
    }

    private static void ConfigureDashboardConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DashboardConfiguration>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ValueType).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var createdAt = new DateTimeOffset(2026, 5, 21, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<PasswordPolicy>().HasData(new PasswordPolicy
        {
            Id = 1,
            MinimumLength = 8,
            RequireUppercase = true,
            RequireLowercase = true,
            RequireNumber = true,
            RequireSymbol = true,
            PasswordExpirationDays = 365,
            CreatedAt = createdAt
        });

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = BuiltInRoleNames.Admin, NormalizedName = "ADMIN", Description = "Full dashboard and configuration access.", IsSystemRole = true, IsActive = true },
            new Role { Id = 2, Name = BuiltInRoleNames.Manager, NormalizedName = "MANAGER", Description = "Broad operational management without security administration.", IsSystemRole = true, IsActive = true },
            new Role { Id = 3, Name = BuiltInRoleNames.QcTech, NormalizedName = "QC TECH", Description = "Capture receiving samples and QC readings.", IsSystemRole = true, IsActive = true },
            new Role { Id = 4, Name = BuiltInRoleNames.Viewer, NormalizedName = "VIEWER", Description = "Read-only operational visibility.", IsSystemRole = true, IsActive = true },
            new Role { Id = 5, Name = BuiltInRoleNames.QcAdmin, NormalizedName = "QC ADMIN", Description = "QC workflow and QC configuration administration without system security access.", IsSystemRole = true, IsActive = true });

        modelBuilder.Entity<RolePageAccess>().HasData(BuildBuiltInRoleAccessSeed(createdAt));

        modelBuilder.Entity<Warehouse>().HasData(
            new Warehouse { Id = 1, Code = "EBS", Name = "EBS", IsActive = true },
            new Warehouse { Id = 2, Code = "DH", Name = "DH", IsActive = true },
            new Warehouse { Id = 3, Code = "McDougall", Name = "McDougall", IsActive = true },
            new Warehouse { Id = 4, Code = "WP", Name = "WP", IsActive = true });

        modelBuilder.Entity<Grade>().HasData(
            new Grade { Id = 1, Code = "W1", Name = "W1", IsActive = true },
            new Grade { Id = 2, Code = "W2", Name = "W2", IsActive = true },
            new Grade { Id = 3, Code = "W3", Name = "W3", IsActive = true },
            new Grade { Id = 4, Code = "W4", Name = "W4", IsActive = true },
            new Grade { Id = 5, Code = "WF", Name = "WF", IsActive = true },
            new Grade { Id = 6, Code = "US1", Name = "US1", IsActive = true },
            new Grade { Id = 7, Code = "US2", Name = "US2", IsActive = true },
            new Grade { Id = 8, Code = "USF", Name = "USF", IsActive = true });

        modelBuilder.Entity<DefectType>().HasData(
            new DefectType { Id = 1, Name = "Bruise", IsActive = true },
            new DefectType { Id = 2, Name = "Sunburn", IsActive = true },
            new DefectType { Id = 3, Name = "Bitter Pit", IsActive = true },
            new DefectType { Id = 4, Name = "Scald", IsActive = true },
            new DefectType { Id = 5, Name = "Decay", IsActive = true },
            new DefectType { Id = 6, Name = "Puncture", IsActive = true },
            new DefectType { Id = 7, Name = "Watercore", IsActive = true },
            new DefectType { Id = 8, Name = "Limb Rub", IsActive = true },
            new DefectType { Id = 9, Name = "Stem Bowl Crack", IsActive = true },
            new DefectType { Id = 10, Name = "Internal Browning", IsActive = true },
            new DefectType { Id = 11, Name = "Other", IsActive = true });

        modelBuilder.Entity<SampleType>().HasData(
            new SampleType { Id = 1, Name = "Receiving Sample", IsActive = true },
            new SampleType { Id = 2, Name = "Door Sample", IsActive = true },
            new SampleType { Id = 3, Name = "Line QC Sample", IsActive = true },
            new SampleType { Id = 4, Name = "Lot Sample", IsActive = true },
            new SampleType { Id = 5, Name = "Field Sample", IsActive = true });

        SeedFruitProfiles(modelBuilder);
        SeedStarchScale(modelBuilder);
        SeedSizeThresholds(modelBuilder);
    }

    private static void SeedFruitProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FruitProfile>().HasData(
            new FruitProfile { Id = 1, Name = "Fuji", Description = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 2, Name = "Gala", Description = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 3, Name = "Golden Delicious", Description = "Golden Delicious", VarietyCode = "GOLD", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 4, Name = "Granny Smith", Description = "Granny Smith", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 5, Name = "Honey Crisp", Description = "Honey Crisp", VarietyCode = "HONY", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 6, Name = "Organic Fuji", Description = "Organic Fuji", VarietyCode = "ORFU", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 7, Name = "Organic Gala", Description = "Organic Gala", VarietyCode = "ORGA", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 8, Name = "Organic Golden Delicious", Description = "Organic Golden Delicious", VarietyCode = "ORGD", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 9, Name = "Organic Granny Smith", Description = "Organic Granny Smith", VarietyCode = "ORGS", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 10, Name = "Organic Honey Crisp", Description = "Organic Honey Crisp", VarietyCode = "ORHC", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 11, Name = "Organic Pink Lady", Description = "Organic Pink Lady", VarietyCode = "ORPL", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 12, Name = "Organic Red Delicious", Description = "Organic Red Delicious", VarietyCode = "ORRD", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 13, Name = "Pink Lady", Description = "Pink Lady", VarietyCode = "PINK", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 14, Name = "Red Delicious", Description = "Red Delicious", VarietyCode = "RED", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 15, Name = "Mardi Gras", Description = "Mardi Gras", VarietyCode = "MDGS", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 16, Name = "Bosc", Description = "Bosc", VarietyCode = "BOSC", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 17, Name = "Bartlett", Description = "Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 18, Name = "D'Anjou", Description = "D'Anjou", VarietyCode = "DANJ", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = true },
            new FruitProfile { Id = 19, Name = "Organic Bartlett", Description = "Organic Bartlett", VarietyCode = "ORBA", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 20, Name = "Organic Bosc", Description = "Organic Bosc", VarietyCode = "ORBO", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 21, Name = "Organic D'anjou", Description = "Organic D'anjou", VarietyCode = "ORDA", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true, IsActive = true },
            new FruitProfile { Id = 22, Name = "Autumn Glory", Description = "Autumn Glory", VarietyCode = "ATGL", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true });
    }

    private static void SeedStarchScale(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StarchScale>().HasData(new StarchScale
        {
            Id = 1,
            Name = "6-point starch scale",
            FruitType = null,
            FruitProfileId = null,
            IsActive = true
        });

        modelBuilder.Entity<StarchScaleValue>().HasData(
            new StarchScaleValue { Id = 1, StarchScaleId = 1, Value = 1.0m, SortOrder = 10, IsActive = true },
            new StarchScaleValue { Id = 2, StarchScaleId = 1, Value = 1.2m, SortOrder = 20, IsActive = true },
            new StarchScaleValue { Id = 3, StarchScaleId = 1, Value = 1.5m, SortOrder = 30, IsActive = true },
            new StarchScaleValue { Id = 4, StarchScaleId = 1, Value = 1.8m, SortOrder = 40, IsActive = true },
            new StarchScaleValue { Id = 5, StarchScaleId = 1, Value = 2.0m, SortOrder = 50, IsActive = true },
            new StarchScaleValue { Id = 6, StarchScaleId = 1, Value = 2.5m, SortOrder = 60, IsActive = true },
            new StarchScaleValue { Id = 7, StarchScaleId = 1, Value = 3.0m, SortOrder = 70, IsActive = true },
            new StarchScaleValue { Id = 8, StarchScaleId = 1, Value = 3.5m, SortOrder = 80, IsActive = true },
            new StarchScaleValue { Id = 9, StarchScaleId = 1, Value = 4.0m, SortOrder = 90, IsActive = true },
            new StarchScaleValue { Id = 10, StarchScaleId = 1, Value = 4.5m, SortOrder = 100, IsActive = true },
            new StarchScaleValue { Id = 11, StarchScaleId = 1, Value = 5.0m, SortOrder = 110, IsActive = true },
            new StarchScaleValue { Id = 12, StarchScaleId = 1, Value = 6.0m, SortOrder = 120, IsActive = true });
    }

    private static void SeedSizeThresholds(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FruitSizeConversionThreshold>().HasData(
            new FruitSizeConversionThreshold { Id = 1, FruitType = "Apple", SizeCategory = 48, MinimumWeightGrams = 405.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 2, FruitType = "Apple", SizeCategory = 56, MinimumWeightGrams = 354.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 3, FruitType = "Apple", SizeCategory = 64, MinimumWeightGrams = 298.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 4, FruitType = "Apple", SizeCategory = 72, MinimumWeightGrams = 264.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 5, FruitType = "Apple", SizeCategory = 80, MinimumWeightGrams = 238.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 6, FruitType = "Apple", SizeCategory = 88, MinimumWeightGrams = 215.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 7, FruitType = "Apple", SizeCategory = 100, MinimumWeightGrams = 190.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 8, FruitType = "Apple", SizeCategory = 113, MinimumWeightGrams = 167.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 9, FruitType = "Apple", SizeCategory = 125, MinimumWeightGrams = 153.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 10, FruitType = "Apple", SizeCategory = 138, MinimumWeightGrams = 136.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 11, FruitType = "Apple", SizeCategory = 150, MinimumWeightGrams = 128.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 12, FruitType = "Apple", SizeCategory = 163, MinimumWeightGrams = 116.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 13, FruitType = "Apple", SizeCategory = 175, MinimumWeightGrams = 108.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 14, FruitType = "Apple", SizeCategory = 198, MinimumWeightGrams = 96.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 15, FruitType = "Apple", SizeCategory = 216, MinimumWeightGrams = 88.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 16, FruitType = "Pear", SizeCategory = 50, MinimumWeightGrams = 360.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 17, FruitType = "Pear", SizeCategory = 60, MinimumWeightGrams = 303.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 18, FruitType = "Pear", SizeCategory = 70, MinimumWeightGrams = 260.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 19, FruitType = "Pear", SizeCategory = 80, MinimumWeightGrams = 227.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 20, FruitType = "Pear", SizeCategory = 90, MinimumWeightGrams = 203.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 21, FruitType = "Pear", SizeCategory = 100, MinimumWeightGrams = 182.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 22, FruitType = "Pear", SizeCategory = 110, MinimumWeightGrams = 165.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 23, FruitType = "Pear", SizeCategory = 120, MinimumWeightGrams = 151.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 24, FruitType = "Pear", SizeCategory = 135, MinimumWeightGrams = 135.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 25, FruitType = "Pear", SizeCategory = 150, MinimumWeightGrams = 121.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 26, FruitType = "Pear", SizeCategory = 165, MinimumWeightGrams = 110.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 27, FruitType = "Pear", SizeCategory = 180, MinimumWeightGrams = 101.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 28, FruitType = "Pear", SizeCategory = 193, MinimumWeightGrams = 94.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 29, FruitType = "Pear", SizeCategory = 210, MinimumWeightGrams = 87.0000m, IsActive = true },
            new FruitSizeConversionThreshold { Id = 30, FruitType = "Pear", SizeCategory = 225, MinimumWeightGrams = 81.0000m, IsActive = true });
    }

    private static IReadOnlyList<RolePageAccess> BuildBuiltInRoleAccessSeed(DateTimeOffset updatedAt)
    {
        var rows = new List<RolePageAccess>(BuiltInAccessAreaKeys.Length * 5);
        var roles = new[]
        {
            (Id: 1, Name: BuiltInRoleNames.Admin),
            (Id: 2, Name: BuiltInRoleNames.Manager),
            (Id: 3, Name: BuiltInRoleNames.QcTech),
            (Id: 4, Name: BuiltInRoleNames.Viewer),
            (Id: 5, Name: BuiltInRoleNames.QcAdmin)
        };
        var id = 1;
        foreach (var role in roles)
        {
            foreach (var area in BuiltInAccessAreaKeys)
            {
                rows.Add(new RolePageAccess
                {
                    Id = id++,
                    RoleId = role.Id,
                    AreaKey = area,
                    AccessLevel = BuiltInAccessLevel(role.Name, area),
                    UpdatedAt = updatedAt
                });
            }
        }
        return rows;
    }

    private static string BuiltInAccessLevel(string role, string area)
    {
        if (role == BuiltInRoleNames.Admin) return "Admin";
        var viewer = area is "dashboard" or "daily-qc" or "field-samples" or "qc-reports" or "receipts"
            or "current-lots" or "rooms" or "inventory" or "grower-lots";
        if (role == BuiltInRoleNames.Viewer) return viewer ? "View" : "None";
        if (role == BuiltInRoleNames.QcTech)
            return area is "daily-qc" or "field-samples" or "receipts" ? "Create" : viewer ? "View" : "None";
        if (role == BuiltInRoleNames.QcAdmin)
        {
            if (area is "daily-qc" or "field-samples" or "qc-reports" or "qc-stations" or "varieties" or "grades"
                or "defects" or "size-configuration" or "variety-colors" or "orchard-recipients" or "orchard-managers") return "Admin";
            if (area == "master-data") return "View";
            return area == "receipts" ? "Create" : viewer ? "View" : "None";
        }
        if (role == BuiltInRoleNames.Manager)
        {
            if (area == "dashboard") return "View";
            if (area is "downloads" or "audit-history") return "View";
            if (area is "users" or "permission-matrix" or "configuration" or "backups" or "backup-history"
                or "email-configuration" or "data-cleanup" or "crop-year-review" or "historical-inventory-cleanup") return "None";
            return "Admin";
        }
        return "None";
    }

    private static readonly string[] BuiltInAccessAreaKeys =
    [
        "dashboard", "daily-qc", "field-samples", "qc-reports", "receipts", "current-lots", "bins-run",
        "projection-planner", "projection-outcome", "actual-runs", "packout-results", "historical-inventory-cleanup",
        "rooms", "room-transactions", "transfers", "true-up", "inventory", "grower-lots", "crop-year-review",
        "master-data", "users", "permission-matrix", "qc-stations", "downloads", "configuration", "variety-colors",
        "backups", "orchard-recipients", "orchard-managers", "facilities", "varieties", "grades", "defects",
        "size-configuration", "email-configuration", "backup-history", "audit-history", "import-tools", "export-tools",
        "data-cleanup"
    ];
}
