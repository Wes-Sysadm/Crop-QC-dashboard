using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Web.Services;

public interface IBinsRunService
{
    Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<ActualRunDetailViewModel?> GetActualRunDetailAsync(long id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<BinsRunProjectionViewModel> GetProjectionAsync(BinsRunProjectionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunProjectionInventorySource>> SearchPlanningInventoryAsync(string? query, int? warehouseId, int? roomId, int take, CancellationToken cancellationToken);
    Task<RunProjectionInventorySource?> GetPlanningInventoryAsync(string inventoryKey, CancellationToken cancellationToken);
    Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> CreateActualRunAsync(ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateActualRunAsync(long id, ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> CancelActualRunAsync(CancelActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ApproveActualRunOverrideAsync(ApproveActualRunOverrideForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class BinsRunService(
    CropQcDbContext dbContext,
    IUserAccessService userAccessService,
    ILogger<BinsRunService> logger,
    IRoomInventoryLedgerQueryService? roomInventoryLedgerQueryService = null,
    IInventoryDeductionInvariantService? inventoryDeductionInvariantService = null,
    IRunExpectationService? runExpectationService = null,
    IConfiguration? configuration = null,
    ICanonicalGrowerService? canonicalGrowerService = null,
    IRoomTreatmentService? roomTreatmentService = null) : IBinsRunService
{
    public const string AdjustmentType = "BinsRun";
    public const string ReversalAdjustmentType = "BinsRunReversal";
    public const string SourceApplication = "CropQc.Web";
    private IRoomInventoryLedgerQueryService RoomInventoryLedger { get; } =
        roomInventoryLedgerQueryService ?? new RoomInventoryLedgerQueryService(dbContext);
    private IInventoryDeductionInvariantService InventoryInvariant { get; } =
        inventoryDeductionInvariantService
        ?? new InventoryDeductionInvariantService(dbContext, NullLogger<InventoryDeductionInvariantService>.Instance);
    private IRunExpectationService RunExpectations { get; } =
        runExpectationService
        ?? new RunExpectationService(dbContext, NullLogger<RunExpectationService>.Instance);
    private int AuthoritativeStartCropYear { get; } = Math.Clamp(
        configuration?.GetValue(
            "RunReporting:AuthoritativeStartCropYear",
            RunReportingService.DefaultAuthoritativeStartCropYear)
            ?? RunReportingService.DefaultAuthoritativeStartCropYear,
        2000,
        2200);

    public async Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var currentEmail = user.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var currentEmployment = currentEmail is null
            ? EmploymentFacilities.Unassigned
            : await dbContext.Users.AsNoTracking()
                .Where(x => x.Email == currentEmail && x.IsActive)
                .Select(x => x.EmploymentFacility)
                .SingleOrDefaultAsync(cancellationToken) ?? EmploymentFacilities.Unassigned;
        currentEmployment = EmploymentFacilities.Normalize(currentEmployment) ?? EmploymentFacilities.Unassigned;
        var runFacilityCandidates = await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && (x.Code == EmploymentFacilities.Wp || x.Code == EmploymentFacilities.Ebs))
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var facilityConfigurationValid = new[] { EmploymentFacilities.Wp, EmploymentFacilities.Ebs }
            .All(code => runFacilityCandidates.Count(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) == 1);
        var runFacilities = facilityConfigurationValid ? runFacilityCandidates : [];
        var forcedFacility = currentEmployment is EmploymentFacilities.Wp or EmploymentFacilities.Ebs
            ? runFacilities.FirstOrDefault(x => x.Code == currentEmployment)
            : null;
        var canRecord = facilityConfigurationValid
            && (await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken)
                || await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Edit, cancellationToken));
        var canAdmin = await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, cancellationToken)
            || await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Admin, cancellationToken);
        var canTransfer = await userAccessService.HasAccessAsync(user, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
        var canTrueUp = await userAccessService.HasAccessAsync(user, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        var editInventoryRows = filter.EditActualRunId is long requestedRunId
            ? await dbContext.BinsRunEntries.AsNoTracking()
                .Where(x => x.ActualRunId == requestedRunId
                    && x.TransactionType == ActualRunTransactionTypes.Depletion
                    && !x.IsReversed)
                .Select(x => new
                {
                    x.WarehouseId,
                    x.RoomId,
                    x.CropYear,
                    x.GrowerLotId,
                    x.LotNumber,
                    Variety = x.VarietyCode ?? "",
                    x.FruitProfileId
                })
                .ToListAsync(cancellationToken)
            : [];
        if (editInventoryRows.Count > 0 && filter.RoomIds.Count == 0)
        {
            filter.RoomIds = editInventoryRows
                .Select(x => x.RoomId)
                .Distinct()
                .ToList();
            filter.SelectionMode = ActualRunSelectionModes.ByRoom;
            var editWarehouseIds = editInventoryRows.Select(x => x.WarehouseId).Distinct().ToList();
            if (filter.WarehouseId is null && editWarehouseIds.Count == 1)
            {
                filter.WarehouseId = editWarehouseIds[0];
            }
        }
        var selectedRoomIds = filter.RoomIds.Where(x => x > 0).Distinct().ToList();
        if (filter.RoomId is int selectedRoomId && !selectedRoomIds.Contains(selectedRoomId))
        {
            selectedRoomIds.Add(selectedRoomId);
        }
        filter.RoomIds = selectedRoomIds;

        if (filter.WarehouseId is null && selectedRoomIds.Count > 0)
        {
            var selectedWarehouseIds = await dbContext.Rooms.AsNoTracking()
                .Where(x => selectedRoomIds.Contains(x.Id))
                .Select(x => x.WarehouseId)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            if (selectedWarehouseIds.Count == 1)
            {
                filter.WarehouseId = selectedWarehouseIds[0];
            }
        }

        var isActualSection = filter.Section.Equals("Actual", StringComparison.OrdinalIgnoreCase);
        var selectionByVariety = string.Equals(filter.SelectionMode, ActualRunSelectionModes.ByVariety, StringComparison.OrdinalIgnoreCase);
        filter.SelectionMode = selectionByVariety ? ActualRunSelectionModes.ByVariety : ActualRunSelectionModes.ByRoom;
        var actualSelectionReady = filter.WarehouseId is not null
            && (selectionByVariety ? filter.FruitProfileId is not null : selectedRoomIds.Count > 0);
        IReadOnlyList<InventorySnapshot> snapshots = isActualSection && !actualSelectionReady
            ? []
            : await GetCurrentInventorySnapshotsForRoomsAsync(
                filter.WarehouseId,
                selectionByVariety || selectedRoomIds.Count == 0 ? null : selectedRoomIds,
                selectionByVariety ? filter.FruitProfileId : null,
                cancellationToken);
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
        snapshots = snapshots.Select(x => x with
        {
            Grower = growerResolver.DisplayName(x.Grower, x.GrowerNumber ?? x.Lot)
        }).ToList();
        var currentSnapshots = isActualSection
            ? snapshots.Where(x => x.CurrentBins > 0 || editInventoryRows.Any(y =>
                y.WarehouseId == x.WarehouseId
                && y.RoomId == x.RoomId
                && y.CropYear == x.CropYear
                && string.Equals(y.LotNumber, x.Lot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(y.Variety, x.Variety, StringComparison.OrdinalIgnoreCase)
                && (y.FruitProfileId is null || y.FruitProfileId == x.FruitProfileId)
                && (y.GrowerLotId is null || y.GrowerLotId == x.GrowerLotId))).ToList()
            : snapshots.Where(x => x.CurrentBins > 0).ToList();
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData = isActualSection
            ? new Dictionary<string, LotSampleDistribution>(StringComparer.OrdinalIgnoreCase)
            : await GetLatestSampleDataByLotAsync(currentSnapshots, cancellationToken);
        var options = await BuildAvailableInventoryOptionsAsync(currentSnapshots, sampleData, cancellationToken);
        var selectedOption = options.FirstOrDefault(x => string.Equals(x.InventoryKey, filter.SourceKey, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault();
        var roomSummary = isActualSection || filter.RoomId is null
            ? null
            : await BuildRoomSummaryAsync(filter.RoomId.Value, currentSnapshots, sampleData, cancellationToken);

        var historyQuery = dbContext.BinsRunEntries.AsNoTracking()
            .Include(x => x.Room)
            .Include(x => x.Warehouse)
            .Include(x => x.CreatedByUser)
            .Where(x => filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
            .Where(x => filter.RoomId == null || x.RoomId == filter.RoomId);
        if (filter.FromDate is DateTime fromDate)
        {
            historyQuery = historyQuery.Where(x => x.RunAt >= new DateTimeOffset(fromDate.Date));
        }

        if (filter.ToDate is DateTime toDate)
        {
            historyQuery = historyQuery.Where(x => x.RunAt < new DateTimeOffset(toDate.Date.AddDays(1)));
        }

        var rooms = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .Where(x => x.IsActive && (filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId))
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.SubLocation)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.CropQcRoomName ?? x.DisplayName ?? x.Code)
            .ToListAsync(cancellationToken);
        var actualRuns = await GetActualRunHistoryAsync(filter, cancellationToken);
        var editRun = filter.EditActualRunId is long editId
            ? actualRuns.SingleOrDefault(x => x.Id == editId && x.Status == ActualRunStatuses.Active)
            : null;
        var actualRunForm = new ActualRunForm
        {
            Id = editRun?.Id,
            ConcurrencyVersion = editRun?.ConcurrencyVersion ?? 0,
            RunAt = editRun?.RunAt ?? DateTimeOffset.UtcNow,
            RunProjectionId = editRun?.RunProjectionId,
            RunFacilityWarehouseId = editRun?.RunFacilityWarehouseId ?? forcedFacility?.Id,
            Notes = editRun?.Notes,
            Lines = editRun?.Lines
                .Where(x => x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
                .Select(x =>
                {
                    var currentOption = options.SingleOrDefault(y =>
                        y.RoomId == x.RoomId
                        && y.CropYear == x.CropYear
                        && string.Equals(y.Lot, x.Lot, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(y.Variety, x.Variety, StringComparison.OrdinalIgnoreCase)
                        && (x.FruitProfileId is null || y.FruitProfileId == x.FruitProfileId)
                        && (x.GrowerLotId is null || y.GrowerLotId == x.GrowerLotId)
                        && string.Equals(y.TreatmentSignature, x.TreatmentSignature, StringComparison.Ordinal));
                    return new ActualRunLineForm
                    {
                        InventoryKey = currentOption?.InventoryKey ?? x.InventoryKey,
                        TreatmentSignature = currentOption?.TreatmentSignature ?? x.TreatmentSignature,
                        BinsRun = x.BinsRun,
                        ExpectedAvailableBins = (currentOption?.CurrentBins ?? 0) + x.BinsRun
                    };
                })
                .ToList() ?? []
        };
        var inventorySelectionMessage = isActualSection && !facilityConfigurationValid
            ? "Run recording is unavailable because exactly one active WP warehouse and one active EBS warehouse are required."
            : !isActualSection
            ? null
            : filter.WarehouseId is null
                ? "Select a facility before loading current inventory."
                : selectionByVariety && filter.FruitProfileId is null
                    ? "Select a variety to load its positive room-lot balances across the facility."
                    : !selectionByVariety && selectedRoomIds.Count == 0
                        ? "Select one or more rooms to load their positive room-lot balances."
                        : options.Count == 0
                            ? selectionByVariety
                                ? "This variety has no positive current inventory in the selected facility."
                                : "The selected rooms have no positive current inventory."
                            : null;

        var history = await historyQuery
            .OrderByDescending(x => x.RunAt)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .Select(x => new BinsRunHistoryItemViewModel
            {
                Id = x.Id,
                InventoryKey = x.ReceiptId != null ? "R:" + x.ReceiptId.Value : $"A:{x.InventoryAdjustmentId}",
                WarehouseId = x.WarehouseId,
                RoomId = x.RoomId,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                GrowerName = x.GrowerName,
                GrowerNumber = x.GrowerNumberSnapshot ?? x.LotNumber,
                Variety = x.VarietyCode ?? "",
                Lot = x.LotNumber,
                PreviousAvailableBins = x.PreviousAvailableBins,
                BinsRun = x.BinsRun,
                NewAvailableBins = x.NewAvailableBins,
                RunAt = x.RunAt,
                CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                IsReversed = x.IsReversed,
                ReverseReason = x.ReverseReason,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);
        foreach (var item in history)
        {
            item.Inventory = $"{growerResolver.DisplayName(item.GrowerName, item.GrowerNumber)} - {item.Variety} - {item.Lot}";
        }

        return new BinsRunPageViewModel
        {
            Filter = filter,
            Form = new BinsRunForm
            {
                WarehouseId = filter.WarehouseId,
                RoomId = filter.RoomId,
                InventoryKey = selectedOption?.InventoryKey ?? "",
                TreatmentSignature = selectedOption?.TreatmentSignature ?? "",
                ExpectedAvailableBins = selectedOption?.CurrentBins ?? 0,
                RunAt = DateTimeOffset.Now,
                RunProjectionId = filter.ProjectionId,
                RunProjectionSourceId = filter.ProjectionSourceId
            },
            ActualRunForm = actualRunForm,
            Warehouses = await dbContext.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(cancellationToken),
            Rooms = rooms,
            FruitProfiles = isActualSection
                ? await dbContext.FruitProfiles.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.ProductionType)
                    .ToListAsync(cancellationToken)
                : [],
            RoomSummary = roomSummary,
            AvailableInventory = options,
            InventorySelectionMessage = inventorySelectionMessage,
            History = history,
            ActualRuns = actualRuns,
            PendingOverrideRequests = canAdmin
                ? await GetPendingOverrideRequestsAsync(filter, cancellationToken)
                : [],
            CanRecord = canRecord,
            CanAdmin = canAdmin,
            CanTransfer = canTransfer,
            CanTrueUp = canTrueUp,
            CurrentEmploymentFacility = currentEmployment,
            ForcedRunFacilityWarehouseId = forcedFacility?.Id,
            ForcedRunFacilityCode = forcedFacility?.Code,
            RequiresRunFacilitySelection = currentEmployment == EmploymentFacilities.Shared,
            RunFacilityOptions = runFacilities,
            SelectedAvailableBins = selectedOption?.CurrentBins
        };
    }

    public async Task<ActualRunDetailViewModel?> GetActualRunDetailAsync(
        long id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.View, cancellationToken))
        {
            throw new UnauthorizedAccessException("Actual Run View access is required.");
        }

        var run = await dbContext.ActualRuns.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ActualRunDetailViewModel
            {
                Id = x.Id,
                Status = x.Status,
                RevisionNumber = x.CurrentRevisionNumber,
                RunAt = x.RunAt,
                CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                Notes = x.Notes
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null) return null;
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);

        var contributionRows = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ActualRunId == id
                && x.ActualRunRevisionId != null
                && x.TransactionType == ActualRunTransactionTypes.Depletion
                && !x.IsReversed
                && x.ActualRunRevision!.IsCurrent)
            .OrderBy(x => x.RoomId)
            .ThenBy(x => x.LotNumber)
            .Select(x => new
            {
                x.Id,
                Facility = x.Warehouse.Code,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                Grower = x.GrowerName,
                GrowerNumber = x.GrowerNumberSnapshot ?? x.LotNumber,
                Lot = x.LotNumber,
                Variety = x.VarietyCode ?? "",
                ProductionType = x.FruitProfile == null ? (x.InventoryStatus ?? "") : x.FruitProfile.ProductionType,
                TreatmentSummary = x.TreatmentSummarySnapshot ?? "No recorded treatment history",
                x.CropYear,
                Bins = x.BinsRun
            })
            .Take(250)
            .ToListAsync(cancellationToken);
        run.TotalBins = contributionRows.Sum(x => x.Bins);
        run.Facility = contributionRows.Select(x => x.Facility).FirstOrDefault() ?? "";
        run.Contributions = contributionRows.Select(x => new ActualRunContributionViewModel(
            x.Id,
            x.Room,
            growerResolver.DisplayName(x.Grower, x.GrowerNumber),
            x.Lot,
            x.Variety,
            x.ProductionType,
            x.TreatmentSummary,
            x.CropYear,
            x.Bins,
            run.TotalBins <= 0 ? 0m : decimal.Round(x.Bins / (decimal)run.TotalBins * 100m, 4)))
            .ToList();

        try
        {
            var expectationRows = await dbContext.RunExpectations.AsNoTracking()
                .Where(x => x.ActualRunId == id)
                .OrderByDescending(x => x.RevisionNumber)
                .Take(50)
                .Select(x => new
                {
                    x.Id,
                    x.RevisionNumber,
                    x.TotalBins,
                    x.GrossPounds,
                    x.ExpectedPackoutPercent,
                    x.ExpectedPackedPounds,
                    x.ExpectedWholeBoxes,
                    x.ExpectedCullPounds,
                    x.ExpectedJuicePounds,
                    x.ExpectedPeelerPounds,
                    x.ExpectedWastePounds,
                    x.ConfidencePercent,
                    x.SizeDistributionSnapshotJson,
                    x.GradeDistributionSnapshotJson,
                    x.ConfigurationSnapshotJson,
                    x.CalculationVersion,
                    x.CalculatedAt
                })
                .ToListAsync(cancellationToken);
            run.Expectations = expectationRows
                .Select(x =>
                {
                    RunExpectationMetadata.TryGetHistoricalReconstruction(x.ConfigurationSnapshotJson, out var reconstruction);
                    return new RunExpectationViewModel
                    {
                        Id = x.Id,
                        RevisionNumber = x.RevisionNumber,
                        TotalBins = x.TotalBins,
                        GrossPounds = x.GrossPounds,
                        ExpectedPackoutPercent = x.ExpectedPackoutPercent,
                        ExpectedPackedPounds = x.ExpectedPackedPounds,
                        ExpectedWholeBoxes = x.ExpectedWholeBoxes,
                        ExpectedCullPounds = x.ExpectedCullPounds,
                        ExpectedJuicePounds = x.ExpectedJuicePounds,
                        ExpectedPeelerPounds = x.ExpectedPeelerPounds,
                        ExpectedWastePounds = x.ExpectedWastePounds,
                        ConfidencePercent = x.ConfidencePercent,
                        SizeDistribution = DeserializeDistribution(x.SizeDistributionSnapshotJson),
                        GradeDistribution = DeserializeDistribution(x.GradeDistributionSnapshotJson),
                        CalculationVersion = x.CalculationVersion,
                        CalculatedAt = x.CalculatedAt,
                        IsHistoricalReconstruction = reconstruction is not null,
                        ReconstructedAt = reconstruction?.ReconstructedAt,
                        PhysicalRunAt = reconstruction?.PhysicalRunAt,
                        QcEvidenceCutoff = reconstruction?.QcEvidenceCutoff,
                        ConfigurationBasis = reconstruction?.ConfigurationBasis,
                        CorrectionPackageIdentifier = reconstruction?.CorrectionPackageIdentifier
                    };
                })
                .ToList();
            run.CurrentExpectation = run.Expectations.SingleOrDefault(x => x.RevisionNumber == run.RevisionNumber);

            run.CanViewPackout = await userAccessService.HasAccessAsync(
                user,
                ApplicationAreas.PackoutResults,
                PageAccessLevel.View,
                cancellationToken);
            var packout = !run.CanViewPackout
                ? null
                : await dbContext.PackoutRuns.AsNoTracking()
                .Where(x => x.ActualRunId == id
                    || (x.ActualRunId == null
                        && x.BinsRunEntry != null
                        && x.BinsRunEntry.ActualRunId == id))
                .OrderByDescending(x => x.ActualRunId != null)
                .ThenByDescending(x => x.Id)
                .Select(x => new ActualRunPackoutViewModel
                {
                    Id = x.Id,
                    Status = x.Status,
                    DumpedBins = x.DumpedBins,
                    PackedPounds = x.PackedProductPounds,
                    JuicePounds = x.JuicePounds,
                    PeelerPounds = x.PeelerSlicerPounds,
                    WastePounds = x.WastePounds,
                    ActualPackoutPercent = x.ActualPackoutPercent,
                    AccuracyPercent = x.OverallAccuracyScore,
                    SizeAccuracyPercent = x.SizeAccuracyScore,
                    GradeAccuracyPercent = x.GradeAccuracyScore
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (packout is not null)
            {
                packout.PackoutVariancePercent = packout.ActualPackoutPercent - run.CurrentExpectation?.ExpectedPackoutPercent;
                var roomLotAllocations = await dbContext.PackoutSourceAllocations.AsNoTracking()
                    .Where(x => x.PackoutRunId == packout.Id)
                    .OrderBy(x => x.RunExpectationSource.RoomSnapshot)
                    .ThenBy(x => x.RunExpectationSource.LotSnapshot)
                    .Take(250)
                    .Select(x => new EstimatedAllocationViewModel(
                        x.RunExpectationSource.RoomSnapshot,
                        x.RunExpectationSource.GrowerSnapshot,
                        x.RunExpectationSource.LotSnapshot,
                        x.BinsContributed,
                        x.ContributionPercent,
                        x.AllocatedPackedPounds,
                        x.AllocatedWholeBoxes,
                        x.AllocatedResidualPounds,
                        x.AllocatedJuicePounds,
                        x.AllocatedPeelerPounds,
                        x.AllocatedWastePounds,
                        x.AllocationVersion))
                    .ToListAsync(cancellationToken);
                packout.Allocations = roomLotAllocations
                    .GroupBy(
                        x => new
                        {
                            Grower = x.Grower.Trim().ToUpperInvariant(),
                            Lot = x.Lot.Trim().ToUpperInvariant(),
                            x.AllocationVersion
                        })
                    .Select(x => new EstimatedAllocationViewModel(
                        string.Join(", ", x.Select(y => y.Room).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y)),
                        x.First().Grower,
                        x.First().Lot,
                        x.Sum(y => y.Bins),
                        x.Sum(y => y.ContributionPercent),
                        x.Sum(y => y.PackedPounds),
                        x.Sum(y => y.WholeBoxes),
                        x.Sum(y => y.ResidualPounds),
                        x.Sum(y => y.JuicePounds),
                        x.Sum(y => y.PeelerPounds),
                        x.Sum(y => y.WastePounds),
                        x.Key.AllocationVersion))
                    .OrderBy(x => x.Lot, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            run.Packout = packout;
            run.CanUploadPackout = packout is null
                && run.Status == ActualRunStatuses.Active
                && await userAccessService.HasAccessAsync(user, ApplicationAreas.PackoutResults, PageAccessLevel.Create, cancellationToken);
            run.CanEditPackout = await userAccessService.HasAccessAsync(user, ApplicationAreas.PackoutResults, PageAccessLevel.Create, cancellationToken);
            run.CanAdminPackout = await userAccessService.HasAccessAsync(user, ApplicationAreas.PackoutResults, PageAccessLevel.Admin, cancellationToken);
        }
        catch (Exception exception) when (
            DatabaseFailureDiagnostics.Classify(exception).Category == DatabaseFailureCategory.SchemaMismatch)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(exception);
            var referenceId = Guid.NewGuid().ToString("N")[..8];
            logger.LogError(
                exception,
                "Actual Run detail optional schema is unavailable. Reference={ReferenceId} ActualRunId={ActualRunId} ProviderCode={ProviderCode}. Base Actual Run and source contribution were loaded.",
                referenceId,
                id,
                diagnostic.ProviderCode ?? "None");
            run.DetailWarning =
                $"Run Expectation and Packout Result details are temporarily unavailable because the database update required by this release has not been completed. The Actual Run itself is unchanged. Reference {referenceId}.";
            run.OptionalDetailAvailable = false;
            run.CanViewPackout = false;
            run.CanUploadPackout = false;
            run.CanEditPackout = false;
            run.CanAdminPackout = false;
        }
        return run;
    }

    public async Task<BinsRunProjectionViewModel> GetProjectionAsync(BinsRunProjectionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.View, cancellationToken))
        {
            throw new UnauthorizedAccessException("Bins Run View access is required.");
        }

        if (request.RoomId is null)
        {
            throw new InvalidOperationException("Select a room before reviewing lot projections.");
        }

        var snapshots = await GetCurrentInventorySnapshotsAsync(request.WarehouseId, request.RoomId, cancellationToken);
        var currentSnapshots = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var sampleData = await GetLatestSampleDataByLotAsync(currentSnapshots, cancellationToken);
        var selectedKeys = request.InventoryKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lots = currentSnapshots;
        var isSelection = selectedKeys.Count > 0;
        if (isSelection)
        {
            var byKey = currentSnapshots.ToDictionary(x => x.InventoryKey, StringComparer.OrdinalIgnoreCase);
            if (selectedKeys.Any(x => !byKey.ContainsKey(x)))
            {
                throw new InvalidOperationException("Selected inventory is not available in this room.");
            }

            lots = selectedKeys.Select(x => byKey[x]).ToList();
        }

        return BuildProjection(lots, sampleData, isSelection);
    }

    public async Task<IReadOnlyList<RunProjectionInventorySource>> SearchPlanningInventoryAsync(
        string? query,
        int? warehouseId,
        int? roomId,
        int take,
        CancellationToken cancellationToken)
    {
        var normalized = query?.Trim() ?? "";
        var snapshots = (await GetCurrentInventorySnapshotsAsync(warehouseId, roomId, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .Where(x => normalized.Length == 0
                || x.Facility.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Room.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Grower.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Lot.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Variety.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Facility)
            .ThenBy(x => x.Room)
            .ThenBy(x => x.Grower)
            .ThenBy(x => x.Lot)
            .Take(Math.Clamp(take, 1, 100))
            .Select(ToPlanningInventory)
            .ToList();
        return snapshots;
    }

    public async Task<RunProjectionInventorySource?> GetPlanningInventoryAsync(string inventoryKey, CancellationToken cancellationToken)
    {
        var snapshot = await GetCurrentInventoryByKeyAsync(inventoryKey, cancellationToken);
        return snapshot is null || snapshot.CurrentBins <= 0 ? null : ToPlanningInventory(snapshot);
    }

    public async Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken))
        {
            return "Bins Run Edit access is required to record bins run.";
        }

        return await SaveNewBalanceAsync(null, form, user, "Create", cancellationToken);
    }

    public async Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Edit, cancellationToken))
        {
            return "Bins Run Edit access is required to edit bins run.";
        }

