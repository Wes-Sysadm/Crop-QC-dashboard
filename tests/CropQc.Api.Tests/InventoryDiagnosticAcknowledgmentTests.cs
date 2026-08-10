using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class InventoryDiagnosticAcknowledgmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T16:00:00Z");

    [Fact]
    public async Task DismissAndRestore_PersistExactFingerprintWithoutChangingLedgerOrReadiness()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await AdjustmentSnapshotAsync(fixture.Db, fixture.AdjustmentIds[0]);
        var readinessBefore = await fixture.Invariant.VerifyReadinessAsync(CancellationToken.None);
        var overview = await fixture.Service.GetOverviewAsync(new(), CancellationToken.None);
        var diagnostic = Assert.Single(overview.ActiveDiagnostics);

        var dismissed = await fixture.Service.DismissAsync(
            diagnostic.DiagnosticKey,
            "Reviewed as a pre-invariant historical depletion.",
            fixture.AdminEmail,
            CancellationToken.None);

        Assert.True(dismissed.Succeeded, dismissed.Error);
        Assert.Equal(before, await AdjustmentSnapshotAsync(fixture.Db, fixture.AdjustmentIds[0]));
        var acknowledgment = await fixture.Db.InventoryDiagnosticAcknowledgments.SingleAsync();
        Assert.True(acknowledgment.IsActive);
        Assert.Equal(diagnostic.DiagnosticKey, acknowledgment.DiagnosticKey);
        Assert.Equal("NoParent", acknowledgment.DiagnosticCode);
        Assert.Equal(InventoryDiagnosticAcknowledgmentService.DiagnosticType, acknowledgment.DiagnosticType);
        Assert.Contains("fingerprintVersion", acknowledgment.DiagnosticSnapshotJson);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x =>
            x.Action == "dismiss"
            && x.EntityKey == diagnostic.DiagnosticKey
            && x.AfterValuesJson!.Contains("DiagnosticSnapshotJson"));

        fixture.Db.ChangeTracker.Clear();
        var restartedService = fixture.CreateService();
        var afterRestart = await restartedService.GetOverviewAsync(new(), CancellationToken.None);
        Assert.Empty(afterRestart.ActiveDiagnostics);
        Assert.Equal(diagnostic.DiagnosticKey, Assert.Single(afterRestart.DismissedDiagnostics).DiagnosticKey);
        var readinessAfter = await fixture.Invariant.VerifyReadinessAsync(CancellationToken.None);
        Assert.Equal(readinessBefore.Issues, readinessAfter.Issues);

        var restored = await restartedService.RestoreAsync(diagnostic.DiagnosticKey, fixture.AdminEmail, CancellationToken.None);
        Assert.True(restored.Succeeded, restored.Error);
        var afterRestore = await restartedService.GetOverviewAsync(new(), CancellationToken.None);
        Assert.Equal(diagnostic.DiagnosticKey, Assert.Single(afterRestore.ActiveDiagnostics).DiagnosticKey);
        Assert.Empty(afterRestore.DismissedDiagnostics);
        Assert.False((await fixture.Db.InventoryDiagnosticAcknowledgments.SingleAsync()).IsActive);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "restore" && x.EntityKey == diagnostic.DiagnosticKey);
        Assert.Equal(before, await AdjustmentSnapshotAsync(fixture.Db, fixture.AdjustmentIds[0]));
    }

    [Fact]
    public async Task ChangedDiagnosticFingerprint_IsActiveAndDoesNotReusePriorDismissal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = Assert.Single((await fixture.Service.GetOverviewAsync(new(), CancellationToken.None)).ActiveDiagnostics);
        Assert.True((await fixture.Service.DismissAsync(
            original.DiagnosticKey,
            "Reviewed against the original historical evidence.",
            fixture.AdminEmail,
            CancellationToken.None)).Succeeded);

        var adjustment = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == original.AdjustmentId);
        adjustment.Source = "Materially changed diagnostic evidence";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var changed = await fixture.Service.GetOverviewAsync(new(), CancellationToken.None);
        var active = Assert.Single(changed.ActiveDiagnostics);
        var dismissed = Assert.Single(changed.DismissedDiagnostics);
        Assert.NotEqual(original.DiagnosticKey, active.DiagnosticKey);
        Assert.Equal(original.DiagnosticKey, dismissed.DiagnosticKey);
        Assert.False(dismissed.StillMatchesCurrentDiagnostic);
    }

    [Fact]
    public async Task BlockingNewFormatDiagnostic_CannotBeDismissed()
    {
        await using var fixture = await Fixture.CreateAsync(invariantVersion: InventoryDeductionInvariantService.CurrentVersion);
        var diagnostic = Assert.Single((await fixture.Service.GetOverviewAsync(new(), CancellationToken.None)).ActiveDiagnostics);
        Assert.True(diagnostic.BlocksDeployment);
        Assert.False(diagnostic.CanDismiss);

        var result = await fixture.Service.DismissAsync(
            diagnostic.DiagnosticKey,
            "This must not make a blocking issue safe.",
            fixture.AdminEmail,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot be dismissed", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.InventoryDiagnosticAcknowledgments.ToListAsync());
        Assert.False((await fixture.Invariant.VerifyReadinessAsync(CancellationToken.None)).IsReady);
    }

    [Fact]
    public async Task SimilarHistoricalRows_KeepSeparateFingerprintsAndDismissals()
    {
        await using var fixture = await Fixture.CreateAsync(adjustmentCount: 2);
        var overview = await fixture.Service.GetOverviewAsync(new(), CancellationToken.None);
        Assert.Equal(2, overview.ActiveDiagnostics.Count);
        Assert.Equal(2, overview.ActiveDiagnostics.Select(x => x.DiagnosticKey).Distinct().Count());

        var first = overview.ActiveDiagnostics.OrderBy(x => x.AdjustmentId).First();
        Assert.True((await fixture.Service.DismissAsync(
            first.DiagnosticKey,
            "Reviewed only this immutable adjustment identity.",
            fixture.AdminEmail,
            CancellationToken.None)).Succeeded);

        var after = await fixture.Service.GetOverviewAsync(new(), CancellationToken.None);
        Assert.Single(after.ActiveDiagnostics);
        Assert.Single(after.DismissedDiagnostics);
        Assert.NotEqual(first.AdjustmentId, after.ActiveDiagnostics[0].AdjustmentId);
    }

    [Fact]
    public async Task Dismissal_RequiresAReasonAndValidCurrentFingerprint()
    {
        await using var fixture = await Fixture.CreateAsync();
        var diagnostic = Assert.Single((await fixture.Service.GetOverviewAsync(new(), CancellationToken.None)).ActiveDiagnostics);

        var shortReason = await fixture.Service.DismissAsync(
            diagnostic.DiagnosticKey,
            "too short",
            fixture.AdminEmail,
            CancellationToken.None);
        var malformed = await fixture.Service.DismissAsync(
            "not-a-fingerprint",
            "A sufficiently long operational reason.",
            fixture.AdminEmail,
            CancellationToken.None);

        Assert.False(shortReason.Succeeded);
        Assert.False(malformed.Succeeded);
        Assert.Empty(await fixture.Db.InventoryDiagnosticAcknowledgments.ToListAsync());
    }

    private static async Task<string> AdjustmentSnapshotAsync(CropQcDbContext db, long id)
    {
        var x = await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(value => value.Id == id);
        return string.Join("|", x.Id, x.OldBinCount, x.ChangeAmount, x.NewBinCount, x.Source, x.InventoryInvariantVersion);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public InventoryDeductionInvariantService Invariant { get; }
        public InventoryDiagnosticAcknowledgmentService Service { get; }
        public string AdminEmail { get; }
        public IReadOnlyList<long> AdjustmentIds { get; }

        private Fixture(
            SqliteConnection connection,
            CropQcDbContext db,
            InventoryDeductionInvariantService invariant,
            InventoryDiagnosticAcknowledgmentService service,
            string adminEmail,
            IReadOnlyList<long> adjustmentIds)
        {
            this.connection = connection;
            Db = db;
            Invariant = invariant;
            Service = service;
            AdminEmail = adminEmail;
            AdjustmentIds = adjustmentIds;
        }

        public static async Task<Fixture> CreateAsync(int invariantVersion = 0, int adjustmentCount = 1)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options;
            var db = new CropQcDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = await db.Warehouses.OrderBy(x => x.Id).FirstAsync();
            var room = new Room
            {
                Id = 51002,
                WarehouseId = warehouse.Id,
                Code = "WP-TEST",
                Name = "Inventory diagnostic test room",
                IsActive = true
            };
            var fruit = await db.FruitProfiles.OrderBy(x => x.Id).FirstAsync();
            var admin = new User
            {
                Email = $"inventory-admin-{Guid.NewGuid():N}@fruitandland.com",
                DisplayName = "Inventory Diagnostic Admin",
                Domain = "fruitandland.com",
                IsActive = true,
                CreatedAt = Now
            };
            db.AddRange(room, admin);
            var adjustments = Enumerable.Range(0, adjustmentCount).Select(index => new RoomInventoryAdjustment
            {
                CropYear = 2026,
                WarehouseId = warehouse.Id,
                RoomId = room.Id,
                FruitProfileId = fruit.Id,
                GrowerName = "Historical Grower",
                LotNumber = "1560",
                VarietyCode = "PINK",
                InventoryStatus = "Conventional",
                OldBinCount = 78,
                ChangeAmount = -78,
                NewBinCount = 0,
                AdjustmentType = "Depletion",
                Source = "Bins sent to line",
                AdjustmentAt = Now.AddMinutes(index),
                CreatedAt = Now.AddMinutes(index),
                CreatedByUser = admin,
                InventoryInvariantVersion = invariantVersion
            }).ToList();
            db.RoomInventoryAdjustments.AddRange(adjustments);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var invariant = new InventoryDeductionInvariantService(
                db,
                NullLogger<InventoryDeductionInvariantService>.Instance);
            var service = new InventoryDiagnosticAcknowledgmentService(
                db,
                invariant,
                new PacificBusinessTimeService(new FixedClock(Now)));
            return new Fixture(connection, db, invariant, service, admin.Email, adjustments.Select(x => x.Id).ToList());
        }

        public InventoryDiagnosticAcknowledgmentService CreateService() => new(
            Db,
            Invariant,
            new PacificBusinessTimeService(new FixedClock(Now.AddHours(1))));

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
