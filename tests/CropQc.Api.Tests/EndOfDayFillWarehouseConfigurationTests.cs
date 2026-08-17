using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class EndOfDayFillWarehouseConfigurationTests
{
    [Fact]
    public void LabelResolver_UsesExplicitMcdAbbreviationWithoutRenamingWarehouseMaster()
    {
        var resolver = new EndOfDayFillWarehouseLabelResolver();
        Assert.Equal("WP", resolver.Resolve(4, "WP", "WP"));
        Assert.Equal("MCD", resolver.Resolve(3, "McDougall", "McDougall"));
        Assert.Equal("DH", resolver.Resolve(2, "DH", "DH"));
        Assert.Equal("EBS", resolver.Resolve(1, "EBS", "EBS"));
        Assert.Equal("MCDOUGALL", resolver.Resolve(30, "McDougall", "McDougall"));
    }

    [Fact]
    public async Task Sync_DryRunApplyAndRerun_SplitsReviewedGroupsWithoutChangingProtectedHistory()
    {
        await using var fixture = await SyncFixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(false), default);

        Assert.True(dryRun.Success);
        Assert.Equal("Ready", dryRun.Preflight.State);
        Assert.False(dryRun.Applied);
        Assert.Equal(2, await fixture.Db.EndOfDayFillReportGroups.CountAsync());
        Assert.Collection(dryRun.Preflight.ExistingGroups,
            x => Assert.Equal((1, "WP End of Day Fill", 4, 1), (x.Id, x.Name, x.WarehouseId, x.HistoricalSendCount)),
            x => Assert.Equal((2, "EBS End of Day Fill", 1, 0), (x.Id, x.Name, x.WarehouseId, x.HistoricalSendCount)));
        Assert.Equal([4, 3, 2, 1], dryRun.Preflight.DesiredGroups.Select(x => x.WarehouseId));
        Assert.Equal([1, 1, 1, 1], dryRun.Preflight.DesiredGroups.Select(x => x.RoomIds.Count));

        var historyBefore = await fixture.Db.EndOfDayFillReportSends.AsNoTracking().SingleAsync();
        var recipientsBefore = await fixture.Db.EndOfDayFillReportRecipients.AsNoTracking().OrderBy(x => x.Id).Select(x => x.EmailAddress).ToListAsync();
        var applied = await fixture.Service.RunAsync(Request(
            true,
            dryRun.Preflight.TargetFingerprint,
            dryRun.Preflight.ProtectedFingerprint), default);

        Assert.True(applied.Success);
        Assert.True(applied.Applied);
        Assert.Equal("AlreadyApplied", applied.FinalState!.State);
        var groups = await fixture.Db.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.UserAssignments).ThenInclude(x => x.User)
            .OrderBy(x => x.WarehouseId)
            .ToListAsync();
        Assert.Equal(4, groups.Count);
        Assert.Equal(4, groups.Count(x => x.IsActive));
        Assert.Collection(groups,
            x => AssertGroup(x, 1, "EBS End of Day Fill", ["wes@fruitandland.com", "rob@earlbrownandsons.com"]),
            x => AssertGroup(x, 2, "DH End of Day Fill", ["wes@fruitandland.com", "jorge@wp-packing.com"]),
            x => AssertGroup(x, 3, "MCD End of Day Fill", ["wes@fruitandland.com", "jorge@wp-packing.com"]),
            x => AssertGroup(x, 4, "WP End of Day Fill", ["wes@fruitandland.com", "jorge@wp-packing.com"]));
        Assert.Equal("McDougall", (await fixture.Db.Warehouses.AsNoTracking().SingleAsync(x => x.Id == 3)).Code);
        Assert.Equal(recipientsBefore, await fixture.Db.EndOfDayFillReportRecipients.AsNoTracking().OrderBy(x => x.Id).Select(x => x.EmailAddress).ToListAsync());
        var historyAfter = await fixture.Db.EndOfDayFillReportSends.AsNoTracking().SingleAsync();
        Assert.Equal(historyBefore.SnapshotJson, historyAfter.SnapshotJson);
        Assert.Equal(historyBefore.HtmlBody, historyAfter.HtmlBody);
        Assert.Equal(historyBefore.TextBody, historyAfter.TextBody);
        Assert.Equal(historyBefore.GmailMessageId, historyAfter.GmailMessageId);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == "end-of-day-fill-warehouse-configuration").ToListAsync());

        var rerun = await fixture.Service.RunAsync(Request(true, applied.FinalState.TargetFingerprint, applied.FinalState.ProtectedFingerprint), default);
        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == "end-of-day-fill-warehouse-configuration").ToListAsync());
    }

    [Fact]
    public async Task Sync_FailsClosedWhenExactWarehouseMasterIdentityChanges()
    {
        await using var fixture = await SyncFixture.CreateAsync();
        (await fixture.Db.Warehouses.SingleAsync(x => x.Id == 3)).Code = "MCD";
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.RunAsync(Request(false), default);

        Assert.False(result.Success);
        Assert.Equal("Conflict", result.Preflight.State);
        Assert.Contains(result.Preflight.Conflicts, x => x.Contains("McDougall", StringComparison.Ordinal));
        Assert.Equal(2, await fixture.Db.EndOfDayFillReportGroups.CountAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    private static EndOfDayFillWarehouseConfigurationSyncRequest Request(
        bool apply,
        string? target = null,
        string? protectedFingerprint = null) => new(
            apply,
            false,
            apply,
            "wes@fruitandland.com",
            apply ? "Disposable restored-production rehearsal" : "",
            target,
            protectedFingerprint);

    private static void AssertGroup(EndOfDayFillReportGroup group, int warehouseId, string name, string[] users)
    {
        Assert.Equal(warehouseId, group.WarehouseId);
        Assert.Equal(name, group.Name);
        Assert.Single(group.Rooms);
        Assert.All(group.Rooms, room => Assert.Equal(warehouseId, room.WarehouseId));
        Assert.Equal(users.Order(StringComparer.OrdinalIgnoreCase), group.UserAssignments.Select(x => x.User.Email).Order(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public EndOfDayFillWarehouseConfigurationSyncService Service { get; }

        private SyncFixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new(db, new PacificBusinessTimeService(new FixedClock()));
        }

        public static async Task<SyncFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var wes = new User { Id = 101, Email = "wes@fruitandland.com", DisplayName = "Wes Cusick", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var jorge = new User { Id = 102, Email = "jorge@wp-packing.com", DisplayName = "Jorge Ledezma", Domain = "wp-packing.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var rob = new User { Id = 103, Email = "rob@earlbrownandsons.com", DisplayName = "Robert Fulgham", Domain = "earlbrownandsons.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Users.AddRange(wes, jorge, rob);
            db.Rooms.AddRange(
                new Room { Id = 201, WarehouseId = 4, Code = "WP-4", Name = "WP 4", CapacityBins = 1000, IsActive = true, EndOfDayFillReportGroupId = 1 },
                new Room { Id = 202, WarehouseId = 3, Code = "MCD-3", Name = "MCD 3", CapacityBins = 650, IsActive = true, EndOfDayFillReportGroupId = 1 },
                new Room { Id = 203, WarehouseId = 2, Code = "DH-1", Name = "DH 1", CapacityBins = 410, IsActive = true, EndOfDayFillReportGroupId = 1 },
                new Room { Id = 204, WarehouseId = 1, Code = "EVANS-7", Name = "Evans 7", CapacityBins = 1859, IsActive = true, EndOfDayFillReportGroupId = 2 });
            db.EndOfDayFillUserGroupAssignments.AddRange(
                new EndOfDayFillUserGroupAssignment { UserId = wes.Id, ReportGroupId = 1, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = wes.Id },
                new EndOfDayFillUserGroupAssignment { UserId = jorge.Id, ReportGroupId = 1, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = wes.Id },
                new EndOfDayFillUserGroupAssignment { UserId = wes.Id, ReportGroupId = 2, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = wes.Id },
                new EndOfDayFillUserGroupAssignment { UserId = rob.Id, ReportGroupId = 2, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = wes.Id });
            db.EndOfDayFillReportSends.Add(new EndOfDayFillReportSend
            {
                ReportGroupId = 1,
                ReportGroupName = "WP End of Day Fill",
                Facility = "WP",
                PacificReportDate = new DateOnly(2026, 8, 9),
                RevisionNumber = 0,
                SenderUserId = wes.Id,
                SenderEmail = wes.Email,
                SenderDisplayName = wes.DisplayName,
                RecipientsJson = "[\"wes@fruitandland.com\"]",
                PhysicalCountConfirmed = true,
                SnapshotHash = new string('a', 64),
                SnapshotJson = "{\"historicalCombinedScope\":true}",
                SuccessRevisionKey = "1:20260809:0",
                SuccessSnapshotKey = $"1:20260809:0:{new string('a', 64)}",
                Subject = "Historical combined report",
                HtmlBody = "<p>historical</p>",
                TextBody = "historical",
                Status = EndOfDayFillSendStatuses.Succeeded,
                GmailMessageId = "gmail-history-id",
                CreatedAt = DateTimeOffset.UtcNow,
                AttemptedAt = DateTimeOffset.UtcNow,
                SentAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new SyncFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    }
}