        if (id <= 0)
        {
            return "Bins Run entry is required.";
        }

        return await SaveNewBalanceAsync(id, form, user, "Update", cancellationToken);
    }

    public async Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, PageAccessLevel.Admin, cancellationToken))
        {
            return "Bins Run Admin access is required to reverse bins run.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Reason is required to reverse bins run.";
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var entry = await dbContext.BinsRunEntries
            .Include(x => x.InventoryAdjustment)
            .SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
        if (entry is null)
        {
            return "Bins Run entry was not found.";
        }

        if (entry.IsReversed)
        {
            return "Bins Run entry is already reversed.";
        }
        if (entry.IsReconciled)
        {
            return "This Bins Run is locked by finalized packout reconciliation. Reopen the actual run before reversing it.";
        }

        var snapshot = await GetCurrentInventoryByEntryAsync(entry, cancellationToken);
        if (snapshot is null)
        {
            return "Selected inventory is no longer available in this room.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var previous = snapshot.CurrentBins;
        var restored = previous + entry.BinsRun;
        var adjustment = CreateAdjustment(snapshot, entry.BinsRun, previous, restored, ReversalAdjustmentType, userId, DateTimeOffset.UtcNow, $"Reversal of Bins Run #{entry.Id}: {form.Reason.Trim()}");
        adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        adjustment.InventoryOperationKey = $"binsrun:{entry.Id}:reversal";
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);

        var reversal = CopyAsReversal(entry, adjustment, previous, restored, userId, form.Reason.Trim());
        dbContext.BinsRunEntries.Add(reversal);

        entry.IsReversed = true;
        entry.ReversedAt = DateTimeOffset.UtcNow;
        entry.ReversedByUserId = userId;
        entry.ReverseReason = form.Reason.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("Reverse", entry, userId, new { previousAvailableBins = previous }, new { restoredAvailableBins = restored, form.Reason }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (roomTreatmentService is not null)
        {
            var lineage = await roomTreatmentService.ReverseMovementsAsync(
                $"binsrun:{entry.Id}:treatment-reversal",
                TreatmentLineageMovementTypes.BinsRunReversal,
                null,
                null,
                entry.Id,
                DateTimeOffset.UtcNow,
                userId,
                cancellationToken);
            if (!lineage.Success) return lineage.Error;
        }
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    public async Task<string?> CreateActualRunAsync(ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Edit, cancellationToken))
        {
            return "Actual Run Create access is required to record an Actual Run.";
        }

        form.Id = null;
        form.ConcurrencyVersion = 0;
        form.RunProjectionId = null;
        return await SaveActualRunAsync(form, user, null, null, cancellationToken);
    }

    public async Task<string?> UpdateActualRunAsync(long id, ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Edit, cancellationToken))
        {
            return "Actual Run Create access is required to edit an Actual Run.";
        }

        form.Id = id;
        form.RunProjectionId = null;
        return await SaveActualRunAsync(form, user, null, null, cancellationToken);
    }

    public async Task<string?> ApproveActualRunOverrideAsync(
        ApproveActualRunOverrideForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Admin, cancellationToken))
        {
            return "Actual Run Admin access is required to approve an inventory shortage.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "An administrator override reason is required.";
        }

        var approverId = await CurrentUserIdAsync(user, cancellationToken);
        if (approverId is null)
        {
            return "The approving user account could not be resolved.";
        }

        var request = await dbContext.ActualRunOverrideRequests
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == form.RequestId, cancellationToken);
        if (request is null || request.Status != ActualRunOverrideStatuses.Pending)
        {
            return "The override request was not found or is no longer pending.";
        }

        if (request.RequestedByUserId == approverId.Value)
        {
            return "The user who requested an overdraw cannot approve their own override.";
        }

        var actualRunForm = new ActualRunForm
        {
            Id = request.ActualRunId,
            ConcurrencyVersion = request.ExpectedConcurrencyVersion ?? 0,
            OperationKey = request.OperationKey,
            RunProjectionId = null,
            RunAt = request.RunAt,
            Notes = request.Notes,
            RunFacilityWarehouseId = request.RunFacilityWarehouseId,
            Lines = request.Lines.Select(x => new ActualRunLineForm
            {
                InventoryKey = LedgerInventoryKey(x.WarehouseId, x.RoomId, x.CropYear, x.LotNumber, x.VarietyCode, x.FruitProfileId, x.GrowerLotId),
                TreatmentSignature = x.TreatmentSignature ?? "",
                BinsRun = x.RequestedBins,
                ExpectedAvailableBins = x.AvailableBins,
                RunProjectionSourceId = x.RunProjectionSourceId
            }).ToList()
        };

        return await SaveActualRunAsync(actualRunForm, user, request, form.Reason.Trim(), cancellationToken);
    }

    public async Task<string?> CancelActualRunAsync(
        CancelActualRunForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.ActualRuns, PageAccessLevel.Admin, cancellationToken))
        {
            return "Actual Run Admin access is required to cancel an Actual Run.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "A cancellation reason is required.";
        }

        if (string.IsNullOrWhiteSpace(form.OperationKey))
        {
            return "The cancellation request identifier is required.";
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        if (await dbContext.ActualRunRevisions.AsNoTracking().AnyAsync(x => x.OperationKey == form.OperationKey, cancellationToken))
        {
            return null;
        }

        var run = await dbContext.ActualRuns
            .Include(x => x.Revisions)
            .Include(x => x.Entries)
            .SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
        if (run is null)
        {
            return "Actual Run was not found.";
        }

        if (run.Status == ActualRunStatuses.Canceled)
        {
            return "Actual Run is already canceled.";
        }

        if (run.ConcurrencyVersion != form.ConcurrencyVersion)
        {
            var conflictUserId = await CurrentUserIdAsync(user, cancellationToken);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "ConcurrencyConflict",
                EntityName = nameof(ActualRun),
                EntityKey = run.Id.ToString(),
                UserId = conflictUserId,
                BeforeValuesJson = JsonSerializer.Serialize(new { ExpectedVersion = form.ConcurrencyVersion }),
                AfterValuesJson = JsonSerializer.Serialize(new { CurrentVersion = run.ConcurrencyVersion, Operation = "Cancel" }),
                SourceApplication = SourceApplication,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return "The Actual Run changed after this page loaded. Reload before canceling.";
        }

        if (run.Entries.Any(x => !x.IsReversed && x.IsReconciled))
        {
            return "This Actual Run is locked by finalized packout reconciliation. Reopen the reconciliation before canceling it.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        if (userId is null)
        {
            return "The current user account could not be resolved.";
        }

        var revision = new ActualRunRevision
        {
            ActualRun = run,
            RevisionNumber = run.CurrentRevisionNumber + 1,
            OperationType = ActualRunRevisionTypes.Cancel,
            OperationKey = form.OperationKey.Trim(),
            IsCurrent = true,
            Reason = form.Reason.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        foreach (var oldRevision in run.Revisions.Where(x => x.IsCurrent))
        {
            oldRevision.IsCurrent = false;
        }
        dbContext.ActualRunRevisions.Add(revision);
        await dbContext.SaveChangesAsync(cancellationToken);

        var activeEntries = run.Entries
            .Where(x => x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
            .OrderBy(x => x.Id)
            .ToList();
        await ReverseEntriesAsync(run, revision, activeEntries, userId.Value, form.Reason.Trim(), cancellationToken);

        run.Status = ActualRunStatuses.Canceled;
        run.CancellationReason = form.Reason.Trim();
        run.CanceledByUserId = userId;
        run.CanceledAt = DateTimeOffset.UtcNow;
        run.UpdatedByUserId = userId;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        run.CurrentRevisionNumber = revision.RevisionNumber;
        run.ConcurrencyVersion++;
        await AddActualRunAuditAsync("Cancel", run, revision, userId.Value, new
        {
            form.Reason,
            ReversedEntryIds = activeEntries.Select(x => x.Id),
            RestoredBins = activeEntries.Sum(x => x.BinsRun)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    private async Task<string?> SaveActualRunAsync(
        ActualRunForm form,
        ClaimsPrincipal user,
        ActualRunOverrideRequest? approvedOverride,
        string? approvalReason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.OperationKey))
        {
            return "The save request identifier is required.";
        }

        var normalizedLines = form.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.InventoryKey) || x.BinsRun != 0)
            .ToList();
        if (normalizedLines.Count == 0)
        {
            return "Select at least one room-lot row.";
        }

        if (normalizedLines.Any(x => x.BinsRun <= 0))
        {
            return "Bins being pulled must be greater than zero for every selected room-lot row.";
        }

        if (normalizedLines.Select(x => $"{x.InventoryKey.Trim()}|{x.TreatmentSignature.Trim()}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedLines.Count)
        {
            return "Each room-lot combination may appear only once in an Actual Run.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        if (userId is null)
        {
            return "The current user account could not be resolved.";
        }

        var canAdminEitherFacility = await userAccessService.HasAccessAsync(
                user,
                ApplicationAreas.ActualRuns,
                PageAccessLevel.Admin,
                cancellationToken)
            || await userAccessService.HasAccessAsync(
                user,
                ApplicationAreas.BinsRun,
                PageAccessLevel.Admin,
                cancellationToken);

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var duplicateRevision = await dbContext.ActualRunRevisions.AsNoTracking()
            .Where(x => x.OperationKey == form.OperationKey.Trim())
            .Select(x => (long?)x.ActualRunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (duplicateRevision is not null)
        {
            return null;
        }

        ActualRun? run = null;
        List<BinsRunEntry> activeEntries = [];
        if (form.Id is long id)
        {
            run = await dbContext.ActualRuns
                .Include(x => x.Revisions)
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (run is null)
            {
                return "Actual Run was not found.";
            }

            if (run.Status == ActualRunStatuses.Canceled)
            {
                return "A canceled Actual Run cannot be edited.";
            }

            if (run.ConcurrencyVersion != form.ConcurrencyVersion)
            {
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "ConcurrencyConflict",
                    EntityName = nameof(ActualRun),
                    EntityKey = run.Id.ToString(),
                    UserId = userId,
                    BeforeValuesJson = JsonSerializer.Serialize(new { ExpectedVersion = form.ConcurrencyVersion }),
                    AfterValuesJson = JsonSerializer.Serialize(new { CurrentVersion = run.ConcurrencyVersion, Operation = "Edit" }),
                    SourceApplication = SourceApplication,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return "Conflict detected: another user changed this Actual Run. Reload and review the current room balances.";
            }

            activeEntries = run.Entries
                .Where(x => x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
                .OrderBy(x => x.Id)
                .ToList();
            if (activeEntries.Any(x => x.IsReconciled))
            {
                return "This Actual Run is locked by finalized packout reconciliation. Reopen the reconciliation before editing it.";
            }
        }

        var parsed = new List<(ActualRunLineForm Form, int WarehouseId, int RoomId, int? CropYear, string Lot, string Variety, int? FruitProfileId, int? GrowerLotId)>();
        foreach (var line in normalizedLines)
        {
            if (!TryParseLedgerInventoryKey(line.InventoryKey, out var warehouseId, out var roomId, out var cropYear, out var lot, out var variety, out var fruitProfileId, out var growerLotId))
            {
                return "One or more selected inventory rows are not room-ledger inventory.";
            }
            parsed.Add((line, warehouseId, roomId, cropYear, lot, variety, fruitProfileId, growerLotId));
        }

        var warehouseIds = parsed.Select(x => x.WarehouseId).Distinct().ToList();
        if (warehouseIds.Count != 1)
        {
            return "All room-lot rows in one Actual Run must belong to the same facility.";
        }

        var roomIds = parsed.Select(x => x.RoomId).Distinct().ToList();
        var snapshots = await GetCurrentInventorySnapshotsForRoomsAsync(warehouseIds[0], roomIds, null, cancellationToken);
        var resolved = new List<(ActualRunLineForm Form, InventorySnapshot Snapshot, int EffectiveAvailable)>();
        foreach (var line in parsed)
        {
            var candidates = snapshots.Where(x =>
                    x.RoomId == line.RoomId
                    && x.CropYear == line.CropYear
                    && string.Equals(x.Lot, line.Lot, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Variety, line.Variety, StringComparison.OrdinalIgnoreCase)
                    && (line.FruitProfileId is null || x.FruitProfileId == line.FruitProfileId)
                    && (line.GrowerLotId is null || x.GrowerLotId == line.GrowerLotId))
                .ToList();
            if (candidates.Count != 1)
            {
                return $"Room inventory is no longer available for lot {line.Lot} / {line.Variety}.";
            }
            var snapshot = candidates[0];

            var restored = activeEntries
                .Where(x => SameInventory(x, snapshot))
                .Sum(x => x.BinsRun);
            resolved.Add((line.Form, snapshot, snapshot.CurrentBins + restored));
        }

        if (resolved.Select(x => x.Snapshot.CropYear).Distinct().Count() != 1)
        {
            return "All room-lot rows in one Actual Run must have the same crop year.";
        }

        if (resolved.Select(x => x.Snapshot.Variety).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return "All room-lot rows in one Actual Run must have the same variety.";
        }

        if (resolved.Select(x => x.Snapshot.IsOrganic).Distinct().Count() != 1)
        {
            return "All room-lot rows in one Actual Run must have the same Organic/Conventional status.";
        }

        var resolvedCropYear = resolved.Select(x => x.Snapshot.CropYear).Distinct().Single();
        if (resolvedCropYear is null)
        {
            return "Crop year is required before an Actual Run can be recorded.";
        }
        var isAuthoritative = resolvedCropYear.Value >= AuthoritativeStartCropYear;
        if (isAuthoritative && resolved.Any(x => x.Snapshot.FruitProfileId is null
            || string.IsNullOrWhiteSpace(x.Snapshot.Variety)
            || string.IsNullOrWhiteSpace(x.Snapshot.ProductionType)
            || x.Snapshot.IsOrganic is null
            || string.IsNullOrWhiteSpace(x.Snapshot.GrowerNumber)))
        {
            return $"Crop {resolvedCropYear} Actual Runs require canonical variety, production type, Organic/Conventional status, and an authoritative receipt grower number.";
        }

        var facilityResolution = approvedOverride is null
            ? await ResolveRunFacilityAsync(
                userId.Value,
                form.RunFacilityWarehouseId,
                run,
                canAdminEitherFacility,
                form.RunAt,
                isAuthoritative,
                cancellationToken)
            : isAuthoritative
                ? await ResolveApprovedOverrideFacilityAsync(approvedOverride, run, cancellationToken)
                : RunFacilityResolution.NotAuthoritative();
        if (facilityResolution.Error is not null)
        {
            return facilityResolution.Error;
        }

        var shortages = resolved
            .Where(x => x.Form.BinsRun > x.EffectiveAvailable)
            .Select(x => new
            {
                x.Form,
                x.Snapshot,
                Available = x.EffectiveAvailable,
                Shortage = x.Form.BinsRun - x.EffectiveAvailable
            })
            .ToList();
        if (shortages.Count > 0 && approvedOverride is null)
        {
            var existingRequest = await dbContext.ActualRunOverrideRequests.AsNoTracking()
                .Where(x => x.OperationKey == form.OperationKey.Trim())
                .Select(x => (long?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingRequest is not null)
            {
                return $"Inventory shortage override request #{existingRequest.Value} is pending administrator approval.";
            }

            var request = new ActualRunOverrideRequest
            {
                ActualRunId = run?.Id,
                RunProjectionId = null,
                OperationType = run is null ? ActualRunRevisionTypes.Create : ActualRunRevisionTypes.Edit,
                OperationKey = form.OperationKey.Trim(),
                Status = ActualRunOverrideStatuses.Pending,
                ExpectedConcurrencyVersion = run?.ConcurrencyVersion,
                RunAt = form.RunAt,
                Notes = NormalizeOptional(form.Notes),
                RequestedByUserId = userId.Value,
                RequestedAt = DateTimeOffset.UtcNow,
                RunFacilityWarehouseId = facilityResolution.WarehouseId,
                RunFacilityCodeSnapshot = facilityResolution.Code,
                RunFacilityAssignmentSource = facilityResolution.AssignmentSource
            };
            foreach (var item in resolved)
            {
                request.Lines.Add(new ActualRunOverrideRequestLine
                {
                    WarehouseId = item.Snapshot.WarehouseId,
                    RoomId = item.Snapshot.RoomId,
                    CropYear = item.Snapshot.CropYear,
                    GrowerLotId = item.Snapshot.GrowerLotId,
                    FruitProfileId = item.Snapshot.FruitProfileId,
                    GrowerName = item.Snapshot.Grower,
                    LotNumber = item.Snapshot.Lot,
                    PoolStart = item.Snapshot.PoolStart,
                    VarietyCode = item.Snapshot.Variety,
                    InventoryStatus = item.Snapshot.InventoryStatus,
                    AvailableBins = item.EffectiveAvailable,
                    RequestedBins = item.Form.BinsRun,
                    ShortageBins = Math.Max(0, item.Form.BinsRun - item.EffectiveAvailable),
                    RunProjectionSourceId = item.Form.RunProjectionSourceId,
                    TreatmentSignature = item.Form.TreatmentSignature
                });
            }
            dbContext.ActualRunOverrideRequests.Add(request);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "OverdrawAttempt",
                EntityName = nameof(ActualRunOverrideRequest),
                EntityKey = form.OperationKey.Trim(),
                UserId = userId,
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    ActualRunId = run?.Id,
                    Rows = shortages.Select(x => new
                    {
                        x.Snapshot.RoomId,
                        x.Snapshot.Lot,
                        x.Snapshot.Variety,
                        x.Available,
                        Requested = x.Form.BinsRun,
                        x.Shortage
                    })
                }),
                SourceApplication = SourceApplication,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return $"Inventory shortage override request #{request.Id} is pending approval by a different administrator.";
        }

        if (approvedOverride is not null)
        {
            if (approvedOverride.Status != ActualRunOverrideStatuses.Pending)
            {
                return "The override request is no longer pending.";
            }
            if (approvedOverride.RequestedByUserId == userId.Value)
            {
                return "The user who requested an overdraw cannot approve their own override.";
            }
            if (string.IsNullOrWhiteSpace(approvalReason))
            {
                return "An administrator override reason is required.";
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (run is null)
        {
            run = new ActualRun
            {
                RunProjectionId = null,
                Status = ActualRunStatuses.Active,
                CurrentRevisionNumber = 0,
                ConcurrencyVersion = 1,
                RunAt = form.RunAt,
                Notes = NormalizeOptional(form.Notes),
                CreatedByUserId = userId,
                CreatedAt = now,
                RunFacilityWarehouseId = facilityResolution.WarehouseId,
                RunFacilityCodeSnapshot = facilityResolution.Code,
                RunFacilityAssignmentSource = facilityResolution.AssignmentSource,
                RunFacilityAssignedByUserId = userId,
                RunFacilityAssignedAt = now
            };
            dbContext.ActualRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var operationType = activeEntries.Count == 0
            ? ActualRunRevisionTypes.Create
            : ActualRunRevisionTypes.Edit;
        var revision = new ActualRunRevision
        {
            ActualRunId = run.Id,
            RevisionNumber = run.CurrentRevisionNumber + 1,
            OperationType = operationType,
            OperationKey = form.OperationKey.Trim(),
            IsCurrent = true,
            CreatedByUserId = userId,
            CreatedAt = now
        };
        foreach (var oldRevision in run.Revisions.Where(x => x.IsCurrent))
        {
            oldRevision.IsCurrent = false;
        }
        dbContext.ActualRunRevisions.Add(revision);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (activeEntries.Count > 0)
        {
            await ReverseEntriesAsync(run, revision, activeEntries, userId.Value, "Actual Run revision", cancellationToken);
        }

        var resolvedTreatmentSelections = roomTreatmentService is null
            ? null
            : await roomTreatmentService.GetSelectionsAsync(resolved.Select(x => ToLedgerSnapshot(x.Snapshot)).ToList(), cancellationToken);

        var createdEntries = new List<BinsRunEntry>(resolved.Count);
        foreach (var item in resolved)
        {
            var previous = item.EffectiveAvailable;
            var next = previous - item.Form.BinsRun;
            var isOverride = item.Form.BinsRun > previous;
            var adjustment = CreateAdjustment(
                item.Snapshot,
                -item.Form.BinsRun,
                previous,
                next,
                AdjustmentType,
                userId,
                form.RunAt,
                form.Notes);
            adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
            adjustment.InventoryOperationKey = $"actualrun:{revision.OperationKey}:{item.Form.InventoryKey.Trim()}";
            adjustment.ActualRunId = run.Id;
            adjustment.ActualRunRevisionId = revision.Id;
            adjustment.Source = $"Actual Run #{run.Id}";
            adjustment.Reason = operationType;
            dbContext.RoomInventoryAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync(cancellationToken);

            var entry = new BinsRunEntry
            {
                ReceiptId = null,
                SourceInventoryAdjustmentId = item.Snapshot.InventoryAdjustmentId,
                InventoryAdjustmentId = adjustment.Id,
                WarehouseId = item.Snapshot.WarehouseId,
                RoomId = item.Snapshot.RoomId,
                CropYear = item.Snapshot.CropYear,
                GrowerLotId = item.Snapshot.GrowerLotId,
                FruitProfileId = item.Snapshot.FruitProfileId,
                GrowerName = item.Snapshot.Grower,
                LotNumber = item.Snapshot.Lot,
                PoolStart = item.Snapshot.PoolStart,
                VarietyCode = item.Snapshot.Variety,
                InventoryStatus = item.Snapshot.InventoryStatus,
                PreviousAvailableBins = previous,
                BinsRun = item.Form.BinsRun,
                NewAvailableBins = next,
                Notes = NormalizeOptional(form.Notes),
                RunAt = form.RunAt,
                CreatedByUserId = userId,
                CreatedAt = now,
                ActualRunId = run.Id,
                ActualRunRevisionId = revision.Id,
                TransactionType = ActualRunTransactionTypes.Depletion,
                IsOverdrawOverride = isOverride,
                OverrideAvailableBins = isOverride ? previous : null,
                OverrideRequestedBins = isOverride ? item.Form.BinsRun : null,
                OverrideShortageBins = isOverride ? item.Form.BinsRun - previous : null,
                OverrideReason = isOverride ? approvalReason : null,
                OverrideApprovedByUserId = isOverride ? userId : null,
                OverrideApprovedAt = isOverride ? now : null,
                ReportingFacilityWarehouseId = isAuthoritative ? run.RunFacilityWarehouseId : null,
                ReportingFacilityCodeSnapshot = isAuthoritative ? run.RunFacilityCodeSnapshot : null,
                ReportingFacilityAssignmentSource = isAuthoritative ? run.RunFacilityAssignmentSource : null,
                ReportingFacilityAssignedByUserId = isAuthoritative ? userId : null,
                ReportingFacilityAssignedAt = isAuthoritative ? now : null,
                ProductionTypeSnapshot = item.Snapshot.ProductionType,
                IsOrganicSnapshot = item.Snapshot.IsOrganic,
                GrowerNumberSnapshot = item.Snapshot.GrowerNumber,
                ReportingCropYearSnapshot = item.Snapshot.CropYear,
                ReportingFruitProfileIdSnapshot = item.Snapshot.FruitProfileId,
                ReportingVarietyCodeSnapshot = item.Snapshot.Variety
            };
            entry.InventoryAdjustment = adjustment;
            dbContext.BinsRunEntries.Add(entry);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (roomTreatmentService is not null)
            {
                var ledgerSnapshot = ToLedgerSnapshot(item.Snapshot);
                var selections = resolvedTreatmentSelections![RoomTreatmentService.SelectionLookupKey(ledgerSnapshot)];
                var selectedTreatment = string.IsNullOrWhiteSpace(item.Form.TreatmentSignature)
                    ? selections.Count == 1 ? selections[0] : null
                    : selections.SingleOrDefault(x => x.TreatmentSignature == item.Form.TreatmentSignature);
                if (selectedTreatment is null)
                {
                    return "This room-lot has multiple treatment histories. Select the exact segment being packed.";
                }
                entry.TreatmentStateSnapshot = selectedTreatment.TreatmentState;
                entry.TreatmentSignatureSnapshot = selectedTreatment.TreatmentSignature;
                entry.TreatmentSummarySnapshot = selectedTreatment.Label;
                var lineage = await roomTreatmentService.MoveAsync(
                    ledgerSnapshot,
                    selectedTreatment.TreatmentSignature,
                    item.Form.BinsRun,
                    null,
                    null,
                    $"actualrun:{revision.OperationKey}:{entry.Id}:treatment",
                    TreatmentLineageMovementTypes.BinsRun,
                    null,
                    null,
                    entry.Id,
                    form.RunAt,
                    userId,
                    cancellationToken);
                if (!lineage.Success) return lineage.Error;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            createdEntries.Add(entry);
        }

        run.RunAt = form.RunAt;
        run.Notes = NormalizeOptional(form.Notes);
        run.CurrentRevisionNumber = revision.RevisionNumber;
        run.UpdatedByUserId = userId;
        run.UpdatedAt = now;
        if (operationType == ActualRunRevisionTypes.Edit)
        {
            run.ConcurrencyVersion++;
        }

        if (approvedOverride is not null)
        {
            approvedOverride.Status = ActualRunOverrideStatuses.Approved;
            approvedOverride.ApprovedByUserId = userId;
            approvedOverride.ApprovedAt = now;
            approvedOverride.ApprovalReason = approvalReason;
        }

        var expectation = await RunExpectations.CreateFrozenAsync(
            run,
            revision,
            createdEntries,
            userId.Value,
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddActualRunAuditAsync(operationType, run, revision, userId.Value, new
        {
            Lines = resolved.Select(x => new
            {
                x.Snapshot.RoomId,
                x.Snapshot.Lot,
                x.Snapshot.Variety,
                Available = x.EffectiveAvailable,
                Requested = x.Form.BinsRun,
                Result = x.EffectiveAvailable - x.Form.BinsRun
            }),
            OverdrawApproved = approvedOverride is not null,
            OverrideRequestId = approvedOverride?.Id,
            OverrideReason = approvalReason,
            RunExpectationId = expectation.Id,
            RunExpectationVersion = expectation.CalculationVersion,
            RunFacility = facilityResolution.Code,
            RunFacilityAssignmentSource = facilityResolution.AssignmentSource
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    private async Task ReverseEntriesAsync(
        ActualRun run,
        ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> entries,
        int userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var roomIds = entries.Select(x => x.RoomId).Distinct().ToList();
        var warehouseId = entries.Select(x => x.WarehouseId).Distinct().SingleOrDefault();
        var snapshots = await GetCurrentInventorySnapshotsForRoomsAsync(warehouseId, roomIds, null, cancellationToken);
        foreach (var entry in entries)
        {
            if (entry.IsReversed)
            {
                continue;
            }
            if (await dbContext.BinsRunEntries.AsNoTracking().AnyAsync(x => x.ReversesBinsRunEntryId == entry.Id, cancellationToken))
            {
                throw new InvalidOperationException($"Bins Run entry #{entry.Id} has already been reversed.");
            }

            var snapshot = snapshots.Single(x => SameInventory(entry, x));
            var previous = snapshot.CurrentBins;
            var next = previous + entry.BinsRun;
            var adjustment = CreateAdjustment(snapshot, entry.BinsRun, previous, next, ReversalAdjustmentType, userId, DateTimeOffset.UtcNow, reason);
            adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
            adjustment.InventoryOperationKey = $"actualrun:{revision.OperationKey}:reversal:{entry.Id}";
            adjustment.ActualRunId = run.Id;
            adjustment.ActualRunRevisionId = revision.Id;
            adjustment.Source = $"Actual Run #{run.Id} reversal";
            adjustment.Reason = reason;
            dbContext.RoomInventoryAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync(cancellationToken);

            var reversal = new BinsRunEntry
            {
                ReceiptId = null,
                SourceInventoryAdjustmentId = entry.SourceInventoryAdjustmentId,
                InventoryAdjustmentId = adjustment.Id,
                WarehouseId = entry.WarehouseId,
                RoomId = entry.RoomId,
                CropYear = entry.CropYear,
                GrowerLotId = entry.GrowerLotId,
                FruitProfileId = entry.FruitProfileId,
                GrowerName = entry.GrowerName,
                LotNumber = entry.LotNumber,
                PoolStart = entry.PoolStart,
                VarietyCode = entry.VarietyCode,
                InventoryStatus = entry.InventoryStatus,
                PreviousAvailableBins = previous,
                BinsRun = entry.BinsRun,
                NewAvailableBins = next,
                Notes = reason,
                RunAt = DateTimeOffset.UtcNow,
                CreatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                ActualRunId = run.Id,
                ActualRunRevisionId = revision.Id,
                TransactionType = ActualRunTransactionTypes.Reversal,
                ReversesBinsRunEntryId = entry.Id,
                ReportingFacilityWarehouseId = entry.ReportingFacilityWarehouseId,
                ReportingFacilityCodeSnapshot = entry.ReportingFacilityCodeSnapshot,
                ReportingFacilityAssignmentSource = entry.ReportingFacilityAssignmentSource,
                ReportingFacilityAssignedByUserId = entry.ReportingFacilityAssignedByUserId,
                ReportingFacilityAssignedAt = entry.ReportingFacilityAssignedAt,
                ProductionTypeSnapshot = entry.ProductionTypeSnapshot,
                IsOrganicSnapshot = entry.IsOrganicSnapshot,
                GrowerNumberSnapshot = entry.GrowerNumberSnapshot,
                ReportingCropYearSnapshot = entry.ReportingCropYearSnapshot,
                ReportingFruitProfileIdSnapshot = entry.ReportingFruitProfileIdSnapshot,
                ReportingVarietyCodeSnapshot = entry.ReportingVarietyCodeSnapshot
                ,
                TreatmentStateSnapshot = entry.TreatmentStateSnapshot
                ,
                TreatmentSignatureSnapshot = entry.TreatmentSignatureSnapshot
                ,
                TreatmentSummarySnapshot = entry.TreatmentSummarySnapshot
            };
            reversal.InventoryAdjustment = adjustment;
            dbContext.BinsRunEntries.Add(reversal);
            entry.IsReversed = true;
            entry.ReversedAt = DateTimeOffset.UtcNow;
            entry.ReversedByUserId = userId;
            entry.ReverseReason = reason;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (roomTreatmentService is not null)
            {
                var lineage = await roomTreatmentService.ReverseMovementsAsync(
                    $"actualrun:{revision.OperationKey}:{entry.Id}:treatment-reversal",
                    TreatmentLineageMovementTypes.BinsRunReversal,
                    null,
                    null,
                    entry.Id,
                    DateTimeOffset.UtcNow,
                    userId,
                    cancellationToken);
                if (!lineage.Success) throw new InvalidOperationException(lineage.Error);
            }

            snapshots = snapshots
                .Select(x => SameInventory(entry, x) ? x with { CurrentBins = next } : x)
                .ToList();
        }
    }

    private async Task LinkProjectionSourceAsync(
        long? projectionId,
        long? sourceId,
        BinsRunEntry entry,
        int userId,
        CancellationToken cancellationToken)
    {
        if (projectionId is null || sourceId is null)
        {
            return;
        }

        var source = await dbContext.RunProjectionSources
            .Include(x => x.RunProjection)
            .SingleOrDefaultAsync(x => x.Id == sourceId.Value && x.RunProjectionId == projectionId.Value, cancellationToken);
        if (source is null)
        {
            throw new InvalidOperationException("The selected projection source was not found.");
        }
        if (source.SourceType != RunProjectionSourceTypes.Inventory)
        {
            throw new InvalidOperationException("Only an inventory projection source can be linked to an Actual Run.");
        }

        source.ActualBinsRunEntryId = entry.Id;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        source.RunProjection.UpdatedAt = DateTimeOffset.UtcNow;
        source.RunProjection.UpdatedByUserId = userId;
        source.RunProjection.ConcurrencyVersion++;
    }

    private async Task<string?> SaveNewBalanceAsync(long? entryId, BinsRunForm form, ClaimsPrincipal user, string auditAction, CancellationToken cancellationToken)
    {
        if (form.BinsRun <= 0)
        {
            return "Bins run must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(form.InventoryKey))
        {
            return "Select available inventory.";
        }
        if (entryId is null && (form.RunProjectionId is not null || form.RunProjectionSourceId is not null))
        {
            return "Planning Projections cannot be converted into operational inventory deductions. Record an independent Actual Run from exact room-lot balances.";
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        BinsRunEntry? existing = null;
        RunProjection? linkedProjection = null;
        RunProjectionSource? linkedProjectionSource = null;
        if (entryId is long id)
        {
            existing = await dbContext.BinsRunEntries.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (existing is null) return "Bins Run entry was not found.";
            if (existing.IsReversed) return "Reversed Bins Run entries cannot be edited.";
            if (existing.IsReconciled) return "This Bins Run is locked by finalized packout reconciliation. Reopen the actual run before editing it.";
        }
        else if (form.RunProjectionId is not null || form.RunProjectionSourceId is not null)
        {
            if (form.RunProjectionId is null || form.RunProjectionSourceId is null)
            {
                return "Both the projection and projection source are required when recording from a plan.";
            }

            linkedProjection = await dbContext.RunProjections
                .Include(x => x.Sources)
                .SingleOrDefaultAsync(x => x.Id == form.RunProjectionId.Value, cancellationToken);
            linkedProjectionSource = linkedProjection?.Sources.SingleOrDefault(x => x.Id == form.RunProjectionSourceId.Value);
            if (linkedProjection is null || linkedProjectionSource is null)
            {
                return "The selected projection source was not found.";
            }
            if (linkedProjection.IsDeleted)
            {
                return "A deleted projection cannot be converted to an actual run.";
            }
            if (linkedProjection.IsLocked)
            {
                return "This projection is locked by an actual packout reconciliation.";
            }
            if (!RunProjectionStatuses.Editable.Contains(linkedProjection.Status, StringComparer.OrdinalIgnoreCase))
            {
                return $"A {linkedProjection.Status} projection cannot be converted to an actual run.";
            }
            if (linkedProjection.ProjectionMode != RunProjectionModes.Inventory)
            {
                return "A Preharvest projection cannot create an actual Bins Run. Create and map an Inventory projection first.";
            }
            if (linkedProjectionSource.SourceType != RunProjectionSourceTypes.Inventory
                || string.IsNullOrWhiteSpace(linkedProjectionSource.InventoryKey))
            {
                return "A planning-only Field Sample source must be mapped to real inventory before an actual run can be recorded.";
            }
            if (linkedProjectionSource.ActualBinsRunEntryId is not null)
            {
                return "This projection source is already linked to an actual Bins Run.";
            }
            if (!string.Equals(linkedProjectionSource.InventoryKey, form.InventoryKey, StringComparison.OrdinalIgnoreCase))
            {
                return "The actual-run inventory must match the selected projection source.";
            }
        }

        var snapshot = await GetCurrentInventoryByKeyAsync(form.InventoryKey, cancellationToken);
        if (snapshot is null)
        {
            return "Selected inventory is no longer available in this room.";
        }

        if (form.RoomId is not null && snapshot.RoomId != form.RoomId)
        {
            return "Selected inventory does not belong to the selected room.";
        }

        if (form.WarehouseId is not null && snapshot.WarehouseId != form.WarehouseId)
        {
            return "Selected inventory does not belong to the selected facility.";
        }
        if (snapshot.CropYear is null)
        {
            return "Crop year is required before Bins Run inventory can be recorded.";
        }
        var isAuthoritative = snapshot.CropYear.Value >= AuthoritativeStartCropYear;
        if (isAuthoritative && (snapshot.FruitProfileId is null
            || string.IsNullOrWhiteSpace(snapshot.Variety)
            || string.IsNullOrWhiteSpace(snapshot.ProductionType)
            || snapshot.IsOrganic is null
            || string.IsNullOrWhiteSpace(snapshot.GrowerNumber)))
        {
            return $"Crop {snapshot.CropYear} Bins Run entries require canonical variety, production type, Organic/Conventional status, and an authoritative receipt grower number.";
        }
        if (linkedProjection is not null
            && (linkedProjection.FacilityWarehouseId is null
                || linkedProjection.FacilityWarehouseId != snapshot.WarehouseId))
        {
            return "The actual-run inventory must belong to the projection's assigned WP or EBS facility.";
        }

        var effectiveAvailable = snapshot.CurrentBins + (existing is null ? 0 : existing.BinsRun);
        if (existing is null && form.ExpectedAvailableBins > 0 && snapshot.CurrentBins != form.ExpectedAvailableBins)
        {
            return $"Available quantity changed before save. {snapshot.CurrentBins} bins are available now.";
        }

        if (form.BinsRun > effectiveAvailable)
        {
            return $"Cannot run {form.BinsRun} bins because only {effectiveAvailable} bins are currently available.";
        }

        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var legacyFacility = await ResolveLegacyReportingFacilityAsync(
            userId,
            user,
            existing,
            form.RunAt,
            isAuthoritative,
            cancellationToken);
        if (legacyFacility.Error is not null)
        {
            return legacyFacility.Error;
        }
        var newAvailable = effectiveAvailable - form.BinsRun;
        var operationKey = string.IsNullOrWhiteSpace(form.OperationKey)
            ? Guid.NewGuid().ToString("N")
            : form.OperationKey.Trim();
        object? before = existing is null ? null : EntrySnapshot(existing);
        if (existing is not null)
        {
            var reversalAdjustment = CreateAdjustment(
                snapshot,
                existing.BinsRun,
                snapshot.CurrentBins,
                effectiveAvailable,
                ReversalAdjustmentType,
                userId,
                DateTimeOffset.UtcNow,
                $"Revision of Bins Run #{existing.Id}");
            reversalAdjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
            reversalAdjustment.InventoryOperationKey = $"binsrun:{operationKey}:reversal:{existing.Id}";
            dbContext.RoomInventoryAdjustments.Add(reversalAdjustment);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.BinsRunEntries.Add(CopyAsReversal(
                existing,
                reversalAdjustment,
                snapshot.CurrentBins,
                effectiveAvailable,
                userId,
                "Bins Run revision"));
            existing.IsReversed = true;
            existing.ReversedAt = DateTimeOffset.UtcNow;
            existing.ReversedByUserId = userId;
            existing.ReverseReason = "Replaced by a corrected Bins Run revision.";
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            if (roomTreatmentService is not null)
            {
                var restoreLineage = await roomTreatmentService.ReverseMovementsAsync(
                    $"binsrun:{operationKey}:{existing.Id}:treatment-reversal",
                    TreatmentLineageMovementTypes.BinsRunReversal,
                    null,
                    null,
                    existing.Id,
                    DateTimeOffset.UtcNow,
                    userId,
                    cancellationToken);
                if (!restoreLineage.Success) return restoreLineage.Error;
            }
        }

        var adjustment = CreateAdjustment(snapshot, -form.BinsRun, effectiveAvailable, newAvailable, AdjustmentType, userId, form.RunAt, form.Notes);
        adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        adjustment.InventoryOperationKey = $"binsrun:{operationKey}:depletion";
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);

        var entry = new BinsRunEntry
        {
            ReceiptId = snapshot.ReceiptId,
            SourceInventoryAdjustmentId = snapshot.InventoryAdjustmentId,
            InventoryAdjustmentId = adjustment.Id,
            InventoryAdjustment = adjustment,
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            CropYear = snapshot.CropYear,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            GrowerName = snapshot.Grower,
            LotNumber = snapshot.Lot,
            PoolStart = snapshot.PoolStart,
            VarietyCode = snapshot.Variety,
            InventoryStatus = snapshot.InventoryStatus,
            PreviousAvailableBins = effectiveAvailable,
            BinsRun = form.BinsRun,
            NewAvailableBins = newAvailable,
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            RunAt = form.RunAt,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            TransactionType = ActualRunTransactionTypes.Legacy,
            ReportingFacilityWarehouseId = legacyFacility.WarehouseId,
            ReportingFacilityCodeSnapshot = legacyFacility.Code,
            ReportingFacilityAssignmentSource = legacyFacility.AssignmentSource,
            ReportingFacilityAssignedByUserId = legacyFacility.WarehouseId is null ? null : userId,
            ReportingFacilityAssignedAt = legacyFacility.WarehouseId is null ? null : DateTimeOffset.UtcNow,
            ProductionTypeSnapshot = snapshot.ProductionType,
            IsOrganicSnapshot = snapshot.IsOrganic,
            GrowerNumberSnapshot = snapshot.GrowerNumber,
            ReportingCropYearSnapshot = snapshot.CropYear,
            ReportingFruitProfileIdSnapshot = snapshot.FruitProfileId,
            ReportingVarietyCodeSnapshot = snapshot.Variety
        };
        dbContext.BinsRunEntries.Add(entry);

        await dbContext.SaveChangesAsync(cancellationToken);
        if (roomTreatmentService is not null)
        {
            var selections = await roomTreatmentService.GetSelectionsAsync(ToLedgerSnapshot(snapshot), cancellationToken);
            var selectedTreatment = string.IsNullOrWhiteSpace(form.TreatmentSignature)
                ? selections.Count == 1 ? selections[0] : null
                : selections.SingleOrDefault(x => x.TreatmentSignature == form.TreatmentSignature);
            if (selectedTreatment is null) return "This room-lot has multiple treatment histories. Select the exact segment being packed.";
            entry.TreatmentStateSnapshot = selectedTreatment.TreatmentState;
            entry.TreatmentSignatureSnapshot = selectedTreatment.TreatmentSignature;
            entry.TreatmentSummarySnapshot = selectedTreatment.Label;
            var lineage = await roomTreatmentService.MoveAsync(
                ToLedgerSnapshot(snapshot), selectedTreatment.TreatmentSignature, form.BinsRun,
                null, null, $"binsrun:{operationKey}:{entry.Id}:treatment",
                TreatmentLineageMovementTypes.BinsRun, null, null, entry.Id, form.RunAt, userId, cancellationToken);
            if (!lineage.Success) return lineage.Error;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (linkedProjection is not null && linkedProjectionSource is not null)
        {
            var previousStatus = linkedProjection.Status;
            var previousActualBinsRunEntryId = linkedProjectionSource.ActualBinsRunEntryId;
            linkedProjectionSource.ActualBinsRunEntryId = entry.Id;
            linkedProjectionSource.UpdatedAt = DateTimeOffset.UtcNow;
            if (linkedProjection.Sources.All(x => x.SourceType == RunProjectionSourceTypes.Inventory && x.ActualBinsRunEntryId is not null))
            {
                linkedProjection.Status = RunProjectionStatuses.Converted;
                linkedProjection.IsLocked = true;
                linkedProjection.LockedAt = DateTimeOffset.UtcNow;
                linkedProjection.LockedByUserId = userId;
            }
            linkedProjection.UpdatedAt = DateTimeOffset.UtcNow;
            linkedProjection.ConcurrencyVersion++;
            linkedProjection.UpdatedByUserId = userId;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "ConvertSourceToActualRun",
                EntityName = nameof(RunProjection),
                EntityKey = linkedProjection.Id.ToString(),
                UserId = userId,
                BeforeValuesJson = JsonSerializer.Serialize(new { Status = previousStatus, ActualBinsRunEntryId = previousActualBinsRunEntryId }),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    linkedProjection.Status,
                    ActualBinsRunEntryId = entry.Id,
                    linkedProjection.IsLocked,
                    linkedProjection.LockedAt
                }),
                SourceApplication = SourceApplication,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await AddAuditAsync(auditAction, entry, userId, before, EntrySnapshot(entry), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<BinsRunInventoryOptionViewModel>> BuildAvailableInventoryOptionsAsync(
        IReadOnlyList<InventorySnapshot> snapshots,
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData,
        CancellationToken cancellationToken)
    {
        var options = new List<BinsRunInventoryOptionViewModel>();
        var ledgerSnapshots = snapshots.Select(ToLedgerSnapshot).ToList();
        var treatmentSelections = roomTreatmentService is null
            ? null
            : await roomTreatmentService.GetSelectionsAsync(ledgerSnapshots, cancellationToken);
        foreach (var x in snapshots
            .OrderBy(x => x.Facility)
            .ThenBy(x => x.Room)
            .ThenBy(x => x.Grower)
            .ThenBy(x => x.Variety)
            .ThenBy(x => x.Lot))
        {
            sampleData.TryGetValue(QcIdentityKey(x), out var distribution);
            var ledgerSnapshot = ToLedgerSnapshot(x);
            var segments = roomTreatmentService is null
                ? [new TreatmentSegmentSelection(RoomTreatmentService.IdentityKey(ledgerSnapshot), "", TreatmentLineageStates.Untreated, x.CurrentBins, "Untreated")]
                : treatmentSelections![RoomTreatmentService.SelectionLookupKey(ledgerSnapshot)];
            foreach (var segment in segments)
            {
                options.Add(new BinsRunInventoryOptionViewModel(
                x.InventoryKey,
                x.ReceiptId,
                x.InventoryAdjustmentId,
                x.WarehouseId,
                x.RoomId,
                $"{x.Grower} - {x.Variety} - {x.Lot} - {segment.Label} - {segment.CurrentBins} bins available",
                x.Grower,
                x.Lot,
                x.Variety,
                $"{x.Facility} / {x.Room}",
                    segment.CurrentBins,
                    distribution is null || distribution.GradePercentages.Count == 0 ? "No grade data" : FormatGradeSummary(distribution.GradePercentages),
                    x.ReceiptDate,
                    x.FruitProfileId,
                    x.FruitType,
                    x.CanonicalOrchardBlockId,
                    x.CropYear,
                    x.ProductionType,
                    x.Facility,
                    x.Room,
                    x.GrowerLotId,
                    x.ReceiptReference ?? $"Ledger adjustment #{x.InventoryAdjustmentId}",
                    segment.TreatmentSignature,
                    segment.Label));
            }
        }
        return options;
    }

    private async Task<BinsRunRoomSummaryViewModel?> BuildRoomSummaryAsync(
        int roomId,
        IReadOnlyList<InventorySnapshot> currentSnapshots,
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData,
        CancellationToken cancellationToken)
    {
        var room = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == roomId, cancellationToken);
        if (room is null)
        {
            return null;
        }

        var roomLots = currentSnapshots.Where(x => x.RoomId == roomId && x.CurrentBins > 0).ToList();
        var projection = BuildProjection(roomLots, sampleData, isSelection: false);
        return new BinsRunRoomSummaryViewModel
        {
            WarehouseId = room.WarehouseId,
            RoomId = room.Id,
            Facility = room.Warehouse.Code,
            Location = string.IsNullOrWhiteSpace(room.SubLocation) ? room.Warehouse.Name : room.SubLocation!,
            RoomName = room.CropQcRoomName ?? room.DisplayName ?? room.Code,
            TotalAvailableBins = roomLots.Sum(x => x.CurrentBins),
            ActiveLotCount = roomLots.Count,
            SizeDistribution = projection.SizeDistribution,
            GradeSummary = projection.GradeSummary,
            SizeDataLotCount = projection.SizeDataLotCount,
            GradeDataLotCount = projection.GradeDataLotCount,
            Projection = projection
        };
    }

    private async Task<IReadOnlyDictionary<string, LotSampleDistribution>> GetLatestSampleDataByLotAsync(IReadOnlyList<InventorySnapshot> currentSnapshots, CancellationToken cancellationToken)
    {
        var targets = currentSnapshots
            .Select(QcIdentity)
            .Where(x => x is not null)
            .Select(x => x!)
            .DistinctBy(x => x.LookupKey)
            .ToList();
        if (targets.Count == 0)
        {
            return new Dictionary<string, LotSampleDistribution>(StringComparer.OrdinalIgnoreCase);
        }

        var candidateQuery = CanonicalQcFruitIdentity.FilterReceiptSamples(
                dbContext.QcSamples.AsNoTracking(),
                targets);
        var samples = await CanonicalQcFruitIdentity.OrderCandidates(
                candidateQuery,
                dbContext.Database.ProviderName)
            .Include(x => x.Receipt)
                .ThenInclude(x => x.FruitProfile)
            .Include(x => x.FruitReadings)
                .ThenInclude(x => x.Grade)
            .Take(CanonicalQcFruitIdentity.CandidateLimit(targets.Count))
            .ToListAsync(cancellationToken);
        var sampleIdentities = samples.ToDictionary(
            x => x.Id,
            x => CanonicalQcFruitIdentity.FromReceipt(x.Receipt!));

        var result = new Dictionary<string, LotSampleDistribution>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var latest = CanonicalQcFruitIdentity.ResolveLatestUnambiguous(
                target,
                samples,
                sample => sampleIdentities[sample.Id],
                sample => sample.SampleTakenAt,
                sample => sample.Id);
            if (latest is not null)
            {
                result[target.LookupKey] = BuildLotSampleDistribution(latest);
            }
        }

        return result;
    }

    private static LotSampleDistribution BuildLotSampleDistribution(QcSample sample)
    {
        var gradeCounts = sample.FruitReadings
            .Where(x => x.Grade is not null)
            .GroupBy(x => x.Grade!.Code)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        return new LotSampleDistribution(
            ProjectionDistributionMath.BuildSizePercentages(sample.FruitReadings),
            Percentages(gradeCounts),
            sample.SampleTakenAt);
    }

    private static IReadOnlyDictionary<string, decimal> Percentages(IReadOnlyDictionary<string, int> counts)
    {
        var total = counts.Values.Sum();
        return total == 0
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : counts.ToDictionary(x => x.Key, x => x.Value / (decimal)total, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BinsRunSizeDistributionPoint> BuildWeightedSizeDistribution(
        IReadOnlyList<InventorySnapshot> roomLots,
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData)
    {
        var sizeData = sampleData.ToDictionary(x => x.Key, x => x.Value.SizeDistribution, StringComparer.OrdinalIgnoreCase);
        return ProjectionDistributionMath.CombineWeightedSizePercentages(
            roomLots,
            sizeData,
            QcIdentityKey,
            lot => lot.CurrentBins);
    }

    private static BinsRunProjectionViewModel BuildProjection(
        IReadOnlyList<InventorySnapshot> lots,
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData,
        bool isSelection)
    {
        var availableBins = lots.Sum(x => x.CurrentBins);
        var sizeRepresentedBins = lots
            .Where(x => sampleData.TryGetValue(QcIdentityKey(x), out var data) && data.SizeDistribution.Percentages.Count > 0)
            .Sum(x => x.CurrentBins);
        var gradeRepresentedBins = lots
            .Where(x => sampleData.TryGetValue(QcIdentityKey(x), out var data) && data.GradePercentages.Count > 0)
            .Sum(x => x.CurrentBins);

        return new BinsRunProjectionViewModel
        {
            IsSelection = isSelection,
            Label = isSelection
                ? $"Projected mix for {lots.Count} selected lot{(lots.Count == 1 ? "" : "s")}"
                : "Room summary",
            LotCount = lots.Count,
            AvailableBins = availableBins,
            SizeDistribution = BuildWeightedSizeDistribution(lots, sampleData),
            GradeSummary = BuildWeightedGradeSummary(lots, sampleData),
            SizeDataLotCount = lots.Count(x => sampleData.TryGetValue(QcIdentityKey(x), out var data) && data.SizeDistribution.Percentages.Count > 0),
            GradeDataLotCount = lots.Count(x => sampleData.TryGetValue(QcIdentityKey(x), out var data) && data.GradePercentages.Count > 0),
            SizeRepresentedBins = sizeRepresentedBins,
            SizeMissingBins = Math.Max(0, availableBins - sizeRepresentedBins),
            SizeCoveragePercent = availableBins <= 0 ? 0m : decimal.Round(sizeRepresentedBins / (decimal)availableBins * 100m, 1),
            SizeUnclassifiedPercent = ProjectionDistributionMath.CombineWeightedUnclassifiedPercent(
                lots,
                sampleData.ToDictionary(x => x.Key, x => x.Value.SizeDistribution, StringComparer.OrdinalIgnoreCase),
                QcIdentityKey,
                lot => lot.CurrentBins),
            GradeRepresentedBins = gradeRepresentedBins,
            GradeMissingBins = Math.Max(0, availableBins - gradeRepresentedBins)
        };
    }

    private static IReadOnlyList<BinsRunGradeSummaryPoint> BuildWeightedGradeSummary(
        IReadOnlyList<InventorySnapshot> roomLots,
        IReadOnlyDictionary<string, LotSampleDistribution> sampleData)
    {
        var estimatedBinsByGrade = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var lot in roomLots)
        {
            if (!sampleData.TryGetValue(QcIdentityKey(lot), out var data))
            {
                continue;
            }

            foreach (var grade in data.GradePercentages)
            {
                estimatedBinsByGrade[grade.Key] = estimatedBinsByGrade.GetValueOrDefault(grade.Key) + lot.CurrentBins * grade.Value;
            }
        }

        return estimatedBinsByGrade
            .Select(x => new BinsRunGradeSummaryPoint(x.Key, x.Value))
            .OrderByDescending(x => x.EstimatedBins)
            .ThenBy(x => x.Grade)
            .ToList();
    }

    private static string FormatGradeSummary(IReadOnlyDictionary<string, decimal> gradePercentages) =>
        string.Join(", ", gradePercentages
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(3)
            .Select(x => $"{x.Key} {x.Value:P0}"));

    private async Task<InventorySnapshot?> GetCurrentInventoryByKeyAsync(string inventoryKey, CancellationToken cancellationToken)
    {
        if (!TryParseLedgerInventoryKey(inventoryKey, out var warehouseId, out var roomId, out var cropYear, out var lot, out var variety, out var fruitProfileId, out var growerLotId))
        {
            return null;
        }

        return (await GetCurrentInventorySnapshotsForRoomsAsync(warehouseId, [roomId], fruitProfileId, cancellationToken))
            .SingleOrDefault(x => x.CropYear == cropYear
                && string.Equals(x.Lot, lot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Variety, variety, StringComparison.OrdinalIgnoreCase)
                && (fruitProfileId is null || x.FruitProfileId == fruitProfileId)
                && (growerLotId is null || x.GrowerLotId == growerLotId));
    }

    private async Task<InventorySnapshot?> GetCurrentInventoryByEntryAsync(BinsRunEntry entry, CancellationToken cancellationToken)
    {
        var snapshots = await GetCurrentInventorySnapshotsAsync(entry.WarehouseId, entry.RoomId, cancellationToken);
        return snapshots.SingleOrDefault(x =>
            x.CropYear == entry.CropYear
            && (entry.GrowerLotId is null || x.GrowerLotId == entry.GrowerLotId)
            && (entry.FruitProfileId is null || x.FruitProfileId == entry.FruitProfileId)
            && string.Equals(CurrentStorageLotKey(x.RoomId, x.Lot, x.Variety), CurrentStorageLotKey(entry.RoomId, entry.LotNumber, entry.VarietyCode ?? ""), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<InventorySnapshot>> GetCurrentInventorySnapshotsAsync(int? warehouseId, int? roomId, CancellationToken cancellationToken)
        => await GetCurrentInventorySnapshotsForRoomsAsync(
            warehouseId,
            roomId is null ? null : [roomId.Value],
            null,
            cancellationToken);

    private async Task<IReadOnlyList<InventorySnapshot>> GetCurrentInventorySnapshotsForRoomsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? roomIds,
        int? fruitProfileId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var ledgerSnapshots = await RoomInventoryLedger.GetSnapshotsAsync(warehouseId, roomIds, fruitProfileId, cancellationToken);
        var result = ledgerSnapshots.Select(x =>
            new InventorySnapshot(
                LedgerInventoryKey(x.WarehouseId, x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId, x.GrowerLotId),
                null,
                x.SourceReference,
                x.LatestAdjustmentId,
                x.WarehouseId,
                x.RoomId,
                x.Facility,
                x.Room,
                x.CropYear,
                x.GrowerLotId,
                x.FruitProfileId,
                x.Grower,
                x.GrowerNumber,
                x.Lot,
                x.PoolStart,
                x.Variety,
                x.FruitType,
                x.ProductionType,
                x.IsOrganic,
                null,
                x.InventoryStatus,
                x.CurrentBins,
                x.LastTransactionAt))
            .ToList();

        stopwatch.Stop();
        logger.LogInformation(
            "Actual Run room inventory loaded from ledger. QueryCount={QueryCount} RowCount={RowCount} WarehouseId={WarehouseId} RoomCount={RoomCount} ElapsedMs={ElapsedMs}",
            2,
            result.Count,
            warehouseId,
            roomIds?.Count ?? 0,
            stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<IReadOnlyList<ActualRunHistoryItemViewModel>> GetActualRunHistoryAsync(
        BinsRunFilterForm filter,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.ActualRuns.AsNoTracking()
            .Where(x => filter.WarehouseId == null || x.Entries.Any(y => y.WarehouseId == filter.WarehouseId))
            .Where(x => filter.RoomId == null || x.Entries.Any(y => y.RoomId == filter.RoomId))
            .OrderByDescending(x => x.RunAt)
            .ThenByDescending(x => x.Id)
            .Take(50)
            .Select(x => new ActualRunHistoryItemViewModel
            {
                Id = x.Id,
                RunProjectionId = x.RunProjectionId,
                Status = x.Status,
                RevisionNumber = x.CurrentRevisionNumber,
                ConcurrencyVersion = x.ConcurrencyVersion,
                RunAt = x.RunAt,
                Notes = x.Notes,
                CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                RunFacilityWarehouseId = x.RunFacilityWarehouseId,
                RunFacility = x.RunFacilityCodeSnapshot ?? (x.RunFacilityWarehouse == null ? "Unresolved" : x.RunFacilityWarehouse.Code),
                CreatedAt = x.CreatedAt,
                CanceledAt = x.CanceledAt,
                CancellationReason = x.CancellationReason
            })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
        {
            return runs;
        }

        var runIds = runs.Select(x => x.Id).ToList();
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
        var lineRows = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ActualRunId != null && runIds.Contains(x.ActualRunId.Value))
            .OrderBy(x => x.ActualRunRevisionId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                RunId = x.ActualRunId!.Value,
                x.Id,
                x.WarehouseId,
                x.RoomId,
                x.CropYear,
                x.GrowerLotId,
                x.FruitProfileId,
                x.TransactionType,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                Grower = x.GrowerName,
                GrowerNumber = x.GrowerNumberSnapshot ?? x.LotNumber,
                Lot = x.LotNumber,
                Variety = x.VarietyCode ?? "",
                x.PreviousAvailableBins,
                x.BinsRun,
                x.NewAvailableBins,
                x.IsReversed,
                x.IsOverdrawOverride,
                x.OverrideReason,
                x.TreatmentStateSnapshot,
                x.TreatmentSignatureSnapshot,
                x.TreatmentSummarySnapshot
            })
            .ToListAsync(cancellationToken);
        var lines = lineRows.Select(x => new
        {
            x.RunId,
            Line = new ActualRunHistoryLineViewModel
            {
                Id = x.Id,
                InventoryKey = LedgerInventoryKey(x.WarehouseId, x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId, x.GrowerLotId),
                RoomId = x.RoomId,
                CropYear = x.CropYear,
                GrowerLotId = x.GrowerLotId,
                FruitProfileId = x.FruitProfileId,
                TransactionType = x.TransactionType,
                Room = x.Room,
                Grower = growerResolver.DisplayName(x.Grower, x.GrowerNumber),
                GrowerNumber = x.GrowerNumber,
                Lot = x.Lot,
                Variety = x.Variety,
                PreviousAvailableBins = x.PreviousAvailableBins,
                BinsRun = x.BinsRun,
                NewAvailableBins = x.NewAvailableBins,
                IsReversed = x.IsReversed,
                IsOverdrawOverride = x.IsOverdrawOverride,
                OverrideReason = x.OverrideReason,
                TreatmentState = x.TreatmentStateSnapshot ?? TreatmentLineageStates.Untreated,
                TreatmentSignature = x.TreatmentSignatureSnapshot ?? "",
                TreatmentSummary = x.TreatmentSummarySnapshot ?? "No recorded treatment history"
            }
        }).ToList();
        var byRun = lines.GroupBy(x => x.RunId).ToDictionary(x => x.Key, x => (IReadOnlyList<ActualRunHistoryLineViewModel>)x.Select(y => y.Line).ToList());
        foreach (var run in runs)
        {
            run.Lines = byRun.GetValueOrDefault(run.Id) ?? [];
        }
        return runs;
    }

    private async Task<IReadOnlyList<ActualRunOverrideRequestViewModel>> GetPendingOverrideRequestsAsync(
        BinsRunFilterForm filter,
        CancellationToken cancellationToken)
    {
        var requests = await dbContext.ActualRunOverrideRequests.AsNoTracking()
            .Where(x => x.Status == ActualRunOverrideStatuses.Pending)
            .Where(x => filter.WarehouseId == null || x.Lines.Any(y => y.WarehouseId == filter.WarehouseId))
            .OrderBy(x => x.RequestedAt)
            .Take(50)
            .Select(x => new ActualRunOverrideRequestViewModel
            {
                Id = x.Id,
                ActualRunId = x.ActualRunId,
                OperationType = x.OperationType,
                RequestedBy = x.RequestedByUser.DisplayName,
                RequestedAt = x.RequestedAt,
                Lines = x.Lines.OrderBy(y => y.RoomId).ThenBy(y => y.LotNumber).Select(y => new ActualRunOverrideLineViewModel
                {
                    Room = y.Room.CropQcRoomName ?? y.Room.DisplayName ?? y.Room.Code,
                    Lot = y.LotNumber,
                    Variety = y.VarietyCode,
                    AvailableBins = y.AvailableBins,
                    RequestedBins = y.RequestedBins,
                    ShortageBins = y.ShortageBins
                }).ToList()
            })
            .ToListAsync(cancellationToken);
        return requests;
    }

    private Task AddActualRunAuditAsync(string action, ActualRun run, ActualRunRevision revision, int userId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(ActualRun),
            EntityKey = run.Id.ToString(),
            UserId = userId,
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                run.Id,
                RevisionId = revision.Id,
                revision.RevisionNumber,
                revision.OperationType,
                run.Status,
                run.RunAt,
                Details = details
            }),
            SourceApplication = SourceApplication,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }

    private static bool SameInventory(BinsRunEntry entry, InventorySnapshot snapshot) =>
        entry.WarehouseId == snapshot.WarehouseId
        && entry.RoomId == snapshot.RoomId
        && entry.CropYear == snapshot.CropYear
        && (entry.FruitProfileId is null || entry.FruitProfileId == snapshot.FruitProfileId)
        && string.Equals(entry.LotNumber, snapshot.Lot, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.VarietyCode, snapshot.Variety, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string LedgerInventoryKey(
        int warehouseId,
        int roomId,
        int? cropYear,
        string lot,
        string variety,
        int? fruitProfileId,
        int? growerLotId) =>
        $"L:{warehouseId}:{roomId}:{cropYear?.ToString() ?? "-"}:{Uri.EscapeDataString(lot.Trim())}:{Uri.EscapeDataString(variety.Trim())}:{fruitProfileId?.ToString() ?? "-"}:{growerLotId?.ToString() ?? "-"}";

    private static bool TryParseLedgerInventoryKey(
        string value,
        out int warehouseId,
        out int roomId,
        out int? cropYear,
        out string lot,
        out string variety,
        out int? fruitProfileId,
        out int? growerLotId)
    {
        warehouseId = 0;
        roomId = 0;
        cropYear = null;
        lot = "";
        variety = "";
        fruitProfileId = null;
        growerLotId = null;
        var parts = value.Split(':');
        if (parts.Length is not (6 or 7 or 8)
            || !parts[0].Equals("L", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1], out warehouseId)
            || !int.TryParse(parts[2], out roomId))
        {
            return false;
        }
        var parsedCropYear = 0;
        if (parts[3] != "-" && !int.TryParse(parts[3], out parsedCropYear))
        {
            return false;
        }
        if (parts[3] != "-")
        {
            cropYear = parsedCropYear;
        }
        lot = Uri.UnescapeDataString(parts[4]).Trim();
        variety = Uri.UnescapeDataString(parts[5]).Trim();
        if (parts.Length >= 7 && parts[6] != "-")
        {
            if (!int.TryParse(parts[6], out var parsedFruitProfileId))
            {
                return false;
            }
            fruitProfileId = parsedFruitProfileId;
        }
        if (parts.Length == 8 && parts[7] != "-")
        {
            if (!int.TryParse(parts[7], out var parsedGrowerLotId))
            {
                return false;
            }
            growerLotId = parsedGrowerLotId;
        }
        return warehouseId > 0 && roomId > 0 && lot.Length > 0 && variety.Length > 0;
    }

    private static RoomInventoryAdjustment CreateAdjustment(InventorySnapshot snapshot, int changeAmount, int previous, int next, string adjustmentType, int? userId, DateTimeOffset adjustmentAt, string? notes) =>
        new()
        {
            ReceiptId = null,
            CropYear = snapshot.CropYear,
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            GrowerName = snapshot.Grower,
            LotNumber = snapshot.Lot,
            PoolStart = snapshot.PoolStart,
            VarietyCode = snapshot.Variety,
            OldBinCount = previous,
            ChangeAmount = changeAmount,
            NewBinCount = next,
            AdjustmentType = adjustmentType,
            Source = "Bins Run",
            InventoryStatus = string.IsNullOrWhiteSpace(snapshot.InventoryStatus) ? null : snapshot.InventoryStatus,
            Reason = adjustmentType,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AdjustmentAt = adjustmentAt,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static BinsRunEntry CopyAsReversal(
        BinsRunEntry original,
        RoomInventoryAdjustment adjustment,
        int previous,
        int next,
        int? userId,
        string reason) =>
        new()
        {
            ReceiptId = original.ReceiptId,
            SourceInventoryAdjustmentId = original.SourceInventoryAdjustmentId,
            InventoryAdjustmentId = adjustment.Id,
            InventoryAdjustment = adjustment,
            WarehouseId = original.WarehouseId,
            RoomId = original.RoomId,
            CropYear = original.CropYear,
            GrowerLotId = original.GrowerLotId,
            FruitProfileId = original.FruitProfileId,
            GrowerName = original.GrowerName,
            LotNumber = original.LotNumber,
            PoolStart = original.PoolStart,
            VarietyCode = original.VarietyCode,
            InventoryStatus = original.InventoryStatus,
            PreviousAvailableBins = previous,
            BinsRun = original.BinsRun,
            NewAvailableBins = next,
            Notes = reason,
            RunAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            ActualRunId = original.ActualRunId,
            ActualRunRevisionId = original.ActualRunRevisionId,
            TransactionType = ActualRunTransactionTypes.Reversal,
            ReversesBinsRunEntryId = original.Id,
            ReportingFacilityWarehouseId = original.ReportingFacilityWarehouseId,
            ReportingFacilityCodeSnapshot = original.ReportingFacilityCodeSnapshot,
            ReportingFacilityAssignmentSource = original.ReportingFacilityAssignmentSource,
            ReportingFacilityAssignedByUserId = original.ReportingFacilityAssignedByUserId,
            ReportingFacilityAssignedAt = original.ReportingFacilityAssignedAt,
            ProductionTypeSnapshot = original.ProductionTypeSnapshot,
            IsOrganicSnapshot = original.IsOrganicSnapshot,
            GrowerNumberSnapshot = original.GrowerNumberSnapshot,
            ReportingCropYearSnapshot = original.ReportingCropYearSnapshot,
            ReportingFruitProfileIdSnapshot = original.ReportingFruitProfileIdSnapshot,
            ReportingVarietyCodeSnapshot = original.ReportingVarietyCodeSnapshot
            ,
            TreatmentStateSnapshot = original.TreatmentStateSnapshot
            ,
            TreatmentSignatureSnapshot = original.TreatmentSignatureSnapshot
            ,
            TreatmentSummarySnapshot = original.TreatmentSummarySnapshot
        };

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        return provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private async Task<RunFacilityResolution> ResolveRunFacilityAsync(
        int userId,
        int? requestedWarehouseId,
        ActualRun? existingRun,
        bool canAdminEitherFacility,
        DateTimeOffset runAt,
        bool isAuthoritative,
        CancellationToken cancellationToken)
    {
        var employment = await ResolveEmploymentAtAsync(userId, runAt, cancellationToken);
        if (!isAuthoritative)
        {
            return existingRun is null && employment == EmploymentFacilities.Unassigned
                ? RunFacilityResolution.Failed("An administrator must assign your Employment Facility before you can record an Actual Run.")
                : RunFacilityResolution.NotAuthoritative();
        }
        if (await AuthoritativeWarehouseConfigurationErrorAsync(cancellationToken) is string facilityConfigurationError)
        {
            return RunFacilityResolution.Failed(facilityConfigurationError);
        }

        if (existingRun is not null)
        {
            if (existingRun.RunFacilityWarehouseId is null)
            {
                return RunFacilityResolution.Failed("This historical Actual Run has no Run Facility and cannot be corrected until its attribution is reviewed.");
            }
            if (requestedWarehouseId is not null && requestedWarehouseId != existingRun.RunFacilityWarehouseId)
            {
                return RunFacilityResolution.Failed("A correction must retain the Actual Run's original Run Facility.");
            }

            var persistedCode = existingRun.RunFacilityCodeSnapshot;
            if (string.IsNullOrWhiteSpace(persistedCode))
            {
                persistedCode = await dbContext.Warehouses.AsNoTracking()
                    .Where(x => x.Id == existingRun.RunFacilityWarehouseId)
                    .Select(x => x.Code)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            if (persistedCode is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
            {
                return RunFacilityResolution.Failed("The Actual Run's persisted facility is not WP or EBS and requires review.");
            }
            if (employment is EmploymentFacilities.Wp or EmploymentFacilities.Ebs
                && !string.Equals(employment, persistedCode, StringComparison.OrdinalIgnoreCase)
                && !canAdminEitherFacility)
            {
                return RunFacilityResolution.Failed($"Your {employment} employment assignment cannot correct a {persistedCode} Actual Run.");
            }
            if (employment == EmploymentFacilities.Unassigned && !canAdminEitherFacility)
            {
                return RunFacilityResolution.Failed("An administrator must assign an Employment Facility before you can correct an Actual Run.");
            }

            return new RunFacilityResolution(
                existingRun.RunFacilityWarehouseId,
                persistedCode,
                existingRun.RunFacilityAssignmentSource ?? RunFacilityAssignmentSources.HistoricalBackfill,
                null);
        }

        if (employment == EmploymentFacilities.Unassigned)
        {
            return RunFacilityResolution.Failed("An administrator must assign your Employment Facility before you can record an Actual Run.");
        }

        if (employment is EmploymentFacilities.Wp or EmploymentFacilities.Ebs)
        {
            var assignedCandidates = await dbContext.Warehouses.AsNoTracking()
                .Where(x => x.IsActive && x.Code == employment)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (assignedCandidates.Count != 1)
            {
                return RunFacilityResolution.Failed($"Exactly one active {employment} warehouse is required before runs can be recorded.");
            }
            var assigned = assignedCandidates[0];
            if (requestedWarehouseId is not null && requestedWarehouseId != assigned.Id)
            {
                return RunFacilityResolution.Failed($"Your Employment Facility requires this run to be credited to {employment}.");
            }
            return new RunFacilityResolution(assigned.Id, assigned.Code, RunFacilityAssignmentSources.Employment, null);
        }

        if (requestedWarehouseId is null)
        {
            return RunFacilityResolution.Failed("Shared / Management users must explicitly select WP or EBS as the Run Facility.");
        }
        var selected = await dbContext.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requestedWarehouseId && x.IsActive, cancellationToken);
        if (selected?.Code is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
        {
            return RunFacilityResolution.Failed("Run Facility must be an active WP or EBS facility.");
        }
        if (await dbContext.Warehouses.AsNoTracking().CountAsync(
                x => x.IsActive && x.Code == selected.Code,
                cancellationToken) != 1)
        {
            return RunFacilityResolution.Failed($"Exactly one active {selected.Code} warehouse is required before runs can be recorded.");
        }
        return new RunFacilityResolution(selected.Id, selected.Code, RunFacilityAssignmentSources.SharedSelection, null);
    }

    private async Task<RunFacilityResolution> ResolveApprovedOverrideFacilityAsync(
        ActualRunOverrideRequest request,
        ActualRun? existingRun,
        CancellationToken cancellationToken)
    {
        if (request.RunFacilityWarehouseId is null)
        {
            return RunFacilityResolution.Failed("The pending override has no persisted Run Facility and cannot be applied.");
        }
        if (existingRun?.RunFacilityWarehouseId is int existingFacilityId
            && existingFacilityId != request.RunFacilityWarehouseId)
        {
            return RunFacilityResolution.Failed("The pending correction would change the Actual Run's original Run Facility.");
        }
        var code = request.RunFacilityCodeSnapshot;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await dbContext.Warehouses.AsNoTracking()
                .Where(x => x.Id == request.RunFacilityWarehouseId)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (code is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
        {
            return RunFacilityResolution.Failed("The pending override's Run Facility is not WP or EBS.");
        }
        return new RunFacilityResolution(
            request.RunFacilityWarehouseId,
            code,
            request.RunFacilityAssignmentSource ?? RunFacilityAssignmentSources.HistoricalBackfill,
            null);
    }

    private async Task<RunFacilityResolution> ResolveLegacyReportingFacilityAsync(
        int? userId,
        ClaimsPrincipal principal,
        BinsRunEntry? existing,
        DateTimeOffset runAt,
        bool isAuthoritative,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return RunFacilityResolution.Failed("The current user account could not be resolved.");
        }
        var employment = await ResolveEmploymentAtAsync(userId.Value, runAt, cancellationToken);
        var canAdminEitherFacility = await userAccessService.HasAccessAsync(
            principal,
            ApplicationAreas.BinsRun,
            PageAccessLevel.Admin,
            cancellationToken);

        if (!isAuthoritative)
        {
            return existing is null && employment == EmploymentFacilities.Unassigned
                ? RunFacilityResolution.Failed("An administrator must assign your Employment Facility before you can record a Bins Run.")
                : RunFacilityResolution.NotAuthoritative();
        }
        if (await AuthoritativeWarehouseConfigurationErrorAsync(cancellationToken) is string facilityConfigurationError)
        {
            return RunFacilityResolution.Failed(facilityConfigurationError);
        }

        if (existing is not null)
        {
            if (existing.ReportingFacilityWarehouseId is null
                || existing.ReportingFacilityCodeSnapshot is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
            {
                return RunFacilityResolution.Failed("This historical Bins Run has no Run Facility and cannot be corrected until its attribution is reviewed.");
            }
            if (employment is EmploymentFacilities.Wp or EmploymentFacilities.Ebs
                && !string.Equals(employment, existing.ReportingFacilityCodeSnapshot, StringComparison.OrdinalIgnoreCase)
                && !canAdminEitherFacility)
            {
                return RunFacilityResolution.Failed($"Your {employment} employment assignment cannot correct a {existing.ReportingFacilityCodeSnapshot} Bins Run.");
            }
            if (employment == EmploymentFacilities.Unassigned && !canAdminEitherFacility)
            {
                return RunFacilityResolution.Failed("An administrator must assign your Employment Facility before you can correct a Bins Run.");
            }
            return new RunFacilityResolution(
                existing.ReportingFacilityWarehouseId,
                existing.ReportingFacilityCodeSnapshot,
                existing.ReportingFacilityAssignmentSource,
                null);
        }

        if (employment == EmploymentFacilities.Shared)
        {
            return RunFacilityResolution.Failed("Shared / Management employees must use Record Actual Run to select WP or EBS explicitly.");
        }
        if (employment is not (EmploymentFacilities.Wp or EmploymentFacilities.Ebs))
        {
            return RunFacilityResolution.Failed("An administrator must assign your Employment Facility before you can record a Bins Run.");
        }
        var facilityCandidates = await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && x.Code == employment)
            .Take(2)
            .ToListAsync(cancellationToken);
        return facilityCandidates.Count != 1
            ? RunFacilityResolution.Failed($"Exactly one active {employment} warehouse is required before runs can be recorded.")
            : new RunFacilityResolution(facilityCandidates[0].Id, facilityCandidates[0].Code, RunFacilityAssignmentSources.Employment, null);
    }

    private async Task<string> ResolveEmploymentAtAsync(
        int userId,
        DateTimeOffset runAt,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new { x.EmploymentFacility, x.EmploymentEffectiveAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return EmploymentFacilities.Unassigned;
        }

        var history = await dbContext.UserEmploymentHistory.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.PreviousEmploymentFacility,
                x.EmploymentFacility,
                x.EffectiveAt
            })
            .ToListAsync(cancellationToken);
        var transitions = history
            .OrderBy(x => x.EffectiveAt)
            .ThenBy(x => x.Id)
            .Select(x => new RunReportingService.EmploymentTransition(
                x.UserId,
                x.PreviousEmploymentFacility,
                x.EmploymentFacility,
                x.EffectiveAt))
            .ToList();
        return RunReportingService.ResolveEmploymentAt(
            user.EmploymentFacility,
            user.EmploymentEffectiveAt,
            transitions,
            runAt);
    }

    private async Task<string?> AuthoritativeWarehouseConfigurationErrorAsync(CancellationToken cancellationToken)
    {
        var facilities = await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && (x.Code == EmploymentFacilities.Wp || x.Code == EmploymentFacilities.Ebs))
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
        return new[] { EmploymentFacilities.Wp, EmploymentFacilities.Ebs }
            .All(code => facilities.Count(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase)) == 1)
            ? null
            : "Exactly one active WP warehouse and one active EBS warehouse are required before authoritative runs can be recorded.";
    }

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking().Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record RunFacilityResolution(int? WarehouseId, string? Code, string? AssignmentSource, string? Error)
    {
        public static RunFacilityResolution Failed(string error) => new(null, null, null, error);
        public static RunFacilityResolution NotAuthoritative() => new(null, null, null, null);
    }

    private async Task AddAuditAsync(string action, BinsRunEntry entry, int? userId, object? before, object? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(BinsRunEntry),
            EntityKey = entry.Id.ToString(),
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = SourceApplication,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Task.CompletedTask;
    }

    private static object EntrySnapshot(BinsRunEntry entry) => new
    {
        entry.Id,
        entry.RoomId,
        entry.ReceiptId,
        entry.SourceInventoryAdjustmentId,
        entry.InventoryAdjustmentId,
        entry.GrowerName,
        entry.LotNumber,
        entry.VarietyCode,
        entry.PreviousAvailableBins,
        entry.BinsRun,
        entry.NewAvailableBins,
        entry.RunAt,
        entry.IsReversed,
        entry.ReverseReason
    };

    private static IReadOnlyDictionary<string, decimal> DeserializeDistribution(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
                ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string CurrentStorageLotKey(int roomId, string lot, string variety) =>
        RoomInventoryImportService.CurrentStorageLotKey(roomId, lot, variety);

    private static CanonicalQcFruitIdentity? QcIdentity(InventorySnapshot snapshot) =>
        CanonicalQcFruitIdentity.Create(
            snapshot.CropYear,
            snapshot.GrowerLotId,
            snapshot.GrowerNumber ?? snapshot.Lot,
            snapshot.Lot,
            snapshot.FruitProfileId,
            snapshot.Variety,
            snapshot.ProductionType,
            snapshot.IsOrganic);

    private static string QcIdentityKey(InventorySnapshot snapshot) =>
        QcIdentity(snapshot)?.LookupKey ?? $"UNRESOLVED:{snapshot.InventoryKey}";

    private static string ReceiptLotNumber(Receipt receipt) =>
        !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode;

    private static RoomInventoryLedgerSnapshot ToLedgerSnapshot(InventorySnapshot x) => new(
        x.WarehouseId,
        x.Facility,
        x.RoomId,
        x.Room,
        "",
        x.CropYear,
        x.GrowerLotId,
        x.FruitProfileId,
        x.Grower,
        x.GrowerNumber,
        x.Lot,
        x.PoolStart,
        x.Variety,
        x.Variety,
        x.Variety,
        x.FruitType,
        x.ProductionType,
        x.IsOrganic,
        x.InventoryStatus,
        0, 0, 0, 0, 0, 0, 0, 0, 0,
        x.CurrentBins,
        0,
        x.ReceiptDate ?? DateTimeOffset.MinValue,
        x.ReceiptDate ?? DateTimeOffset.MinValue,
        x.InventoryAdjustmentId ?? 0,
        x.ReceiptReference ?? "");

    private static RunProjectionInventorySource ToPlanningInventory(InventorySnapshot x) =>
        new(
            x.InventoryKey,
            x.ReceiptId,
            x.ReceiptReference,
            x.InventoryAdjustmentId,
            x.WarehouseId,
            x.RoomId,
            x.Facility,
            x.Room,
            x.FruitProfileId,
            x.FruitType,
            x.CanonicalOrchardBlockId,
            x.Grower,
            x.GrowerNumber,
            x.Lot,
            x.Variety,
            x.CurrentBins,
            x.ReceiptDate);

    private sealed record InventorySnapshot(
        string InventoryKey,
        long? ReceiptId,
        string? ReceiptReference,
        long? InventoryAdjustmentId,
        int WarehouseId,
        int RoomId,
        string Facility,
        string Room,
        int? CropYear,
        int? GrowerLotId,
        int? FruitProfileId,
        string Grower,
        string? GrowerNumber,
        string Lot,
        string? PoolStart,
        string Variety,
        string FruitType,
        string ProductionType,
        bool? IsOrganic,
        int? CanonicalOrchardBlockId,
        string InventoryStatus,
        int CurrentBins,
        DateTimeOffset? ReceiptDate);

    private sealed record LotSampleDistribution(
        SizeSampleDistribution SizeDistribution,
        IReadOnlyDictionary<string, decimal> GradePercentages,
        DateTimeOffset SampleTakenAt);
}
