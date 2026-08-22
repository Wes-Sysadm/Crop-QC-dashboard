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

namespace CropQc.Api.Tests;

public sealed class Bartlett1372TransferRegressionTests
{
    [Fact]
    public async Task Exact_restored_Bartlett_1372_room16_to_room14_transfers_one_exact_segment_and_reverses()
    {
        var currentRestore = Environment.GetEnvironmentVariable("BARTLETT_1372_CURRENT_RESTORE_CONNECTION_STRING");
        var connectionString = currentRestore
            ?? Environment.GetEnvironmentVariable("BARTLETT_1372_RESTORE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);

        await using var db = new CropQcDbContext(
            new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options);
        var actor = await db.Users.AsNoTracking().SingleAsync(x => x.Email == ApplicationAreas.OwnerEmail && x.IsActive);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, actor.Email)], "Bartlett1372Probe"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var configuration = new ConfigurationBuilder().Build();
        var access = new UserAccessService(db, configuration);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var businessTime = new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-21T08:30:00Z")));
        var treatments = new RoomTreatmentService(
            db, ledger, access, accessor, businessTime, NullLogger<RoomTreatmentService>.Instance);
        var dashboard = CreateDashboardService(db, principal, ledger, treatments, businessTime);

        const int sourceWarehouseId = 3;
        const int sourceRoomId = 68;
        const int destinationRoomId = 66;
        var sourceRoom = await db.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleAsync(x => x.Id == sourceRoomId);
        var destinationRoom = await db.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleAsync(x => x.Id == destinationRoomId);
        Assert.Equal("McDougall", sourceRoom.Warehouse.Code);
        Assert.Equal("MCD-16", sourceRoom.Code);
        Assert.Equal("MCD-14", destinationRoom.Code);

