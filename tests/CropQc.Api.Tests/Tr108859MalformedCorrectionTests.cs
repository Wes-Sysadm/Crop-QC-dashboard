using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class Tr108859MalformedCorrectionTests
{
    [Fact]
    public async Task Exact_run64_shape_normalizes_adjustment_280_in_place_and_rerun_is_zero_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var correction = fixture.CorrectionService();
        var preflight = await correction.PreflightAsync(CancellationToken.None);
        var originalAuditBefore = await fixture.Db.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == 23057);

        Assert.Equal("Ready", preflight.State);
        Assert.Equal(Tr108859DroppedBinsCorrectionConstants.MalformedTrueUpCase, preflight.Evidence!.CorrectionCase);
        Assert.Equal(466, preflight.Evidence.CurrentLedgerBalance);
        Assert.Equal(7, preflight.Evidence.CanonicalReceiptCount);
        Assert.Equal(248, preflight.Evidence.CanonicalReceiptAddBins);
        Assert.Equal(248, preflight.Evidence.CanonicalBalanceBeforeTarget);
        Assert.Equal(0, preflight.Evidence.LaterIdentityAdjustmentCount);
        Assert.Equal(1, preflight.Evidence.OriginalAuditCount);

        var first = await correction.RunAsync(fixture.Request(preflight), CancellationToken.None);
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var auditCount = await fixture.Db.AuditLogs.CountAsync();
        var second = await correction.RunAsync(fixture.Request(first.Preflight), CancellationToken.None);

        Assert.True(first.Success, first.Message);
        Assert.True(first.Applied);
        Assert.Equal("AlreadyApplied", first.Preflight.State);
        Assert.Equal(246, first.Preflight.Evidence!.CurrentLedgerBalance);
        Assert.True(second.Success, second.Message);
        Assert.True(second.AlreadyApplied);
        Assert.False(second.Applied);
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());

        var target = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 280);
        Assert.Equal(RoomInventoryLossAdjustmentTypes.DroppedBins, target.AdjustmentType);
        Assert.Equal(248, target.OldBinCount);
        Assert.Equal(-2, target.ChangeAmount);
        Assert.Equal(246, target.NewBinCount);
        Assert.Equal(DateTimeOffset.Parse("2026-08-11T22:23:00Z"), target.AdjustmentAt);
        Assert.Equal(DateTimeOffset.Parse("2026-08-11T22:25:24.118888Z"), target.CreatedAt);
        Assert.Equal(2, target.CreatedByUserId);
        Assert.Equal(InventoryDeductionInvariantService.CurrentVersion, target.InventoryInvariantVersion);
        Assert.NotNull(target.RoomInventoryLossId);

        var loss = Assert.Single(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(208, loss.ReceiptId);
        Assert.Equal(2, loss.BinCount);
        Assert.Null(loss.OccurredAt);
        Assert.False(loss.IsReversed);
        Assert.Equal(Tr108859DroppedBinsCorrectionConstants.HistoricalNotes, loss.Notes);
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(208L))!.BinCount);
        Assert.Equal(8, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        var originalAuditAfter = await fixture.Db.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == 23057);
        Assert.Equal(originalAuditBefore.Action, originalAuditAfter.Action);
        Assert.Equal(originalAuditBefore.EntityName, originalAuditAfter.EntityName);
        Assert.Equal(originalAuditBefore.EntityKey, originalAuditAfter.EntityKey);
        Assert.Equal(originalAuditBefore.UserId, originalAuditAfter.UserId);
        Assert.Equal(originalAuditBefore.BeforeValuesJson, originalAuditAfter.BeforeValuesJson);
        Assert.Equal(originalAuditBefore.AfterValuesJson, originalAuditAfter.AfterValuesJson);
        Assert.Equal(originalAuditBefore.SourceApplication, originalAuditAfter.SourceApplication);
        Assert.Equal(originalAuditBefore.CreatedAt, originalAuditAfter.CreatedAt);
        var correctionAudit = Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "NormalizeMalformedManualTrueUp").ToListAsync());
        var auditEvidence = JsonSerializer.Deserialize<RoomInventoryLossNormalizationAuditEvidence>(
            correctionAudit.AfterValuesJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(auditEvidence);
        Assert.Equal("TR108859", auditEvidence.ReceiptReference);
        Assert.Equal(208, auditEvidence.ReceiptId);
        Assert.Equal(28, auditEvidence.ReceivedBins);
        Assert.Equal(2, auditEvidence.DroppedBins);
        Assert.Equal(248, auditEvidence.CanonicalBalanceBeforeAdjustment);
        Assert.Equal(246, auditEvidence.CorrectedCurrentPackableBins);
        Assert.Equal(280, auditEvidence.MalformedAdjustmentId);
        Assert.Equal("ManualTrueUp", auditEvidence.OriginalAdjustmentType);
        Assert.Equal(28, auditEvidence.OriginalOldBinCount);
        Assert.Equal(218, auditEvidence.OriginalChangeAmount);
        Assert.Equal(246, auditEvidence.OriginalNewBinCount);
        Assert.Equal(RoomInventoryLossAdjustmentTypes.DroppedBins, auditEvidence.CorrectedAdjustmentType);
        Assert.Equal(248, auditEvidence.CorrectedOldBinCount);
        Assert.Equal(-2, auditEvidence.CorrectedChangeAmount);
        Assert.Equal(246, auditEvidence.CorrectedNewBinCount);
        Assert.Equal(Tr108859DroppedBinsCorrectionConstants.CorrectionRootCause, auditEvidence.RootCause);
        Assert.Contains("receipt-local 28-bin balance", auditEvidence.RootCause);
        Assert.Contains("248-bin canonical aggregate inventory identity", auditEvidence.RootCause);
        Assert.Equal(Tr108859DroppedBinsCorrectionConstants.CorrectionBusinessEvent, auditEvidence.BusinessEvent);
        Assert.True(auditEvidence.ReceiptQuantityWasNotChanged);
        Assert.True(auditEvidence.OriginalAuditWasRetained);
        await fixture.Invariant.ValidateBeforeCommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Applied_state_with_incomplete_correction_audit_fails_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var correction = fixture.CorrectionService();
        var preflight = await correction.PreflightAsync(CancellationToken.None);
        var applied = await correction.RunAsync(fixture.Request(preflight), CancellationToken.None);
        Assert.True(applied.Applied);

        var audit = await fixture.Db.AuditLogs.SingleAsync(x => x.Action == "NormalizeMalformedManualTrueUp");
        audit.AfterValuesJson = JsonSerializer.Serialize(new
        {
            receiptReference = "TR108859",
            receiptId = 208,
            receivedBins = 28,
            droppedBins = 2
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await correction.RunAsync(fixture.Request(applied.Preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.AlreadyApplied);
        Assert.Equal("Refused", result.Preflight.State);
        Assert.Contains(result.Preflight.Issues, x => x.Contains("correction audit", StringComparison.OrdinalIgnoreCase));
        Assert.Single(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "NormalizeMalformedManualTrueUp").ToListAsync());
        Assert.Equal(RoomInventoryLossAdjustmentTypes.DroppedBins, (await fixture.Db.RoomInventoryAdjustments.FindAsync(280L))!.AdjustmentType);
    }

    [Theory]
    [InlineData("wrong-delta")]
    [InlineData("wrong-note")]
    [InlineData("later-adjustment")]
    [InlineData("receipt-quantity")]
    [InlineData("physical-parent")]
    [InlineData("missing-original-audit")]
    public async Task Near_matches_are_refused_with_zero_correction_writes(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MutateAsync(mutation);
        var adjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var audits = await fixture.Db.AuditLogs.CountAsync();
        var receiptBins = await fixture.Db.Receipts.OrderBy(x => x.Id).Select(x => x.BinCount).ToListAsync();

        var result = await fixture.CorrectionService().RunAsync(
            fixture.Request("not-reviewed", "not-reviewed"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Refused", result.Preflight.State);
        Assert.NotEmpty(result.Preflight.Issues);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(adjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(audits, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(receiptBins, await fixture.Db.Receipts.OrderBy(x => x.Id).Select(x => x.BinCount).ToListAsync());
    }

    [Theory]
    [InlineData("token")]
    [InlineData("backup")]
    [InlineData("target-fingerprint")]
    [InlineData("protected-fingerprint")]
    [InlineData("administrator")]
    public async Task Apply_guards_refuse_with_zero_writes(string guard)
    {
        await using var fixture = await Fixture.CreateAsync();
        var preflight = await fixture.CorrectionService().PreflightAsync(CancellationToken.None);
        var request = fixture.Request(preflight);
        request = guard switch
        {
            "token" => request with { AuthorizationToken = "WRONG" },
            "backup" => request with { VerifiedBackupRunId = 999 },
            "target-fingerprint" => request with { ExpectedTargetFingerprint = "WRONG" },
            "protected-fingerprint" => request with { ExpectedProtectedFingerprint = "WRONG" },
            "administrator" => request with { RequestedByEmail = "not-admin@fruitandland.com" },
            _ => request
        };

        var result = await fixture.CorrectionService().RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal("ManualTrueUp", (await fixture.Db.RoomInventoryAdjustments.FindAsync(280L))!.AdjustmentType);
        Assert.DoesNotContain(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "NormalizeMalformedManualTrueUp");
    }

    [Fact]
    public async Task Production_requires_explicit_confirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var correction = fixture.CorrectionService(production: true);
        var preflight = await correction.PreflightAsync(CancellationToken.None);

        var result = await correction.RunAsync(fixture.Request(preflight) with { ConfirmProduction = false }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("confirm-production", result.Message);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
    }

    [Fact]
    public async Task Invariant_failure_rolls_back_parent_adjustment_and_correction_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var correction = fixture.CorrectionService(invariant: new RejectingInvariant());
        var preflight = await correction.PreflightAsync(CancellationToken.None);

        var result = await correction.RunAsync(fixture.Request(preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Ready", result.Preflight.State);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        var target = await fixture.Db.RoomInventoryAdjustments.FindAsync(280L);
        Assert.Equal("ManualTrueUp", target!.AdjustmentType);
        Assert.Equal(218, target.ChangeAmount);
        Assert.DoesNotContain(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "NormalizeMalformedManualTrueUp");
        Assert.NotNull(await fixture.Db.AuditLogs.FindAsync(23057L));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T06:00:00Z");
        private readonly SqliteConnection connection;

        private Fixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
            Ledger = new CanonicalTestLedger(db);
            Invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        }

        public CropQcDbContext Db { get; }
        public IRoomInventoryLedgerQueryService Ledger { get; }
        public InventoryDeductionInvariantService Invariant { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var warehouse = await db.Warehouses.FindAsync(1)
                ?? new Warehouse { Id = 1, Code = "EBS", Name = "Earl Brown & Sons" };
            var room = await db.Rooms.FindAsync(17)
                ?? new Room { Id = 17, Warehouse = warehouse, WarehouseId = 1, Code = "EVANS-7", Name = "EVANS-7", CropQcRoomName = "EVANS-7" };
            var growerLot = new GrowerLot { Id = 94, Grower = "DL & JJ FARMS - CLARENCE", LotNumber = "9040", CreatedAt = Now.AddDays(-10), UpdatedAt = Now.AddDays(-10) };
            var fruit = await db.FruitProfiles.FindAsync(2)
                ?? new FruitProfile { Id = 2, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var admin = new User { Id = 2, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now.AddYears(-1) };
            var adminRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
            if (db.Entry(warehouse).State == EntityState.Detached) db.Warehouses.Add(warehouse);
            if (db.Entry(room).State == EntityState.Detached) db.Rooms.Add(room);
            if (db.Entry(fruit).State == EntityState.Detached) db.FruitProfiles.Add(fruit);
            db.AddRange(growerLot, admin);
            db.UserRoles.Add(new UserRole { User = admin, Role = adminRole });

            var receiptShapes = new[]
            {
                (208L, "TR108859", 28, "2026-08-10T18:33:00Z", 251L),
                (209L, "TR108860", 29, "2026-08-10T21:14:00Z", 252L),
                (225L, "TR108861", 44, "2026-08-11T15:49:00Z", 270L),
                (226L, "TR108862", 44, "2026-08-11T17:23:00Z", 271L),
                (227L, "TR108863", 24, "2026-08-11T18:25:00Z", 272L),
                (228L, "TR108864", 44, "2026-08-11T19:23:00Z", 273L),
                (230L, "TR108865", 35, "2026-08-11T21:14:00Z", 275L)
            };
            foreach (var shape in receiptShapes)
            {
                var receivedAt = DateTimeOffset.Parse(shape.Item4);
                var receipt = new Receipt
                {
                    Id = shape.Item1,
                    CropYear = 2026,
                    ReceivedAt = receivedAt,
                    CompuTechReceiptId = shape.Item2,
                    ReceiptType = "Truck receipt",
                    Warehouse = warehouse,
                    WarehouseId = 1,
                    Room = room,
                    RoomId = 17,
                    FruitProfile = fruit,
                    FruitProfileId = 2,
                    GrowerLot = growerLot,
                    GrowerLotId = 94,
                    GrowerNumber = "9040",
                    GrowerName = "DL & JJ FARMS - CLARENCE",
                    LotCode = "9040",
                    BinCount = shape.Item3,
                    CreatedAt = receivedAt,
                    UpdatedAt = receivedAt
                };
                db.Receipts.Add(receipt);
                db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
                {
                    Id = shape.Item5,
                    Receipt = receipt,
                    ReceiptId = receipt.Id,
                    Warehouse = warehouse,
                    WarehouseId = 1,
                    Room = room,
                    RoomId = 17,
                    GrowerName = receipt.GrowerName,
                    LotNumber = "",
                    OldBinCount = null,
                    ChangeAmount = receipt.BinCount,
                    NewBinCount = receipt.BinCount,
                    AdjustmentType = "ReceiptAdd",
                    AdjustmentAt = receivedAt,
                    CreatedAt = receivedAt
                });
            }
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = 280,
                ReceiptId = 208,
                CropYear = 2026,
                Warehouse = warehouse,
                WarehouseId = 1,
                Room = room,
                RoomId = 17,
                GrowerLot = growerLot,
                GrowerLotId = 94,
                FruitProfile = fruit,
                FruitProfileId = 2,
                GrowerName = "DL & JJ FARMS - CLARENCE",
                LotNumber = "9040",
                VarietyCode = "GALA",
                OldBinCount = 28,
                ChangeAmount = 218,
                NewBinCount = 246,
                AdjustmentType = "ManualTrueUp",
                Source = "Two Dropped Bins",
                Reason = "Two Dropped Bins",
                Notes = Tr108859DroppedBinsCorrectionConstants.HistoricalNotes,
                AdjustmentAt = DateTimeOffset.Parse("2026-08-11T22:23:00Z"),
                CreatedAt = DateTimeOffset.Parse("2026-08-11T22:25:24.118888Z"),
                CreatedByUserId = 2
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = 23057,
                UserId = 2,
                Action = "BinCountChange",
                EntityName = nameof(RoomInventoryAdjustment),
                EntityKey = "208",
                AfterValuesJson = "ManualTrueUp changed bins from 28 to 246. Reason: Two Dropped Bins",
                SourceApplication = "Web",
                CreatedAt = DateTimeOffset.Parse("2026-08-11T22:25:24.120349Z")
            });
            db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = Tr108859DroppedBinsCorrectionConstants.VerifiedRestoreBackupRunId,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "Disposable restored production",
                DatabaseProvider = "PostgreSQL",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = Now.AddMinutes(-5),
                CompletedAt = Now,
                VerifiedAt = Now,
                RetentionProcessedAt = Now,
                LeaseReleasedAt = Now,
                Sha256 = Tr108859DroppedBinsCorrectionConstants.VerifiedRestorePackageSha256
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(connection, db);
        }

        public Tr108859DroppedBinsCorrectionService CorrectionService(bool production = false, IInventoryDeductionInvariantService? invariant = null)
        {
            var configuration = new ConfigurationBuilder().Build();
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"))
                }
            };
            var lossService = new RoomInventoryLossService(
                Db,
                Ledger,
                invariant ?? Invariant,
                new UserAccessService(Db, configuration),
                accessor,
                new PacificBusinessTimeService(new FixedClock(Now)),
                NullLogger<RoomInventoryLossService>.Instance);
            return new Tr108859DroppedBinsCorrectionService(
                Db,
                new AppEnvironmentOptions { Kind = production ? AppEnvironmentKinds.Production : AppEnvironmentKinds.Development },
                new PacificBusinessTimeService(new FixedClock(Now)),
                Ledger,
                lossService,
                NullLogger<Tr108859DroppedBinsCorrectionService>.Instance);
        }

        public Tr108859DroppedBinsCorrectionRequest Request(Tr108859DroppedBinsCorrectionPreflight preflight) =>
            Request(preflight.TargetFingerprint, preflight.ProtectedFingerprint);

        public Tr108859DroppedBinsCorrectionRequest Request(string targetFingerprint, string protectedFingerprint) => new(
            true,
            false,
            true,
            Tr108859DroppedBinsCorrectionConstants.VerifiedRestoreBackupRunId,
            Tr108859DroppedBinsCorrectionConstants.VerifiedRestorePackageSha256,
            ApplicationAreas.OwnerEmail,
            "Normalize the exact reviewed malformed true-up as two dropped bins.",
            targetFingerprint,
            protectedFingerprint,
            Tr108859DroppedBinsCorrectionConstants.ApplyAuthorizationToken);

        public async Task MutateAsync(string mutation)
        {
            switch (mutation)
            {
                case "wrong-delta":
                    var deltaTarget = await Db.RoomInventoryAdjustments.FindAsync(280L);
                    deltaTarget!.ChangeAmount = 217;
                    deltaTarget.NewBinCount = 245;
                    break;
                case "wrong-note":
                    (await Db.RoomInventoryAdjustments.FindAsync(280L))!.Notes = "Similar, but not exact";
                    break;
                case "later-adjustment":
                    var source = await Db.RoomInventoryAdjustments.FindAsync(280L);
                    Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
                    {
                        Id = 281,
                        ReceiptId = 208,
                        CropYear = 2026,
                        WarehouseId = 1,
                        RoomId = 17,
                        GrowerLotId = 94,
                        FruitProfileId = 2,
                        GrowerName = source!.GrowerName,
                        LotNumber = "9040",
                        VarietyCode = "GALA",
                        OldBinCount = 246,
                        ChangeAmount = 1,
                        NewBinCount = 247,
                        AdjustmentType = "ManualTrueUp",
                        AdjustmentAt = source.AdjustmentAt.AddMinutes(1),
                        CreatedAt = source.CreatedAt.AddMinutes(1)
                    });
                    break;
                case "receipt-quantity":
                    (await Db.Receipts.FindAsync(209L))!.BinCount = 30;
                    break;
                case "physical-parent":
                    Db.RoomDepletions.Add(new RoomDepletion
                    {
                        ReceiptId = 208,
                        WarehouseId = 1,
                        RoomId = 17,
                        FruitProfileId = 2,
                        GrowerName = "DL & JJ FARMS - CLARENCE",
                        LotCode = "9040",
                        BinCountDepleted = 1,
                        DepletedAt = Now.AddMinutes(-1),
                        CreatedAt = Now
                    });
                    break;
                case "missing-original-audit":
                    Db.AuditLogs.Remove((await Db.AuditLogs.FindAsync(23057L))!);
                    break;
            }
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class RejectingInvariant : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) =>
            throw new InventoryDeductionInvariantException("Injected invariant failure.");

        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryDeductionReadinessResult(0, 0, 0, []));
    }

    private sealed class CanonicalTestLedger(CropQcDbContext db) : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            CancellationToken cancellationToken) =>
            GetSnapshotsAsync(warehouseId, roomIds, null, cancellationToken);

        public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            int? fruitProfileId,
            CancellationToken cancellationToken)
        {
            var rows = await db.RoomInventoryAdjustments.AsNoTracking()
                .Include(x => x.Warehouse).Include(x => x.Room)
                .Include(x => x.FruitProfile)
                .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
                .Where(x => warehouseId == null || x.WarehouseId == warehouseId)
                .Where(x => roomIds == null || roomIds.Contains(x.RoomId))
                .ToListAsync(cancellationToken);
            var snapshots = rows
                .Select(x => new
                {
                    Row = x,
                    CropYear = x.CropYear ?? x.Receipt?.CropYear,
                    GrowerLotId = x.GrowerLotId ?? x.Receipt?.GrowerLotId,
                    FruitProfileId = x.FruitProfileId ?? x.Receipt?.FruitProfileId,
                    Lot = string.IsNullOrWhiteSpace(x.LotNumber) ? x.Receipt?.GrowerNumber ?? x.Receipt?.LotCode ?? "" : x.LotNumber,
                    Variety = x.FruitProfile?.VarietyCode ?? x.Receipt?.FruitProfile.VarietyCode ?? x.VarietyCode ?? ""
                })
                .Where(x => fruitProfileId == null || x.FruitProfileId == fruitProfileId)
                .GroupBy(x => new { x.Row.WarehouseId, x.Row.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.Lot, x.Variety })
                .Select(group =>
                {
                    var latest = group.OrderByDescending(x => x.Row.AdjustmentAt).ThenByDescending(x => x.Row.Id).First().Row;
                    var profile = latest.FruitProfile ?? latest.Receipt?.FruitProfile;
                    var current = group.Sum(x => x.Row.ChangeAmount);
                    var dropped = group.Where(x => x.Row.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins).Sum(x => -x.Row.ChangeAmount);
                    return new RoomInventoryLedgerSnapshot(
                        latest.WarehouseId,
                        latest.Warehouse.Code,
                        latest.RoomId,
                        latest.Room.CropQcRoomName ?? latest.Room.DisplayName ?? latest.Room.Code,
                        "",
                        group.Key.CropYear,
                        group.Key.GrowerLotId,
                        group.Key.FruitProfileId,
                        latest.GrowerName,
                        latest.Receipt?.GrowerNumber ?? "9040",
                        group.Key.Lot,
                        latest.PoolStart,
                        latest.VarietyCode ?? "",
                        group.Key.Variety,
                        profile?.Name ?? group.Key.Variety,
                        profile?.FruitType ?? "",
                        profile?.ProductionType ?? "",
                        profile?.IsOrganic,
                        latest.InventoryStatus ?? "",
                        group.Where(x => x.Row.ChangeAmount > 0).Sum(x => x.Row.ChangeAmount),
                        group.Where(x => x.Row.ChangeAmount < 0).Sum(x => x.Row.ChangeAmount),
                        0, 0, 0, 0, 0,
                        group.Where(x => x.Row.AdjustmentType == "ManualTrueUp").Sum(x => x.Row.ChangeAmount),
                        current + dropped,
                        current,
                        group.Count(),
                        group.Min(x => x.Row.AdjustmentAt),
                        group.Max(x => x.Row.AdjustmentAt),
                        latest.Id,
                        latest.Source ?? "",
                        dropped,
                        0);
                })
                .ToList();
            return snapshots;
        }
    }
}
