using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class BulkRoomTransferTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T16:00:00Z");

    [Fact]
    public async Task Eight_hundred_bins_move_once_with_receipt_and_treatment_links_and_cannot_move_from_old_room()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(800);
        var snapshot = Assert.Single(await f.Ledger.GetSnapshotsAsync(null, [1], default));
        f.Db.TreatmentLineageSegments.Add(f.Segment(snapshot, 800, receiptId: 1, signature: "u|a:1"));
        f.Db.RoomTreatmentApplications.Add(new RoomTreatmentApplication
        {
            Id = 1,
            OperationKey = "application",
            RoomId = 1,
            WarehouseId = 3,
            TreatmentChemicalId = 1,
            ProductNameSnapshot = "Test treatment",
            CommonNameSnapshot = "Test",
            CropSnapshot = "Pear",
            UnitSnapshot = "oz",
            CurrencySnapshot = "USD",
            AppliedByUserId = 1,
            AppliedAt = Now.AddHours(-1),
            CreatedAt = Now,
            CreatedByUserId = 1
        });
        await f.Db.SaveChangesAsync();
        var source = await f.Db.TreatmentLineageSegments.SingleAsync();
        source.Applications.Add(new TreatmentLineageSegmentApplication { RoomTreatmentApplicationId = 1, Sequence = 1 });
        await f.Db.SaveChangesAsync();
        var form = await f.FormAsync();
        Assert.Equal(800, form.BinCount);
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(form, default));
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(form, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await f.Ledger.GetSnapshotsAsync(null, [1], default)).Sum(x => x.CurrentBins));
        Assert.Equal(800, (await f.Ledger.GetSnapshotsAsync(null, [2], default)).Sum(x => x.CurrentBins));
        Assert.Single(await f.Db.RoomTransfers.ToListAsync());
        var movement = Assert.Single(await f.Db.TreatmentLineageMovements.ToListAsync());
        Assert.Equal(1, movement.ReceiptId);
        Assert.Equal("u|a:1", movement.TreatmentSignatureSnapshot);
        var destination = await f.Db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.RoomId == 2);
        Assert.Equal(800, destination.CurrentBins);
        Assert.Equal(1, Assert.Single(destination.Applications).RoomTreatmentApplicationId);
        Assert.All(await f.Db.RoomInventoryAdjustments.Where(x => x.RoomTransferId != null).ToListAsync(), x =>
        {
            Assert.Equal(1, x.ReceiptId); Assert.Equal(1, x.GrowerLotId); Assert.Equal(17, x.FruitProfileId);
        });
        form.OperationKey = Guid.NewGuid().ToString("N");
        Assert.Contains("changed", await f.Dashboard.CreateRoomTransferAsync(form, default));
        Assert.Single(await f.Db.RoomTransfers.ToListAsync());
        Assert.Equal(800, (await f.Db.Receipts.SingleAsync()).BinCount);
        Assert.Equal(1, (await f.Db.Receipts.SingleAsync()).RoomId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_or_duplicate_lineage_excludes_only_ambiguous_position_and_preserves_it(bool duplicate)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(798);
        await f.SeedAsync(25, id: 2);
        var snapshot = (await f.Ledger.GetSnapshotsAsync(null, [1], default)).Single(x => x.GrowerLotId == 1);
        f.Db.TreatmentLineageSegments.Add(f.Segment(snapshot, duplicate ? 798 : 802));
        if (duplicate) f.Db.TreatmentLineageSegments.Add(f.Segment(snapshot, 4, receiptId: 1));
        await f.Db.SaveChangesAsync();
        var before = await f.Db.TreatmentLineageSegments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var page = await f.Dashboard.GetRoomDetailAsync(1, default);
        Assert.Equal(823, page.TransferCurrentRoomBins);
        Assert.Equal(25, page.TransferAvailableBins);
        Assert.Equal(798, page.TransferNeedsReconciliationBins);
        Assert.Contains("802 explicit bins exceed 798", Assert.Single(page.TransferLotOptions, x => !x.IsAvailable).UnavailableReason);
        Assert.Contains("segment #", Assert.Single(page.TransferLotOptions, x => !x.IsAvailable).UnavailableReason);
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(await f.FormAsync(), default));
        f.Db.ChangeTracker.Clear();
        foreach (var original in before)
        {
            var after = await f.Db.TreatmentLineageSegments.SingleAsync(x => x.Id == original.Id);
            Assert.Equal(original.CurrentBins, after.CurrentBins);
            Assert.Equal(original.ConcurrencyVersion, after.ConcurrencyVersion);
        }
        Assert.Equal(798, (await f.Ledger.GetSnapshotsAsync(null, [1], default)).Sum(x => x.CurrentBins));
        Assert.Equal(25, (await f.Ledger.GetSnapshotsAsync(null, [2], default)).Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task Blank_and_redundant_production_status_retain_all_798_bins_after_reviewed_repair()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(798);
        var snapshot = Assert.Single(await f.Ledger.GetSnapshotsAsync(null, [1], default));
        f.Db.TreatmentLineageSegments.AddRange(
            f.Segment(snapshot with { InventoryStatus = "Conventional" }, 256, receiptId: 1),
            f.Segment(snapshot with { InventoryStatus = "" }, 542));
        await f.Db.SaveChangesAsync();
        var selections = await f.Treatments.GetSelectionsAsync(snapshot, default);
        Assert.Equal(798, selections.Sum(x => x.CurrentBins));
        Assert.All(selections, x => Assert.True(x.IsAvailable));
        var form = await f.FormAsync();
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(form, default));
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(form, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, await f.Db.TreatmentLineageSegments.Where(x => x.RoomId == 1).SumAsync(x => x.CurrentBins));
        Assert.Equal(798, await f.Db.TreatmentLineageSegments.Where(x => x.RoomId == 2).SumAsync(x => x.CurrentBins));
        Assert.Equal(2, await f.Db.RoomTransfers.CountAsync());
        Assert.Equal(0, await f.Db.RoomInventoryAdjustments.SumAsync(x => x.RoomTransferId != null ? x.ChangeAmount : 0));
    }

    [Fact]
    public async Task Legitimate_four_bin_authoritative_remainder_can_move_before_legacy_status_segment()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(802);
        var snapshot = Assert.Single(await f.Ledger.GetSnapshotsAsync(null, [1], default));
        var alias = snapshot with { InventoryStatus = string.IsNullOrWhiteSpace(snapshot.InventoryStatus) ? "Conventional" : "" };
        f.Db.TreatmentLineageSegments.Add(f.Segment(alias, 798));
        await f.Db.SaveChangesAsync();
        var page = await f.Dashboard.GetRoomDetailAsync(1, default);
        Assert.Equal(802, page.TransferAvailableBins);
        var remainder = Assert.Single(page.TransferLotOptions, x => x.TreatmentSegmentId is null);
        Assert.Equal(4, remainder.CurrentBins);
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            FromRoomId = 1,
            DestinationWarehouseId = 3,
            DestinationRoomId = 2,
            SourceLotKey = remainder.LotKey,
            TreatmentSignature = "u",
            BinCount = 4,
            TransferAt = Now,
            Reason = "Move legitimate authoritative remainder"
        }, default));
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(await f.FormAsync(), default));
        Assert.Equal(802, (await f.Ledger.GetSnapshotsAsync(null, [2], default)).Sum(x => x.CurrentBins));
        Assert.Equal(0, await f.Db.TreatmentLineageSegments.Where(x => x.RoomId == 1).SumAsync(x => x.CurrentBins));
    }

    [Fact]
    public async Task Split_dispatch_of_four_then_sixty_two_does_not_recreate_four_bins()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(864);
        var snapshot = Assert.Single(await f.Ledger.GetSnapshotsAsync(null, [1], default));
        f.Db.TreatmentLineageSegments.AddRange(f.Segment(snapshot, 4, 1), f.Segment(snapshot, 860));
        f.Db.InterCrewTransfers.Add(new InterCrewTransfer
        {
            Id = 22,
            OperationKey = "dispatch22",
            SourceWarehouseId = 3,
            SourceRoomId = 1,
            DestinationCustodyGroup = "WP/DH",
            CropYear = 2026,
            GrowerLotId = 1,
            FruitProfileId = 17,
            GrowerNameSnapshot = "Test Grower",
            LotNumberSnapshot = snapshot.Lot,
            VarietyCodeSnapshot = snapshot.Variety,
            ProductionTypeSnapshot = snapshot.ProductionType,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            TreatmentSummarySnapshot = "Untreated",
            BinsLoaded = 66,
            LoadedAt = Now,
            CreatedAt = Now,
            LoadedByUserId = 1,
            Status = "InTransit"
        });
        await f.Db.SaveChangesAsync();
        var result = await f.Treatments.DispatchAsync(snapshot, "u", 66, "split", 22, Now, 1, default);
        Assert.True(result.Success, result.Error);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(798, await f.Db.TreatmentLineageSegments.SumAsync(x => x.CurrentBins));
        Assert.Equal(new[] { 4, 62 }, await f.Db.TreatmentLineageMovements.OrderBy(x => x.Id).Select(x => x.BinCount).ToArrayAsync());
    }

    [Fact]
    public async Task Depleted_inventory_is_not_resurrected_and_stale_review_writes_nothing()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(802);
        var form = await f.FormAsync();
        var original = await f.Db.RoomInventoryAdjustments.SingleAsync();
        f.Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            WarehouseId = 3,
            RoomId = 1,
            CropYear = 2026,
            GrowerLotId = 1,
            FruitProfileId = 17,
            ReceiptId = 1,
            GrowerName = original.GrowerName,
            LotNumber = original.LotNumber,
            VarietyCode = original.VarietyCode,
            ChangeAmount = -4,
            NewBinCount = 798,
            AdjustmentType = "HistoricalRemoval",
            AdjustmentAt = Now,
            CreatedAt = Now
        });
        await f.Db.SaveChangesAsync();
        Assert.Contains("changed", await f.Dashboard.CreateRoomTransferAsync(form, default));
        Assert.Empty(await f.Db.RoomTransfers.ToListAsync());
        Assert.Null(await f.Dashboard.CreateRoomTransferAsync(await f.FormAsync(), default));
        Assert.Equal(798, (await f.Ledger.GetSnapshotsAsync(null, [2], default)).Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task PostgreSql_failure_in_second_position_rolls_back_the_entire_bulk_transfer()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROOM_TRANSFER_TEST_POSTGRES"))) return;
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(500);
        await f.SeedAsync(300, id: 2);
        var form = await f.FormAsync();
        // A deterministic failure after the first position has saved its transfer,
        // lineage and paired ledger rows proves the enclosing transaction rolls back.
        await f.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_second_transfer() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."GrowerLotId" = 2 THEN RAISE EXCEPTION 'injected failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_second BEFORE INSERT ON "RoomTransfers" FOR EACH ROW EXECUTE FUNCTION reject_second_transfer();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Dashboard.CreateRoomTransferAsync(form, default));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.RoomTransfers.ToListAsync());
        Assert.Empty(await f.Db.TreatmentLineageMovements.ToListAsync());
        Assert.Empty(await f.Db.TreatmentLineageSegments.ToListAsync());
        Assert.Equal(2, await f.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(800, (await f.Ledger.GetSnapshotsAsync(null, [1], default)).Sum(x => x.CurrentBins));
    }

    [Fact]
    public async Task PostgreSql_reviewed_repair_checks_evidence_preserves_history_and_is_idempotent()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROOM_TRANSFER_TEST_POSTGRES"))) return;
        await using var f = await Fixture.CreateAsync();
        await f.Db.Database.OpenConnectionAsync();
        // Session-local evidence fixture: use the actual model's column types, with
        // only the columns consulted by the guarded repair populated.
        foreach (var table in new[] { "Users", "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomInventoryAdjustments", "AuditLogs", "TreatmentLineageSegmentApplications" })
        {
            var createTable = $"CREATE TEMP TABLE \"{table}\" AS TABLE public.\"{table}\" WITH NO DATA";
            await f.Db.Database.ExecuteSqlRawAsync(createTable);
        }
        await f.Db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id","IsActive") VALUES (1,true);
            INSERT INTO "TreatmentLineageSegments" ("Id","WarehouseId","RoomId","CropYear","GrowerLotId","FruitProfileId","TreatmentState","TreatmentSignature","CurrentBins","ConcurrencyVersion","ReceiptId","IdentityKey","UpdatedAt") VALUES
            (148,3,66,2026,448,17,'Untreated','u',184,2,720,'2026|448|17|1372|1372|BART|CONVENTIONAL|False|CONVENTIONAL',now()),
            (154,3,66,2026,448,17,'Untreated','u',72,2,626,'2026|448|17|1372|1372|BART|CONVENTIONAL|False|CONVENTIONAL',now()),
            (160,3,66,2026,448,17,'Untreated','u',802,8,null,'2026|448|17|1372|1372|BART|CONVENTIONAL|False|','2026-09-16T13:46:23.520179Z');
            INSERT INTO "TreatmentLineageMovements" ("Id","DestinationSegmentId","RoomTransferId","BinCount") VALUES (96,160,218,280),(97,160,219,194),(182,160,281,64);
            INSERT INTO "TreatmentLineageMovements" ("Id","SourceSegmentId","InterCrewTransferId","BinCount","MovementType") VALUES (590,159,22,4,'InterCrewDispatch'),(591,160,22,62,'InterCrewDispatch');
            INSERT INTO "RoomInventoryAdjustments" ("Id","ReceiptId","RoomId","ChangeAmount","LotNumber","FruitProfileId","InterCrewTransferId") VALUES
            (1298,763,66,66,'1372',17,null),(3180,null,66,-66,'1372',17,22),(1,null,66,798,'1372',17,null);
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "scripts", "postgresql", "repair-mcd14-lineage-20260923.sql"));
        script = script.Replace("\\set ON_ERROR_STOP on", "").Replace(":'actor_user_id'", "'1'");
        await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlRawAsync(script));
        await f.Db.Database.ExecuteSqlRawAsync("ROLLBACK");
        Assert.Equal(802, await Scalar("SELECT \"CurrentBins\" AS \"Value\" FROM \"TreatmentLineageSegments\" WHERE \"Id\"=160"));
        Assert.Equal(0, await f.Db.AuditLogs.CountAsync());
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE \"TreatmentLineageSegments\" SET \"ConcurrencyVersion\"=7 WHERE \"Id\"=160");
        await f.Db.Database.ExecuteSqlRawAsync(script);
        await f.Db.Database.ExecuteSqlRawAsync(script);
        Assert.Equal(542, await Scalar("SELECT \"CurrentBins\" AS \"Value\" FROM \"TreatmentLineageSegments\" WHERE \"Id\"=160"));
        Assert.Equal(798, await Scalar("SELECT SUM(\"CurrentBins\")::integer AS \"Value\" FROM \"TreatmentLineageSegments\""));
        Assert.Equal(1, await f.Db.AuditLogs.CountAsync());
        Assert.Equal(5, await f.Db.TreatmentLineageMovements.CountAsync());
        Assert.Equal(3, await f.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Contains("802", (await f.Db.AuditLogs.Select(x => x.BeforeValuesJson).SingleAsync())!);
        Assert.Contains("542", (await f.Db.AuditLogs.Select(x => x.AfterValuesJson).SingleAsync())!);
        Task<int> Scalar(string sql) => f.Db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required CropQcDbContext Db { get; init; }
        public required RoomInventoryLedgerQueryService Ledger { get; init; }
        public required RoomTreatmentService Treatments { get; init; }
        public required DashboardDataService Dashboard { get; init; }
        private string? AdminConnection { get; init; }
        private string? DatabaseName { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>();
            var admin = Environment.GetEnvironmentVariable("ROOM_TRANSFER_TEST_POSTGRES");
            string? databaseName = null;
            if (string.IsNullOrWhiteSpace(admin)) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            else
            {
                ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(admin);
                databaseName = "cropqc_room_transfer_test_" + Guid.NewGuid().ToString("N");
                await using var connection = new NpgsqlConnection(admin);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
                await command.ExecuteNonQueryAsync();
                options.UseNpgsql(new NpgsqlConnectionStringBuilder(admin) { Database = databaseName }.ConnectionString);
            }
            var db = new CropQcDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            // Seed identities above repository seed ranges.

            db.Rooms.AddRange(new Room { Id = 1, WarehouseId = 3, Code = "MCD-14", Name = "Room 14" },
                new Room { Id = 2, WarehouseId = 3, Code = "MCD-15", Name = "Room 15" });
            db.Users.Add(new User { Id = 1, Email = ApplicationAreas.OwnerEmail, DisplayName = "Test", Domain = "fruitandland.com", CreatedAt = Now });
            db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 1 });
            await db.SaveChangesAsync();
            var configuration = new ConfigurationBuilder().Build();
            var access = new UserAccessService(db, configuration);
            var accessor = new FixedAccessor
            {
                HttpContext = new DefaultHttpContext
                { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test")) }
            };
            var time = new PacificBusinessTimeService(new FixedClock());
            var ledger = new RoomInventoryLedgerQueryService(db);
            var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
            var dashboard = new DashboardDataService(db, null!, new FileStorageOptions(), new EmailOptions(), null!,
                new GoogleAuthenticationOptions(), null!, null!, new QcPhotoRequirementPolicy(), null!,
                new CropYearService(db, configuration), accessor, configuration, NullLogger<DashboardDataService>.Instance,
                access, roomInventoryLedgerQueryService: ledger, roomTreatmentService: treatments, businessTime: time);
            return new Fixture { Db = db, Ledger = ledger, Treatments = treatments, Dashboard = dashboard, AdminConnection = admin, DatabaseName = databaseName };
        }

        public async Task SeedAsync(int bins, int id = 1)
        {
            Db.GrowerLots.Add(new GrowerLot { Id = id, Grower = "Test Grower", LotNumber = $"137{id}", CreatedAt = Now, UpdatedAt = Now });
            if (!await Db.FruitProfiles.AnyAsync(x => x.Id == 17))
                Db.FruitProfiles.Add(new FruitProfile { Id = 17, Name = "Test Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false });
            Db.Receipts.Add(new Receipt
            {
                Id = id,
                CompuTechReceiptId = $"TEST-{id}",
                WarehouseId = 3,
                RoomId = 1,
                CropYear = 2026,
                GrowerLotId = id,
                FruitProfileId = 17,
                GrowerName = "Test Grower",
                GrowerNumber = $"137{id}",
                LotCode = $"137{id}",
                BinCount = bins,
                ReceivedAt = Now.AddDays(-1),
                CreatedAt = Now,
                UpdatedAt = Now
            });
            Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                WarehouseId = 3,
                RoomId = 1,
                CropYear = 2026,
                GrowerLotId = id,
                FruitProfileId = 17,
                ReceiptId = id,
                GrowerName = "Test Grower",
                LotNumber = $"137{id}",
                VarietyCode = "BART",
                ChangeAmount = bins,
                NewBinCount = bins,
                AdjustmentType = "ReceiptAdd",
                AdjustmentAt = Now.AddDays(-1),
                CreatedAt = Now
            });
            await Db.SaveChangesAsync();
        }

        public TreatmentLineageSegment Segment(RoomInventoryLedgerSnapshot s, int bins, long? receiptId = null, string signature = "u") => new()
        {
            WarehouseId = s.WarehouseId,
            RoomId = s.RoomId,
            CropYear = s.CropYear,
            GrowerLotId = s.GrowerLotId,
            FruitProfileId = s.FruitProfileId,
            IdentityKey = RoomTreatmentService.IdentityKey(s),
            ReceiptId = receiptId,
            GrowerNumberSnapshot = s.GrowerNumber,
            GrowerNameSnapshot = s.Grower,
            LotNumberSnapshot = s.Lot,
            VarietyCodeSnapshot = s.Variety,
            ProductionTypeSnapshot = s.ProductionType,
            IsOrganicSnapshot = s.IsOrganic,
            InventoryStatusSnapshot = s.InventoryStatus,
            TreatmentSignature = signature,
            TreatmentState = signature == "u" ? "Untreated" : "Confirmed",
            CurrentBins = bins,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        public async Task<RoomTransferForm> FormAsync()
        {
            var page = await Dashboard.GetRoomDetailAsync(1, default);
            return new RoomTransferForm
            {
                TransferAllEligible = true,
                ExpectedInventoryToken = page.TransferForm.ExpectedInventoryToken,
                FromRoomId = 1,
                DestinationWarehouseId = 3,
                DestinationRoomId = 2,
                BinCount = page.TransferAvailableBins,
                TransferAt = Now,
                Reason = "Bulk regression"
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (DatabaseName is null) return;
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(AdminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE {DatabaseName} WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class FixedAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
}