        var snapshots = (await ledger.GetSnapshotsAsync(sourceWarehouseId, [sourceRoomId], default))
            .Where(x => x.CurrentBins > 0
                && x.CropYear == 2026
                && x.FruitProfileId == 17
                && string.Equals(x.GrowerNumber ?? x.Lot, "1372", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sourceSnapshot = Assert.Single(snapshots);
        var expectedSourceBins = currentRestore is null ? 894 : 1028;
        var expectedDestinationBins = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [destinationRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot)).CurrentBins;
        if (currentRestore is null) Assert.Equal(66, expectedDestinationBins);
        else Assert.Equal(572, expectedDestinationBins);
        Assert.Equal(expectedSourceBins, sourceSnapshot.CurrentBins);
        Assert.Equal(448, sourceSnapshot.GrowerLotId);

        var treatmentSelections = await treatments.GetSelectionsAsync(snapshots, default);
        Assert.Equal(expectedSourceBins, treatmentSelections.Values.SelectMany(x => x).Sum(x => x.CurrentBins));
        Assert.All(treatmentSelections.Values.SelectMany(x => x), x => Assert.Equal(TreatmentLineageStates.Untreated, x.TreatmentState));

        var variety = new InventoryByVarietyService(
            db,
            ledger,
            treatments,
            new NoWriteVarietyColors(),
            new FacilityContextService(db));
        var varietyBefore = Assert.Single(
            (await variety.GetSummaryAsync("All", default)).Varieties,
            x => string.Equals(x.VarietyKey, "BARTLETT", StringComparison.OrdinalIgnoreCase)).CurrentBins;
        var globalBefore = (await ledger.GetSnapshotsAsync(null, null, default))
            .Where(x => SameIdentity(x, sourceSnapshot))
            .Sum(x => x.CurrentBins);
        var beforeTransfers = await db.RoomTransfers.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var beforeMovements = await db.TreatmentLineageMovements.CountAsync();
        var page = await dashboard.GetRoomDetailAsync(sourceRoomId, default);
        Assert.True(page.TransferInventoryReconciles, page.TransferInventoryError);
        var options = page.TransferLotOptions.Where(x => x.CurrentBins > 0
            && x.Label.Contains("TOP PEAR CONV 1372 BART", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(currentRestore is null ? 2 : 3, options.Count);
        Assert.Equal(expectedSourceBins, options.Sum(x => x.CurrentBins));
        Assert.All(options, x => Assert.Equal("u", x.TreatmentSignature));
        var selected = currentRestore is null
            ? Assert.Single(options, x => x.TreatmentSegmentId is not null)
            : Assert.Single(options, x => x.TreatmentReceiptId == 774);
        if (currentRestore is not null)
        {
            Assert.Equal(122, selected.TreatmentSegmentId);
            Assert.Equal(134, selected.CurrentBins);
            Assert.NotEqual(sourceRoomId, await db.Receipts.AsNoTracking()
                .Where(x => x.Id == selected.TreatmentReceiptId)
                .Select(x => x.RoomId)
                .SingleAsync());
        }

        var runKey = Guid.NewGuid().ToString("N");
        var transferOperationKey = $"run92-bartlett-1372-room16-room14-{runKey}";
        var error = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = transferOperationKey,
            FromRoomId = sourceRoomId,
            DestinationWarehouseId = sourceWarehouseId,
            DestinationRoomId = destinationRoomId,
            SourceLotKey = selected.LotKey,
            TreatmentSignature = selected.TreatmentSignature,
            TreatmentSegmentId = selected.TreatmentSegmentId,
            BinCount = 1,
            TransferAt = DateTimeOffset.Parse("2026-08-21T08:30:00Z"),
            Reason = "Disposable exact Bartlett transfer probe"
        }, default);

        Assert.Null(error);
        Assert.Equal(beforeTransfers + 1, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments + 2, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeMovements + 1, await db.TreatmentLineageMovements.CountAsync());
        var transfer = await db.RoomTransfers.SingleAsync(x => x.OperationKey == transferOperationKey);
        Assert.Equal(448, transfer.GrowerLotId);
        Assert.Equal(17, transfer.FruitProfileId);
        Assert.Equal("1372", transfer.LotNumber);
        Assert.Equal("BART", transfer.VarietyCode);
        Assert.Equal(1, transfer.BinCount);

        var movement = await db.TreatmentLineageMovements.AsNoTracking()
            .SingleAsync(x => x.RoomTransferId == transfer.Id && x.ReversesTreatmentLineageMovementId == null);
        Assert.Equal(selected.TreatmentSegmentId, movement.SourceSegmentId);
        Assert.NotNull(movement.DestinationSegmentId);
        var sourceAfter = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [sourceRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot));
        var destinationAfter = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [destinationRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot));
        Assert.Equal(expectedSourceBins - 1, sourceAfter.CurrentBins);
        Assert.Equal(expectedDestinationBins + 1, destinationAfter.CurrentBins);
        Assert.Equal(expectedSourceBins + expectedDestinationBins, sourceAfter.CurrentBins + destinationAfter.CurrentBins);
        var sourceSegmentsAfter = await treatments.GetSelectionsAsync(sourceAfter, default);
        var destinationSegmentsAfter = await treatments.GetSelectionsAsync(destinationAfter, default);
        Assert.Equal(expectedSourceBins - 1, sourceSegmentsAfter.Sum(x => x.CurrentBins));
        Assert.All(sourceSegmentsAfter, x => Assert.Equal(selected.TreatmentSignature, x.TreatmentSignature));
        Assert.Equal(
            1,
            Assert.Single(destinationSegmentsAfter, x => x.SegmentId == movement.DestinationSegmentId).CurrentBins);
        Assert.Equal(selected.TreatmentSignature, Assert.Single(
            destinationSegmentsAfter,
            x => x.SegmentId == movement.DestinationSegmentId).TreatmentSignature);
        Assert.Equal(globalBefore, (await ledger.GetSnapshotsAsync(null, null, default))
            .Where(x => SameIdentity(x, sourceSnapshot))
            .Sum(x => x.CurrentBins));
        Assert.Equal(varietyBefore, Assert.Single(
            (await variety.GetSummaryAsync("All", default)).Varieties,
            x => string.Equals(x.VarietyKey, "BARTLETT", StringComparison.OrdinalIgnoreCase)).CurrentBins);

        var reverseError = await dashboard.ReverseRoomTransferAsync(new ReverseRoomTransferForm
        {
            Id = transfer.Id,
            OperationKey = $"run92-bartlett-1372-room16-room14-reversal-{runKey}",
            Reason = "Disposable exact Bartlett restoration probe"
        }, default);
        Assert.Null(reverseError);
        var sourceRestored = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [sourceRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot));
        var destinationRestored = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [destinationRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot));
        Assert.Equal(expectedSourceBins, sourceRestored.CurrentBins);
        Assert.Equal(expectedDestinationBins, destinationRestored.CurrentBins);
        var sourceSegmentsRestored = await treatments.GetSelectionsAsync(sourceRestored, default);
        Assert.Equal(expectedSourceBins, sourceSegmentsRestored.Sum(x => x.CurrentBins));
        Assert.All(sourceSegmentsRestored, x => Assert.Equal(selected.TreatmentSignature, x.TreatmentSignature));
        Assert.Equal(globalBefore, (await ledger.GetSnapshotsAsync(null, null, default))
            .Where(x => SameIdentity(x, sourceSnapshot))
            .Sum(x => x.CurrentBins));
        Assert.Equal(varietyBefore, Assert.Single(
            (await variety.GetSummaryAsync("All", default)).Varieties,
            x => string.Equals(x.VarietyKey, "BARTLETT", StringComparison.OrdinalIgnoreCase)).CurrentBins);
    }

    private static bool SameIdentity(RoomInventoryLedgerSnapshot x, RoomInventoryLedgerSnapshot expected) =>
        x.CropYear == expected.CropYear
        && x.GrowerLotId == expected.GrowerLotId
        && x.FruitProfileId == expected.FruitProfileId
        && string.Equals(x.GrowerNumber ?? x.Lot, expected.GrowerNumber ?? expected.Lot, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Variety, expected.Variety, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.ProductionType, expected.ProductionType, StringComparison.OrdinalIgnoreCase)
        && x.IsOrganic == expected.IsOrganic;

    private static DashboardDataService CreateDashboardService(
        CropQcDbContext db,
        ClaimsPrincipal principal,
        IRoomInventoryLedgerQueryService ledger,
        IRoomTreatmentService treatments,
        IBusinessTimeService businessTime)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            null!,
            new FileStorageOptions(),
            new EmailOptions(),
            null!,
            new GoogleAuthenticationOptions(),
            null!,
            null!,
            new QcPhotoRequirementPolicy(),
            null!,
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            configuration,
            NullLogger<DashboardDataService>.Instance,
            new UserAccessService(db, configuration),
            businessTime: businessTime,
            roomInventoryLedgerQueryService: ledger,
            roomTreatmentService: treatments);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class NoWriteVarietyColors : IVarietyColorService
    {
        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsReadOnlyAsync(
            IEnumerable<string> varietyKeys,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, VarietyColorResolved>>(varietyKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x,
                    x => new VarietyColorResolved(
                        x,
                        VarietyColorService.NormalizeIdentity(x, x).Name,
                        VarietyColorService.FallbackColor(x),
                        false),
                    StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(
            IEnumerable<string> varietyKeys,
            CancellationToken cancellationToken) =>
            GetResolvedColorsReadOnlyAsync(varietyKeys, cancellationToken);

        public Task<VarietyColorsAdminViewModel> GetAdminPageAsync(
            bool canManage,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsForMasterDataAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> SaveAsync(
            VarietyColorForm form,
            string changedByEmail,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ResetAsync(
            VarietyColorForm form,
            string changedByEmail,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
