using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class BusinessTimeAndReceiptPurgeTests
{
    [Theory]
    [InlineData("2026-01-15T12:00:00Z", "2026-01-15T04:00:00-08:00", "PST")]
    [InlineData("2026-07-15T12:00:00Z", "2026-07-15T05:00:00-07:00", "PDT")]
    public void Pacific_conversion_uses_the_correct_dst_offset(string utc, string expectedPacific, string expectedZone)
    {
        var service = Service(DateTimeOffset.Parse(utc));

        var actual = service.ToPacific(DateTimeOffset.Parse(utc));

        Assert.Equal(DateTimeOffset.Parse(expectedPacific), actual);
        Assert.Equal(expectedZone, service.TimeZoneAbbreviation(actual));
    }

    [Fact]
    public void Next_nightly_backup_is_one_am_Pacific_across_spring_dst()
    {
        var service = Service(DateTimeOffset.Parse("2026-03-07T10:00:00Z"));

        var next = service.NextNightlyBackupUtc();

        Assert.Equal(DateTimeOffset.Parse("2026-03-08T09:00:00Z"), next);
        Assert.Equal(1, service.ToPacific(next).Hour);
    }

    [Fact]
    public void Both_fall_back_candidates_have_one_Pacific_date_for_the_uniqueness_guard()
    {
        var service = Service(DateTimeOffset.Parse("2026-11-01T08:30:00Z"));
        var firstOneAm = DateTimeOffset.Parse("2026-11-01T08:30:00Z");
        var repeatedOneAm = DateTimeOffset.Parse("2026-11-01T09:30:00Z");

        Assert.True(service.IsNightlyCandidate(firstOneAm));
        Assert.True(service.IsNightlyCandidate(repeatedOneAm));
        Assert.Equal(service.PacificDate(firstOneAm), service.PacificDate(repeatedOneAm));
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]
    [InlineData(2026, 11, 1, 25)]
    public void Pacific_business_day_utc_range_handles_dst_length(int year, int month, int day, int expectedHours)
    {
        var service = Service(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var range = service.UtcRangeForPacificDate(new DateOnly(year, month, day));

        Assert.Equal(expectedHours, (range.End - range.Start).TotalHours);
        Assert.Equal(new DateOnly(year, month, day), service.PacificDate(range.Start));
    }

    [Fact]
    public async Task Purge_is_dry_run_by_default_and_selects_only_persisted_2026()
    {
        await using var db = CreateDbContext();
        await SeedReceiptsAsync(db);
        var service = PurgeService(db);

        var result = await service.PurgeAsync(new ReceiptPurgeRequest(
            2026, Apply: false, ConfirmProduction: false, VerifiedBackupRunId: null,
            "admin@example.com", "test dry run"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Applied);
        Assert.Single(result.Preflight.Receipts);
        Assert.Equal(2026, result.Preflight.TargetCropYear);
        Assert.Equal(1, result.Preflight.Receipts[0].Dependencies.Receipts);
        Assert.Equal(2, await db.Receipts.CountAsync());
        Assert.Equal(1, result.Preflight.PreservationBaseline.ReceiptCount);
    }

    [Fact]
    public async Task Production_apply_refuses_without_confirmation_and_verified_backup()
    {
        await using var db = CreateDbContext();
        await SeedReceiptsAsync(db);
        var service = PurgeService(db);

        var noConfirmation = await service.PurgeAsync(new ReceiptPurgeRequest(
            2026, Apply: true, ConfirmProduction: false, VerifiedBackupRunId: null,
            "admin@example.com", "authorized cleanup"), CancellationToken.None);
        var noBackup = await service.PurgeAsync(new ReceiptPurgeRequest(
            2026, Apply: true, ConfirmProduction: true, VerifiedBackupRunId: null,
            "admin@example.com", "authorized cleanup"), CancellationToken.None);

        Assert.False(noConfirmation.Success);
        Assert.Contains("confirm-production", noConfirmation.Message);
        Assert.False(noBackup.Success);
        Assert.Contains("verified backup", noBackup.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await db.Receipts.CountAsync());
    }

    [Fact]
    public async Task Transactional_purge_removes_reviewed_2026_dependents_and_preserves_2025_and_field_samples()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedReceiptsAsync(db);
        var receiptSampleType = new SampleType { Id = 900001, Name = "Purge Test Receipt Sample" };
        var fieldSampleType = new SampleType { Id = 900002, Name = "Purge Test Field Sample" };
        var defectType = new DefectType { Id = 900001, Name = "Purge Test Bruise" };
        db.AddRange(receiptSampleType, fieldSampleType, defectType);
        var sample2025 = Sample(100, 1, receiptSampleType, db.Receipts.Local.Single(x => x.CropYear == 2025));
        var sample2026 = Sample(200, 2, receiptSampleType, db.Receipts.Local.Single(x => x.CropYear == 2026));
        var fieldSample = new QcSample
        {
            Id = 300,
            SampleType = fieldSampleType,
            SampleTypeId = fieldSampleType.Id,
            Status = "In Progress",
            StarchStatus = "Pending",
            PhotoStatus = "Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.Parse("2026-07-20T16:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-20T16:00:00Z")
        };
        db.AddRange(sample2025, sample2026, fieldSample);
        var reading2025 = Reading(1000, sample2025);
        var reading2026 = Reading(2000, sample2026);
        db.AddRange(reading2025, reading2026);
        db.QcFruitDefects.Add(new QcFruitDefect { Id = 1, QcFruitReading = reading2026, QcFruitReadingId = reading2026.Id, DefectType = defectType, DefectTypeId = defectType.Id });
        db.QcPhotos.Add(new QcPhoto
        {
            Id = 1,
            QcSample = sample2026,
            QcSampleId = sample2026.Id,
            PhotoType = "Other",
            PhotoSource = "Test",
            FileName = "2026.jpg",
            ContentType = "image/jpeg",
            SharePointDriveId = "test-drive",
            SharePointItemId = "test-item",
            CapturedAt = DateTimeOffset.Parse("2026-07-20T16:00:00Z")
        });
        db.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            Id = 1,
            QcSample = sample2026,
            QcSampleId = sample2026.Id,
            FromAddress = "qc@example.com",
            ToAddress = "manager@example.com",
            Subject = "Test",
            Status = "Sent",
            CreatedAt = DateTimeOffset.Parse("2026-07-20T16:00:00Z")
        });
        db.OfflineSyncItems.Add(new OfflineSyncItem
        {
            Id = 1,
            EntityName = nameof(QcSample),
            LocalEntityId = "sample-200",
            ServerEntityId = sample2026.Id,
            SyncStatus = "Synced",
            CreatedAt = DateTimeOffset.Parse("2026-07-20T16:00:00Z")
        });
        db.BackupRunRecords.Add(new BackupRunRecord
        {
            Id = 99,
            BackupType = BackupRunTypes.PreDeployment,
            Status = BackupRunStatuses.Succeeded,
            EnvironmentName = "Test",
            DatabaseProvider = "Sqlite",
            RetentionCategory = BackupRunTypes.PreDeployment,
            StartedAt = DateTimeOffset.Parse("2026-07-23T05:00:00Z"),
            VerifiedAt = DateTimeOffset.Parse("2026-07-23T05:05:00Z"),
            RetentionProcessedAt = DateTimeOffset.Parse("2026-07-23T05:06:00Z"),
            LeaseReleasedAt = DateTimeOffset.Parse("2026-07-23T05:07:00Z")
        });
        await db.SaveChangesAsync();
        var service = PurgeService(db);

        var result = await service.PurgeAsync(new ReceiptPurgeRequest(
            2026, Apply: true, ConfirmProduction: true, VerifiedBackupRunId: 99,
            "admin@example.com", "Authorized 2026 cleanup"), CancellationToken.None);
        var secondDryRun = await service.PreflightAsync(2026, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Applied);
        Assert.Equal(1, result.DeletedCounts?.Receipts);
        Assert.Equal(1, result.DeletedCounts?.QcSamples);
        Assert.Equal(1, result.DeletedCounts?.FruitReadings);
        Assert.Equal(1, result.DeletedCounts?.Defects);
        Assert.Equal(1, result.DeletedCounts?.Photos);
        Assert.Equal(1, result.DeletedCounts?.EmailLogs);
        Assert.Equal(1, result.DeletedCounts?.OfflineSyncItems);
        Assert.Empty(secondDryRun.Receipts);
        Assert.Single(await db.Receipts.Where(x => x.CropYear == 2025).ToListAsync());
        Assert.NotNull(await db.QcSamples.FindAsync(100L));
        Assert.NotNull(await db.QcSamples.FindAsync(300L));
        Assert.NotNull(await db.QcFruitReadings.FindAsync(1000L));
        Assert.Single(await db.ReceiptDeletionAudits.ToListAsync());
        Assert.Equal(ReceiptPurgeStatuses.Succeeded, (await db.ReceiptPurgeOperations.SingleAsync()).Status);
        Assert.Single(await db.BackupRunRecords.ToListAsync());
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    public async Task Purge_rejects_every_crop_year_except_the_explicitly_authorized_year(int cropYear)
    {
        await using var db = CreateDbContext();
        var service = PurgeService(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreflightAsync(cropYear, CancellationToken.None));

        Assert.Contains("2026", exception.Message);
    }

    [Fact]
    public async Task Backup_notification_queue_is_deduplicated_per_run_and_result()
    {
        await using var db = CreateDbContext();
        db.BackupRunRecords.Add(new BackupRunRecord
        {
            Id = 42,
            BackupType = BackupRunTypes.Manual,
            Status = BackupRunStatuses.Succeeded,
            EnvironmentName = "Test",
            DatabaseProvider = "Test",
            RetentionCategory = BackupRunTypes.Manual,
            StartedAt = DateTimeOffset.Parse("2026-07-23T06:00:00Z"),
            VerifiedAt = DateTimeOffset.Parse("2026-07-23T06:05:00Z"),
            RetentionProcessedAt = DateTimeOffset.Parse("2026-07-23T06:06:00Z"),
            LeaseReleasedAt = DateTimeOffset.Parse("2026-07-23T06:07:00Z")
        });
        await db.SaveChangesAsync();
        var service = new BackupNotificationService(
            db,
            new BackupOptions { NotificationRecipient = "wes@fruitandland.com" },
            new NoOpEmailSender(),
            Service(DateTimeOffset.Parse("2026-07-23T06:00:00Z")),
            NullLogger<BackupNotificationService>.Instance);

        await service.QueueAsync(42, BackupNotificationTypes.Success, CancellationToken.None);
        await service.QueueAsync(42, BackupNotificationTypes.Success, CancellationToken.None);

        var notification = Assert.Single(await db.BackupNotificationRecords.ToListAsync());
        Assert.Equal("wes@fruitandland.com", notification.Recipient);
        Assert.Equal(BackupNotificationStatuses.Pending, notification.Status);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupNotificationQueued").ToListAsync());
    }

    [Fact]
    public async Task Unverified_backup_cannot_queue_a_success_notification()
    {
        await using var db = CreateDbContext();
        db.BackupRunRecords.Add(BackupRun(51, BackupRunStatuses.Succeeded, verified: false));
        await db.SaveChangesAsync();
        var service = NotificationService(db, new NoOpEmailSender());

        await service.QueueAsync(51, BackupNotificationTypes.Success, CancellationToken.None);

        Assert.Empty(await db.BackupNotificationRecords.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupNotificationSuppressed").ToListAsync());
    }

    [Fact]
    public async Task Verified_success_and_failed_backup_each_send_one_audited_email()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = SqliteDb(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(NotificationSender());
        db.BackupRunRecords.AddRange(
            BackupRun(61, BackupRunStatuses.Succeeded, verified: true),
            BackupRun(62, BackupRunStatuses.Failed, verified: false));
        await db.SaveChangesAsync();
        var sender = new RecordingEmailSender();
        var service = NotificationService(db, sender);

        await service.QueueAsync(61, BackupNotificationTypes.Success, CancellationToken.None);
        await service.QueueAsync(61, BackupNotificationTypes.Success, CancellationToken.None);
        await service.QueueAsync(62, BackupNotificationTypes.Failure, CancellationToken.None);
        var sent = await service.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal(2, sender.Messages.Count);
        Assert.Contains(sender.Messages, x => x.Subject.StartsWith("SUCCESS:", StringComparison.Ordinal));
        Assert.Contains(sender.Messages, x => x.Subject.StartsWith("FAILURE:", StringComparison.Ordinal));
        Assert.All(await db.BackupNotificationRecords.ToListAsync(), x => Assert.Equal(BackupNotificationStatuses.Sent, x.Status));
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "BackupNotificationSent"));
    }

    [Fact]
    public async Task Notification_failure_is_recorded_without_invalidating_verified_backup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = SqliteDb(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(NotificationSender());
        db.BackupRunRecords.Add(BackupRun(71, BackupRunStatuses.Succeeded, verified: true));
        await db.SaveChangesAsync();
        var service = NotificationService(db, new FailingEmailSender());

        await service.QueueAsync(71, BackupNotificationTypes.Success, CancellationToken.None);
        var sent = await service.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Equal(BackupRunStatuses.Succeeded, (await db.BackupRunRecords.FindAsync(71L))!.Status);
        var notification = Assert.Single(await db.BackupNotificationRecords.ToListAsync());
        Assert.Equal(BackupNotificationStatuses.Failed, notification.Status);
        Assert.True(notification.NextAttemptAt > notification.LastAttemptedAt);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupNotificationFailed").ToListAsync());
    }

    [Fact]
    public async Task Normal_receipt_delete_soft_deletes_only_eligible_receipt_and_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = SqliteDb(connection);
        await db.Database.EnsureCreatedAsync();
        await SeedReceiptsAsync(db);
        var service = PurgeService(db);
        var token = Guid.NewGuid().ToString("D");
        var form = new DeleteReceiptForm
        {
            Id = 2,
            ConfirmationValue = "PURGE-2026",
            ConfirmDeletion = true,
            Reason = "Duplicate entry",
            OperationToken = token
        };

        var first = await service.DeleteEligibleReceiptAsync(form, "admin@example.com", CancellationToken.None);
        var second = await service.DeleteEligibleReceiptAsync(form, "admin@example.com", CancellationToken.None);

        Assert.Null(first);
        Assert.Contains("already deleted", second, StringComparison.OrdinalIgnoreCase);
        Assert.True((await db.Receipts.FindAsync(2L))!.IsDeleted);
        Assert.False((await db.Receipts.FindAsync(1L))!.IsDeleted);
        Assert.Single(await db.ReceiptDeletionAudits.Where(x => x.DeletedReceiptId == 2).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.EntityName == nameof(Receipt) && x.EntityKey == "2").ToListAsync());
    }

    [Fact]
    public async Task Normal_receipt_delete_refuses_operational_history()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = SqliteDb(connection);
        await db.Database.EnsureCreatedAsync();
        await SeedReceiptsAsync(db);
        var sampleType = new SampleType { Id = 900010, Name = "Delete Blocker Test" };
        db.Add(sampleType);
        db.Add(Sample(900010, 2, sampleType, db.Receipts.Local.Single(x => x.Id == 2)));
        await db.SaveChangesAsync();
        var service = PurgeService(db);

        var error = await service.DeleteEligibleReceiptAsync(new DeleteReceiptForm
        {
            Id = 2,
            ConfirmationValue = "PURGE-2026",
            ConfirmDeletion = true,
            Reason = "Should be blocked",
            OperationToken = Guid.NewGuid().ToString("D")
        }, "admin@example.com", CancellationToken.None);

        Assert.Contains("operational history", error, StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Receipts.FindAsync(2L))!.IsDeleted);
        Assert.Empty(await db.ReceiptDeletionAudits.ToListAsync());
    }

    [Fact]
    public void Receipt_delete_endpoints_require_the_admin_policy()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ReceiptsController.cs"));

        Assert.Equal(2, source.Split("AccessPolicyNames.ReceiptDeleteAdmin", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Receipt_delete_page_requires_reason_exact_number_and_second_confirmation()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Delete.cshtml"));

        Assert.Contains("ConfirmationValue", source);
        Assert.Contains("Reason", source);
        Assert.Contains("ConfirmDeletion", source);
        Assert.Contains("Dependent Data", source);
    }

    private static PacificBusinessTimeService Service(DateTimeOffset now) =>
        new(new FixedClock(now));

    private static CropQcDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CropQcDbContext SqliteDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);

    private static ReceiptPurgeService PurgeService(CropQcDbContext db) =>
        new(
            db,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Production, DisplayName = "Production" },
            Service(DateTimeOffset.Parse("2026-07-23T06:00:00Z")),
            NullLogger<ReceiptPurgeService>.Instance);

    private static BackupNotificationService NotificationService(CropQcDbContext db, IQcEmailSender sender) =>
        new(
            db,
            new BackupOptions
            {
                NotificationRecipient = "wes@fruitandland.com",
                NotificationSender = "wes@fruitandland.com"
            },
            sender,
            Service(DateTimeOffset.Parse("2026-07-23T06:10:00Z")),
            NullLogger<BackupNotificationService>.Instance);

    private static BackupRunRecord BackupRun(long id, string status, bool verified) =>
        new()
        {
            Id = id,
            BackupType = status == BackupRunStatuses.Failed ? BackupRunTypes.Daily : BackupRunTypes.Manual,
            Status = status,
            EnvironmentName = "Test",
            DatabaseProvider = "Sqlite",
            RetentionCategory = BackupRunTypes.Manual,
            StartedAt = DateTimeOffset.Parse("2026-07-23T06:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-07-23T06:07:00Z"),
            VerifiedAt = verified ? DateTimeOffset.Parse("2026-07-23T06:05:00Z") : null,
            RetentionProcessedAt = verified ? DateTimeOffset.Parse("2026-07-23T06:06:00Z") : null,
            LeaseReleasedAt = DateTimeOffset.Parse("2026-07-23T06:07:00Z"),
            FailureStage = status == BackupRunStatuses.Failed ? "Package upload" : null,
            ErrorSummary = status == BackupRunStatuses.Failed ? "Safe test failure." : null
        };

    private static User NotificationSender() =>
        new()
        {
            Id = 900020,
            Email = "wes@fruitandland.com",
            DisplayName = "Wes",
            IsActive = true,
            CreatedAt = DateTimeOffset.Parse("2026-07-23T05:00:00Z")
        };

    private static async Task SeedReceiptsAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 900001, Code = "PURGE-TEST", Name = "Purge Test Warehouse" };
        var room = new Room { Id = 900001, Warehouse = warehouse, WarehouseId = warehouse.Id, Code = "PURGE-ROOM", Name = "Purge Test Room" };
        var fruit = new FruitProfile
        {
            Id = 900001,
            Name = "Purge Test Bartlett",
            VarietyCode = "PURGE-BART",
            FruitType = "Pear",
            ProductionType = "Conventional"
        };
        db.AddRange(warehouse, room, fruit);
        db.Receipts.AddRange(
            Receipt(1, 2025, "KEEP-2025", warehouse, room, fruit),
            Receipt(2, 2026, "PURGE-2026", warehouse, room, fruit));
        await db.SaveChangesAsync();
    }

    private static Receipt Receipt(long id, int cropYear, string number, Warehouse warehouse, Room room, FruitProfile fruit) =>
        new()
        {
            Id = id,
            CropYear = cropYear,
            ReceivedAt = DateTimeOffset.Parse($"{cropYear}-07-01T16:00:00Z"),
            CompuTechReceiptId = number,
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Room = room,
            RoomId = room.Id,
            FruitProfile = fruit,
            FruitProfileId = fruit.Id,
            GrowerName = "WP ORCHARD",
            GrowerNumber = "1080",
            LotCode = "WP-4",
            BinCount = 1,
            CreatedAt = DateTimeOffset.Parse($"{cropYear}-07-01T16:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse($"{cropYear}-07-01T16:00:00Z")
        };

    private static QcSample Sample(long id, long receiptId, SampleType type, Receipt receipt) =>
        new()
        {
            Id = id,
            ReceiptId = receiptId,
            Receipt = receipt,
            SampleType = type,
            SampleTypeId = type.Id,
            Status = "In Progress",
            StarchStatus = "Pending",
            PhotoStatus = "Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = receipt.ReceivedAt,
            CreatedAt = receipt.ReceivedAt
        };

    private static QcFruitReading Reading(long id, QcSample sample) =>
        new()
        {
            Id = id,
            QcSample = sample,
            QcSampleId = sample.Id,
            RowNumber = 1,
            WeightGrams = 180,
            SizeStatus = "Calculated",
            CreatedAt = sample.CreatedAt
        };

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(parts));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class NoOpEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(QcEmailSendResult.Sent("test-message"));
    }

    private sealed class RecordingEmailSender : IQcEmailSender
    {
        public List<QcEmailMessage> Messages { get; } = [];

        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(QcEmailSendResult.Sent($"message-{Messages.Count}"));
        }
    }

    private sealed class FailingEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(QcEmailSendResult.Failed("Test Gmail failure."));
    }
}
