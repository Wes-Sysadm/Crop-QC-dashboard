using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ReceiptDateBaselineTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Boundary = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
    private const int WarehouseId = 8101, RoomId = 8201, DestinationId = 8202, FruitId = 8301, GrowerId = 8401;
    private const long ReceiptId = 8501;
    public const string PostgresSetting = "CROPQC_TEST_RECEIPT_DATE_POSTGRES";

    public static IEnumerable<object[]> Cases()
    {
        yield return [-2, -1, "ordinary", false];
        yield return [-1, -2, "ordinary", false];
        yield return [1, 2, "ordinary", false];
        yield return [2, 1, "ordinary", false];
        yield return [-1, 1, "ordinary", true];
        yield return [1, -1, "ordinary", true];
        yield return [0, 1, "ordinary", true];
        yield return [1, 0, "ordinary", true];
        yield return [-1, 0, "ordinary", false];
        yield return [0, -1, "ordinary", false];
        yield return [-1, 1, "none", false];
        yield return [-1, 1, "unrelated-room", false];
        yield return [-1, 1, "transfer-destination", true];
        yield return [1, -1, "transfer-destination", true];
        yield return [-1, 1, "consolidated", true];
        yield return [-1, 1, "identity-corrected", true];
        yield return [-1, 1, "quantity-corrected", true];
        yield return [-1, 1, "previous-crop", true];
        yield return [-1, 1, "baseline-previous-crop", true];
        yield return [-1, 1, "multiple", false];
        yield return [1, 3, "multiple", true];
        yield return [-1, 1, "zero-net", true];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Date_edit_preserves_accounting(int oldOffset, int newOffset, string shape, bool blocked) =>
        VerifyAsync(oldOffset, newOffset, shape, blocked, postgres: false);

    [ReceiptDatePostgreSqlTheory]
    [MemberData(nameof(Cases))]
    public Task PostgreSql_date_edit_preserves_accounting(int oldOffset, int newOffset, string shape, bool blocked) =>
        VerifyAsync(oldOffset, newOffset, shape, blocked, postgres: true);

    private async Task VerifyAsync(int oldOffset, int newOffset, string shape, bool blocked, bool postgres)
    {
        await using var fixture = await Fixture.CreateAsync(postgres);
        var db = fixture.Db;
        await SeedAsync(db, Boundary.AddDays(oldOffset), shape);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var conservation = new InventoryConservationReportService(db, ledger, shape == "previous-crop" ? 2025 : 2026);
        var before = await conservation.AnalyzeAsync(default);
        Assert.True(before.IsReady, JsonSerializer.Serialize(before));
        var positionsBefore = await PositionsAsync(ledger);
        var effectiveBefore = await EffectiveIdsAsync(db);
        var protectedBefore = await FingerprintAsync(db);
        var receiptBefore = await ReceiptFingerprintAsync(db);
        var auditsBefore = await db.AuditLogs.CountAsync();
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == ReceiptId);
        var form = new UpdateReceiptForm
        {
            Id = ReceiptId,
            CropYear = receipt.CropYear,
            ConfirmCropYear = true,
            ReceivedAt = Boundary.AddDays(newOffset),
            CompuTechReceiptId = receipt.CompuTechReceiptId,
            ReceiptType = receipt.ReceiptType,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerLotId = receipt.GrowerLotId,
            GrowerNumber = receipt.GrowerNumber!,
            GrowerName = receipt.GrowerName,
            LotCode = receipt.LotCode,
            BinCount = receipt.BinCount
        };
        var error = Dashboard(db).UpdateReceiptAsync(form, default);
        var message = await error;
        db.ChangeTracker.Clear();
        var after = await conservation.AnalyzeAsync(default);
        output.WriteLine($"{db.Database.ProviderName}: {shape} {oldOffset}->{newOffset}; error={message ?? "(none)"}; current={before.Global.AuthoritativeCurrentBins}->{after.Global.AuthoritativeCurrentBins}; effectiveRows={string.Join(",", effectiveBefore)}->{string.Join(",", await EffectiveIdsAsync(db))}");
        if (blocked)
        {
            Assert.Contains("opening inventory accounting", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No changes were saved", message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(receiptBefore, await ReceiptFingerprintAsync(db));
            Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync());
        }
        else
        {
            Assert.Null(message);
            Assert.Equal(form.ReceivedAt, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == ReceiptId)).ReceivedAt);
        }
        Assert.Equal(protectedBefore, await FingerprintAsync(db));
        Assert.Equal(effectiveBefore, await EffectiveIdsAsync(db));
        Assert.Equal(positionsBefore, await PositionsAsync(ledger));
        Assert.True(after.IsReady, JsonSerializer.Serialize(after));
        Assert.Equal(before.Receiving.ReceiptTotal, after.Receiving.ReceiptTotal);
        Assert.Equal(before.Receiving.LedgerTotal, after.Receiving.LedgerTotal);
        Assert.Equal(0, after.Receiving.MismatchCount);
        Assert.Equal(0, after.Global.Difference);
        Assert.All(after.Facilities, x => Assert.Equal(0, x.Difference));
    }

    private static DashboardDataService Dashboard(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));
        return new DashboardDataService(db, null!, new FileStorageOptions(), new EmailOptions(), null!,
            new GoogleAuthenticationOptions(), null!, null!, new QcPhotoRequirementPolicy(), null!,
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            configuration, NullLogger<DashboardDataService>.Instance, new UserAccessService(db, configuration));
    }

    private static async Task SeedAsync(CropQcDbContext db, DateTimeOffset receivedAt, string shape)
    {
        var warehouse = new Warehouse { Id = WarehouseId, Code = "DATE-WP", Name = "Date guard test" };
        var room = new Room { Id = RoomId, Warehouse = warehouse, Code = "DATE-A", Name = "Date A" };
        var destination = new Room { Id = DestinationId, Warehouse = warehouse, Code = "DATE-B", Name = "Date B" };
        var fruit = new FruitProfile { Id = FruitId, Name = "Date Gala", VarietyCode = "DATE-GALA", FruitType = "Apple", ProductionType = "Conventional" };
        var grower = new GrowerLot { Id = GrowerId, Grower = "Date Grower", LotNumber = "DATE-100", IsActive = true, CreatedAt = Boundary, UpdatedAt = Boundary };
        var admin = new User { Id = 8601, Email = ApplicationAreas.OwnerEmail, DisplayName = "Date Admin", Domain = "fruitandland.com", CreatedAt = Boundary };
        var receipt = new Receipt
        {
            Id = ReceiptId,
            CropYear = shape == "previous-crop" ? 2025 : 2026,
            ReceivedAt = receivedAt,
            CreatedAt = receivedAt,
            UpdatedAt = receivedAt,
            CompuTechReceiptId = "DATE-TEST",
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerLot = grower,
            GrowerNumber = grower.LotNumber,
            LotCode = grower.LotNumber,
            GrowerName = grower.Grower,
            BinCount = 20
        };
        db.AddRange(warehouse, room, destination, fruit, grower, admin, receipt);
        await db.SaveChangesAsync();

        RoomInventoryAdjustment Row(long id, Room target, int amount, string type, bool linked = true) => new()
        {
            Id = id,
            Receipt = linked ? receipt : null,
            ReceiptId = linked ? ReceiptId : null,
            Warehouse = warehouse,
            Room = target,
            CropYear = receipt.CropYear,
            GrowerLot = grower,
            FruitProfile = fruit,
            GrowerName = grower.Grower,
            LotNumber = grower.LotNumber,
            VarietyCode = fruit.VarietyCode,
            AdjustmentType = type,
            ChangeAmount = amount,
            NewBinCount = Math.Max(0, amount),
            AdjustmentAt = Boundary.AddDays(4),
            CreatedAt = Boundary.AddDays(4),
            Reason = "Synthetic date guard fixture"
        };
        var source = Row(8701, room, 20, "ReceiptAdd");
        source.AdjustmentAt = receivedAt;
        db.Add(source);
        var moved = shape is "transfer-destination" or "consolidated";
        if (moved)
        {
            var transfer = new RoomTransfer
            {
                Id = 8801,
                OperationKey = "date-transfer",
                SourceWarehouse = warehouse,
                DestinationWarehouse = warehouse,
                SourceRoom = room,
                DestinationRoom = destination,
                CropYear = receipt.CropYear,
                GrowerLotId = GrowerId,
                FruitProfile = fruit,
                GrowerName = grower.Grower,
                LotNumber = grower.LotNumber,
                VarietyCode = fruit.VarietyCode,
                BinCount = 20,
                Reason = "Synthetic transfer",
                TransferredAt = Boundary.AddDays(4),
                CreatedAt = Boundary.AddDays(4)
            };
            var debit = Row(8702, room, -20, "TransferOut");
            var credit = Row(8703, destination, 20, "TransferIn", linked: shape != "consolidated");
            debit.RoomTransfer = credit.RoomTransfer = transfer;
            db.AddRange(transfer, debit, credit);
        }
        if (shape == "zero-net")
        {
            db.Add(Row(8704, room, -20, "Depletion"));
        }
        if (shape == "quantity-corrected")
        {
            receipt.BinCount = 25;
            var quantity = new ReceiptInventoryOverride
            {
                Id = Guid.NewGuid(),
                Receipt = receipt,
                AdministratorUser = admin,
                OperationKey = "date-quantity",
                ActionType = ReceiptInventoryOverrideActionTypes.QuantityCorrection,
                OldReceiptBinCount = 20,
                NewReceiptBinCount = 25,
                InventoryDelta = 5,
                Reason = "Synthetic quantity correction",
                CreatedAt = Boundary.AddDays(4),
                BeforeReceiptSnapshotJson = "{}",
                AfterReceiptSnapshotJson = "{}",
                AffectedInventorySnapshotJson = "[]"
            };
            var increase = Row(8705, room, 5, "ReceiptAdminOverride");
            increase.ReceiptInventoryOverride = quantity;
            db.AddRange(quantity, increase);
        }
        if (shape == "identity-corrected")
        {
            var original = new GrowerLot { Id = GrowerId + 1, Grower = "Original grower", LotNumber = "DATE-OLD", IsActive = true, CreatedAt = Boundary, UpdatedAt = Boundary };
            var correction = new InventoryIdentityCorrection
            {
                Id = Guid.NewGuid(),
                OperationKey = "date-identity",
                SourceCropYear = 2026,
                TargetCropYear = 2026,
                SourceGrowerLot = original,
                TargetGrowerLot = grower,
                SourceFruitProfile = fruit,
                TargetFruitProfile = fruit,
                CorrectedReceipt = receipt,
                CreatedByUser = admin,
                Reason = "Synthetic identity correction",
                SourceIdentitySnapshotJson = "{}",
                TargetIdentitySnapshotJson = "{}",
                CreatedAt = Boundary.AddDays(4),
                IsComplete = true,
                ExpectedAdjustmentCount = 2
            };
            source.GrowerLot = original; source.GrowerName = original.Grower; source.LotNumber = original.LotNumber;
            var debit = Row(8706, room, -20, "InventoryIdentityCorrection");
            debit.GrowerLot = original; debit.GrowerName = original.Grower; debit.LotNumber = original.LotNumber;
            var credit = Row(8707, room, 20, "InventoryIdentityCorrection");
            debit.InventoryIdentityCorrection = credit.InventoryIdentityCorrection = correction;
            db.AddRange(original, correction, debit, credit);
        }
        if (shape != "none")
        {
            var baselineRoom = shape is "unrelated-room" or "transfer-destination" ? destination : room;
            var baseline = Row(8790, baselineRoom, 0, RoomInventoryImportService.StartingInventoryAdjustmentType, linked: false);
            baseline.NewBinCount = 100;
            baseline.AdjustmentAt = baseline.CreatedAt = Boundary;
            if (shape == "baseline-previous-crop") baseline.CropYear = 2025;
            db.Add(baseline);
            if (shape == "multiple")
            {
                var newer = Row(8791, room, 0, RoomInventoryImportService.StartingInventoryAdjustmentType, linked: false);
                newer.NewBinCount = 100;
                newer.AdjustmentAt = newer.CreatedAt = Boundary.AddDays(2);
                db.Add(newer);
            }
        }
        await db.SaveChangesAsync();
        // Seed materialized current treatment, including receiptless consolidated descendants,
        // from the real operational query, not a mocked balance.
        var ledger = new RoomInventoryLedgerQueryService(db);
        var snapshots = await ledger.GetSnapshotsAsync(null, null, default);
        long segmentId = 8900;
        foreach (var snapshot in snapshots.Where(x => x.CurrentBins > 0))
        {
            db.Add(new TreatmentLineageSegment
            {
                Id = ++segmentId,
                WarehouseId = snapshot.WarehouseId,
                RoomId = snapshot.RoomId,
                ReceiptId = shape == "consolidated" ? null : ReceiptId,
                CropYear = snapshot.CropYear,
                GrowerLotId = snapshot.GrowerLotId,
                FruitProfileId = snapshot.FruitProfileId,
                IdentityKey = RoomTreatmentService.IdentityKey(snapshot),
                GrowerNameSnapshot = snapshot.Grower,
                LotNumberSnapshot = snapshot.Lot,
                VarietyCodeSnapshot = snapshot.Variety,
                ProductionTypeSnapshot = "Conventional",
                TreatmentState = TreatmentLineageStates.Confirmed,
                TreatmentSignature = "",
                CurrentBins = snapshot.CurrentBins,
                CreatedAt = Boundary,
                UpdatedAt = Boundary
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<string> PositionsAsync(IRoomInventoryLedgerQueryService ledger) =>
        JsonSerializer.Serialize((await ledger.GetSnapshotsAsync(null, null, default))
            .Select(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.CurrentBins })
            .OrderBy(x => x.RoomId).ThenBy(x => x.GrowerLotId).ThenBy(x => x.CropYear));

    private static async Task<long[]> EffectiveIdsAsync(CropQcDbContext db) =>
        InventoryConservationReportService.EffectiveAdjustments(
            await db.RoomInventoryAdjustments.AsNoTracking().Include(x => x.Receipt).ToListAsync())
            .Select(x => x.Id).Order().ToArray();

    private static async Task<string> ReceiptFingerprintAsync(CropQcDbContext db) =>
        await RowsAsync<Receipt>(db);

    private static async Task<string> FingerprintAsync(CropQcDbContext db) =>
        string.Join("|", await RowsAsync<RoomInventoryAdjustment>(db), await RowsAsync<RoomTransfer>(db),
            await RowsAsync<ReceiptInventoryOverride>(db), await RowsAsync<InventoryIdentityCorrection>(db),
            await RowsAsync<TreatmentLineageSegment>(db), await RowsAsync<TreatmentLineageMovement>(db),
            await RowsAsync<InterCrewTransfer>(db), await RowsAsync<OutsideWarehouseTransfer>(db),
            await RowsAsync<BinsRunEntry>(db), await RowsAsync<ActualRun>(db), await RowsAsync<RoomInventoryLoss>(db));

    private static async Task<string> RowsAsync<T>(CropQcDbContext db) where T : class
    {
        var rows = await db.Set<T>().AsNoTracking().ToListAsync();
        var scalars = rows.Select(x => JsonSerializer.Serialize(db.Entry(x).Properties.OrderBy(p => p.Metadata.Name)
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", scalars))));
    }

    private sealed class Fixture(CropQcDbContext db, string? adminConnection, string? database) : IAsyncDisposable
    {
        public CropQcDbContext Db => db;
        public static async Task<Fixture> CreateAsync(bool postgres)
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>();
            string? admin = null, name = null;
            if (postgres)
            {
                admin = Environment.GetEnvironmentVariable(PostgresSetting);
                Assert.False(string.IsNullOrWhiteSpace(admin), "PostgreSQL proof requires an explicit disposable connection.");
                ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(admin!);
                var builder = new NpgsqlConnectionStringBuilder(admin);
                Assert.Contains(builder.Host, new[] { "localhost", "127.0.0.1", "::1" });
                name = "cropqc_batch1a_test_" + Guid.NewGuid().ToString("N");
                await using var connection = new NpgsqlConnection(admin);
                await connection.OpenAsync();
                Assert.StartsWith("18.", connection.PostgreSqlVersion.ToString());
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
                await create.ExecuteNonQueryAsync();
                builder.Database = name;
                builder.Pooling = false;
                options.UseNpgsql(builder.ConnectionString);
            }
            else options.UseInMemoryDatabase("date-guard-" + Guid.NewGuid());
            var fixture = new Fixture(new CropQcDbContext(options.Options), admin, name);
            try { await fixture.Db.Database.EnsureCreatedAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            if (adminConnection is null || database is null) return;
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class ReceiptDatePostgreSqlTheoryAttribute : TheoryAttribute
{
    public ReceiptDatePostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ReceiptDateBaselineTests.PostgresSetting)))
            Skip = "Requires CROPQC_TEST_RECEIPT_DATE_POSTGRES (localhost disposable PostgreSQL 18).";
    }
}
