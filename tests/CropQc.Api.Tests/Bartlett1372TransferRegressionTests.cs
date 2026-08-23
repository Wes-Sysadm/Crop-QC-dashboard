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
            [new Claim(ClaimTypes.Email, actor.Email), new Claim(ClaimTypes.Role, BuiltInRoleNames.Admin)], "Bartlett1372Probe"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var configuration = new ConfigurationBuilder().Build();
        var access = new UserAccessService(db, configuration);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var businessTime = new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-23T16:00:00Z")));
        var treatments = new RoomTreatmentService(
            db, ledger, access, accessor, businessTime, NullLogger<RoomTreatmentService>.Instance);
        var dashboard = CreateDashboardService(db, principal, ledger, treatments, businessTime);

        const int sourceWarehouseId = 3;
        const int sourceRoomId = 68;
        const int destinationRoomId = 66;
        var sealing = new RoomSealingService(db, businessTime);
        foreach (var roomId in new[] { sourceRoomId, destinationRoomId })
        {
            var initialSeal = (await sealing.GetConfirmationAsync(roomId, principal, default))!.Form;
            if (!initialSeal.ExpectedIsSealed) continue;
            initialSeal.Note = "Prepare disposable Bartlett transfer with both Rooms unsealed";
            Assert.Null(await sealing.ChangeStateAsync(initialSeal, false, principal, default));
            db.ChangeTracker.Clear();
        }
        var sourceRoom = await db.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleAsync(x => x.Id == sourceRoomId);
        var destinationRoom = await db.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleAsync(x => x.Id == destinationRoomId);
        Assert.Equal("McDougall", sourceRoom.Warehouse.Code);
        Assert.Equal("MCD-16", sourceRoom.Code);
        Assert.Equal("MCD-14", destinationRoom.Code);

        var activeRoomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
        var allRoomSnapshots = await ledger.GetSnapshotsAsync(null, activeRoomIds, default);
        var mcd09Id = await db.Rooms.AsNoTracking().Where(x => x.Code == "MCD-09").Select(x => x.Id).SingleAsync();
        var evans09Id = await db.Rooms.AsNoTracking().Where(x => x.Code == "EVANS-9").Select(x => x.Id).SingleAsync();
        var mcd09Bins = allRoomSnapshots.Where(x => x.RoomId == mcd09Id).Sum(x => x.CurrentBins);
        var evans09Bins = allRoomSnapshots.Where(x => x.RoomId == evans09Id).Sum(x => x.CurrentBins);
        var allRoomBins = allRoomSnapshots.Sum(x => x.CurrentBins);
        Assert.True(mcd09Bins > 0);
        Assert.True(evans09Bins > 0);
        Assert.True(allRoomBins >= mcd09Bins + evans09Bins);
        Console.WriteLine(
            $"Authoritative inventory: MCD-09={mcd09Bins}; EVANS-9={evans09Bins}; all active Rooms={allRoomBins}.");

        var snapshots = (await ledger.GetSnapshotsAsync(sourceWarehouseId, [sourceRoomId], default))
            .Where(x => x.CurrentBins > 0
                && x.CropYear == 2026
                && x.FruitProfileId == 17
                && string.Equals(x.GrowerNumber ?? x.Lot, "1372", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sourceSnapshot = Assert.Single(snapshots);
        var expectedSourceBins = sourceSnapshot.CurrentBins;
        var expectedDestinationBins = Assert.Single(
            await ledger.GetSnapshotsAsync(sourceWarehouseId, [destinationRoomId], default),
            x => x.CurrentBins > 0 && SameIdentity(x, sourceSnapshot)).CurrentBins;
        Assert.True(expectedSourceBins > 0);
        Assert.True(expectedDestinationBins > 0);
        Assert.Equal(448, sourceSnapshot.GrowerLotId);
        Console.WriteLine(
            $"Authoritative Bartlett 1372 baseline: MCD-16={expectedSourceBins}; MCD-14={expectedDestinationBins}.");

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
        var treatmentSegmentBinsBefore = await db.TreatmentLineageSegments.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.CurrentBins);
        var page = await dashboard.GetRoomDetailAsync(sourceRoomId, default);
        Assert.True(page.TransferInventoryReconciles, page.TransferInventoryError);
        var options = page.TransferLotOptions.Where(x => x.CurrentBins > 0
            && x.Label.Contains("TOP PEAR CONV 1372 BART", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(options);
        Assert.Equal(expectedSourceBins, options.Sum(x => x.CurrentBins));
        Assert.All(options, x => Assert.Equal("u", x.TreatmentSignature));
        var selected = Assert.Single(options, x => x.TreatmentSegmentId is not null);
        Console.WriteLine(
            $"Selected current provenance: receipt={selected.TreatmentReceiptId}; segment={selected.TreatmentSegmentId}; " +
            $"bins={selected.CurrentBins}; currentRoom={sourceRoomId}.");

        var runKey = Guid.NewGuid().ToString("N");
        var sourceSeal = (await sealing.GetConfirmationAsync(sourceRoomId, principal, default))!.Form;
        sourceSeal.Note = "Disposable Bartlett source seal interaction proof";
        Assert.Null(await sealing.ChangeStateAsync(sourceSeal, true, principal, default));
        db.ChangeTracker.Clear();
        var sourceBlocked = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = $"run92-bartlett-source-sealed-{runKey}",
            FromRoomId = sourceRoomId,
            DestinationWarehouseId = sourceWarehouseId,
            DestinationRoomId = destinationRoomId,
            SourceLotKey = selected.LotKey,
            TreatmentSignature = selected.TreatmentSignature,
            TreatmentSegmentId = selected.TreatmentSegmentId,
            BinCount = 1,
            TransferAt = DateTimeOffset.Parse("2026-08-23T16:00:00Z"),
            Reason = "Disposable sealed-source rejection proof"
        }, default);
        Assert.Contains("sealed", sourceBlocked, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeTransfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeMovements, await db.TreatmentLineageMovements.CountAsync());
        var sourceUnseal = (await sealing.GetConfirmationAsync(sourceRoomId, principal, default))!.Form;
        sourceUnseal.Note = "Restore Bartlett source after seal interaction proof";
        Assert.Null(await sealing.ChangeStateAsync(sourceUnseal, false, principal, default));
        db.ChangeTracker.Clear();

        var destinationSeal = (await sealing.GetConfirmationAsync(destinationRoomId, principal, default))!.Form;
        destinationSeal.Note = "Disposable Bartlett destination seal interaction proof";
        Assert.Null(await sealing.ChangeStateAsync(destinationSeal, true, principal, default));
        db.ChangeTracker.Clear();
        var destinationBlocked = await dashboard.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = $"run92-bartlett-destination-sealed-{runKey}",
            FromRoomId = sourceRoomId,
            DestinationWarehouseId = sourceWarehouseId,
            DestinationRoomId = destinationRoomId,
            SourceLotKey = selected.LotKey,
            TreatmentSignature = selected.TreatmentSignature,
            TreatmentSegmentId = selected.TreatmentSegmentId,
            BinCount = 1,
            TransferAt = DateTimeOffset.Parse("2026-08-23T16:00:00Z"),
            Reason = "Disposable sealed-destination rejection proof"
        }, default);
        Assert.Contains("sealed", destinationBlocked, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeTransfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeMovements, await db.TreatmentLineageMovements.CountAsync());
        var destinationUnseal = (await sealing.GetConfirmationAsync(destinationRoomId, principal, default))!.Form;
        destinationUnseal.Note = "Restore Bartlett destination after seal interaction proof";
        Assert.Null(await sealing.ChangeStateAsync(destinationUnseal, false, principal, default));
        db.ChangeTracker.Clear();

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
            TransferAt = DateTimeOffset.Parse("2026-08-23T16:00:00Z"),
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
        var destinationSegment = await db.TreatmentLineageSegments.AsNoTracking()
            .SingleAsync(x => x.Id == movement.DestinationSegmentId);
        Assert.Equal(selected.TreatmentReceiptId, destinationSegment.ReceiptId);
        Assert.Equal(448, destinationSegment.GrowerLotId);
        Assert.Equal(17, destinationSegment.FruitProfileId);
        var expectedDestinationSegmentBins = treatmentSegmentBinsBefore.TryGetValue(destinationSegment.Id, out var priorBins)
            ? priorBins + 1
            : 1;
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
            expectedDestinationSegmentBins,
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
