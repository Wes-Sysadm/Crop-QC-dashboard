using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data;

public sealed class CropQcDbContext(DbContextOptions<CropQcDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGoogleCredential> UserGoogleCredentials => Set<UserGoogleCredential>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PasswordPolicy> PasswordPolicies => Set<PasswordPolicy>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<FruitProfile> FruitProfiles => Set<FruitProfile>();
    public DbSet<SampleType> SampleTypes => Set<SampleType>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<DefectType> DefectTypes => Set<DefectType>();
    public DbSet<StarchScale> StarchScales => Set<StarchScale>();
    public DbSet<StarchScaleValue> StarchScaleValues => Set<StarchScaleValue>();
    public DbSet<FruitSizeConversionThreshold> FruitSizeConversionThresholds => Set<FruitSizeConversionThreshold>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<RoomDepletion> RoomDepletions => Set<RoomDepletion>();
    public DbSet<QcSample> QcSamples => Set<QcSample>();
    public DbSet<QcFruitReading> QcFruitReadings => Set<QcFruitReading>();
    public DbSet<QcFruitDefect> QcFruitDefects => Set<QcFruitDefect>();
    public DbSet<QcPhoto> QcPhotos => Set<QcPhoto>();
    public DbSet<QcSummaryEmailLog> QcSummaryEmailLogs => Set<QcSummaryEmailLog>();
    public DbSet<QcStation> QcStations => Set<QcStation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OfflineSyncItem> OfflineSyncItems => Set<OfflineSyncItem>();
    public DbSet<DashboardConfiguration> DashboardConfigurations => Set<DashboardConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuth(modelBuilder);
        ConfigureMasterData(modelBuilder);
        ConfigureQc(modelBuilder, IsPostgreSqlProvider());
        ConfigureAudit(modelBuilder);
        ConfigureDashboardConfiguration(modelBuilder);
        SeedData(modelBuilder);
    }

    private bool IsPostgreSqlProvider() =>
        Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static void ConfigureAuth(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.GoogleSubjectId).HasMaxLength(200);
            entity.Property(x => x.Domain).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.GoogleSubjectId);
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

        modelBuilder.Entity<Role>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.RoleId });
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.Property(x => x.PermissionKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.RoleId, x.PermissionKey }).IsUnique();
        });
    }

    private static void ConfigureMasterData(ModelBuilder modelBuilder)
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
            entity.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.WarehouseId, x.Name }).IsUnique();
            entity.HasOne(x => x.Warehouse)
                .WithMany(x => x.Rooms)
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(x => x.GrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LotCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DeleteReason).HasMaxLength(1000);
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

        modelBuilder.Entity<QcSample>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.StarchStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PhotoStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EmailStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.DeleteReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.ReceiptId, x.SampleSequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.ReceiptId, x.IsDeleted });
            entity.HasOne(x => x.SampleType)
                .WithMany(x => x.Samples)
                .HasForeignKey(x => x.SampleTypeId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(x => x.ToAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ReplyToAddress).HasMaxLength(320);
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.MessageId).HasMaxLength(500);
            entity.Property(x => x.ResendReason).HasMaxLength(1000);
            entity.Property(x => x.OverrideReason).HasMaxLength(1000);
            entity.Property(x => x.ReportSnapshotReference).HasMaxLength(1000);
            entity.HasIndex(x => x.ReceiptId);
            entity.HasIndex(x => x.QcSampleId);
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
            new Role { Id = 1, Name = "Admin", Description = "Full dashboard and configuration access.", IsSystemRole = true },
            new Role { Id = 2, Name = "Manager", Description = "Manage QC receiving workflows and resend summaries.", IsSystemRole = true },
            new Role { Id = 3, Name = "QC User", Description = "Capture receiving samples and QC readings.", IsSystemRole = true },
            new Role { Id = 4, Name = "Viewer", Description = "Read-only dashboard access.", IsSystemRole = true });

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
            new SampleType { Id = 4, Name = "Lot Sample", IsActive = true });

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
}
