using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRunProjectionService
{
    Task<RunProjectionPlannerViewModel> GetPlannerAsync(DateOnly? date, long? projectionId, string? facility, string? deletionStatus, string? sort, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<ProjectionOutcomeViewModel?> GetOutcomeAsync(long id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunProjectionSourceCandidateViewModel>> SearchSourcesAsync(string? query, int? facilityWarehouseId, int? roomId, string? projectionMode, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunProjectionQcChoiceViewModel>> GetFieldSampleChoicesAsync(long projectionId, int canonicalBlockId, int fruitProfileId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(long? Id, string? Error)> CreateAsync(RunProjectionCreateForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateHeaderAsync(RunProjectionHeaderForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> AddSourceAsync(RunProjectionAddSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> UpdateSourceAsync(RunProjectionUpdateSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ApplyPackoutToAllAsync(RunProjectionApplyPackoutForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(RunProjectionPackPlanPreviewViewModel? Preview, string? Error)> PreviewPackPlanAsync(RunProjectionPackPlanForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> ApplyPackPlanAsync(RunProjectionPackPlanForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> RefreshSourceAsync(long projectionId, long sourceId, long concurrencyVersion, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> RemoveSourceAsync(long projectionId, long sourceId, long concurrencyVersion, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> MarkReadyAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> CancelAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(long? Id, string? Error)> DuplicateAsync(RunProjectionDuplicateForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<(long? Id, string? Error)> CreateInventoryFromPreharvestAsync(RunProjectionCreateInventoryForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<RunProjectionDeletionConfirmationViewModel?> GetDeletionConfirmationAsync(long id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> DeleteAsync(DeleteRunProjectionForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public static class RunProjectionSettings
{
    public const string ApplePoundsPerBinKey = "RunProjection__ApplePoundsPerBin";
    public const string PearPoundsPerBinKey = "RunProjection__PearPoundsPerBin";
    public const string StandardBoxWeightKey = "RunProjection__StandardBoxWeightPounds";
    public const string DraftExpirationDaysKey = "RunProjection__DraftExpirationDays";
    public const string VisibilityPastDaysKey = "RunProjection__VisibilityPastDays";
    public const string VisibilityFutureDaysKey = "RunProjection__VisibilityFutureDays";
    public const string DefaultExpectedPackoutPercentKey = "RunProjection__DefaultExpectedPackoutPercent";
    public const string MinimumDistributionFruitKey = "RunProjection__MinimumDistributionFruit";

    public const int DefaultDraftExpirationDays = 14;
    public const int DefaultVisibilityPastDays = 30;
    public const int DefaultVisibilityFutureDays = 14;
    public const int DefaultMinimumDistributionFruit = 10;
}

public sealed class RunProjectionService(
    CropQcDbContext dbContext,
    IBinsRunService binsRunService,
    IUserAccessService userAccessService,
    ICropYearService cropYearService,
    IBusinessTimeService businessTime,
    IFieldSampleTrendService? fieldSampleTrendService = null,
    ILogger<RunProjectionService>? logger = null) : IRunProjectionService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const string SourceApplication = "CropQc.Web";
    private static readonly string[] OperationalFacilityCodes = ["WP", "EBS"];
    private IFieldSampleTrendService FieldSampleTrends { get; } =
        fieldSampleTrendService ?? new FieldSampleTrendService(dbContext);

    public async Task<RunProjectionPlannerViewModel> GetPlannerAsync(
        DateOnly? date,
        long? projectionId,
        string? facility,
        string? deletionStatus,
        string? sort,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var settings = await LoadSettingsAsync(cancellationToken);
        await ExpireDraftsAsync(settings.DraftExpirationDays, cancellationToken);
        var canEdit = await CanAsync(user, PageAccessLevel.Edit, cancellationToken);
        var canAdmin = await CanAsync(user, PageAccessLevel.Admin, cancellationToken);
        var selectedFacility = NormalizeFacilityFilter(facility, canAdmin);
        var selectedDeletionStatus = NormalizeDeletionStatus(deletionStatus, canAdmin);
        var selectedSort = NormalizeSort(sort);
        var facilities = await OperationalFacilities()
            .OrderByDescending(x => x.Code == "WP")
            .ThenBy(x => x.Code)
            .Select(x => new RunProjectionFacilityOptionViewModel(x.Id, x.Code, x.Name))
            .ToListAsync(cancellationToken);
        var today = businessTime.PacificDate(businessTime.UtcNow);
        var selectedDate = date ?? today;
        var start = today.AddDays(-settings.VisibilityPastDays);
        var end = today.AddDays(settings.VisibilityFutureDays);

        var filtered = ApplyPlannerFilters(dbContext.RunProjections.AsNoTracking(), selectedFacility, selectedDeletionStatus);
        var calendarRows = await filtered
            .Where(x => x.PlannedRunDate >= start && x.PlannedRunDate <= end)
            .GroupBy(x => new
            {
                x.PlannedRunDate,
                Facility = x.FacilityWarehouse == null
                    ? (x.FacilityCodeSnapshot ?? "Unassigned")
                    : x.FacilityWarehouse.Code
            })
            .Select(x => new
            {
                Date = x.Key.PlannedRunDate,
                x.Key.Facility,
                Count = x.Count(),
                PlannedBins = x.Sum(y => y.TotalPlannedBins)
            })
            .ToListAsync(cancellationToken);

        var recordsQuery = filtered.Where(x => x.PlannedRunDate == selectedDate);
        IOrderedQueryable<RunProjection> orderedRecords = selectedSort switch
        {
            "RunDate" => recordsQuery.OrderBy(x => x.PlannedRunDate).ThenBy(x => x.Name),
            "Status" => recordsQuery.OrderBy(x => x.Status).ThenBy(x => x.Name),
            "Updated" => recordsQuery.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Name),
            "PlannedBins" => recordsQuery.OrderByDescending(x => x.TotalPlannedBins).ThenBy(x => x.Name),
            _ => recordsQuery.OrderBy(x => x.FacilityWarehouse == null ? x.FacilityCodeSnapshot : x.FacilityWarehouse.Code)
                .ThenBy(x => x.Name)
        };
        var records = await orderedRecords
            .ThenBy(x => x.Id)
            .Select(x => new RunProjectionListItemViewModel
            {
                Id = x.Id,
                PlannedRunDate = x.PlannedRunDate,
                Name = x.Name,
                Status = x.Status,
                ProjectionMode = x.ProjectionMode,
                FacilityWarehouseId = x.FacilityWarehouseId,
                FacilityCode = x.FacilityWarehouse == null
                    ? (x.FacilityCodeSnapshot ?? "Unassigned")
                    : x.FacilityWarehouse.Code,
                TotalPlannedBins = x.TotalPlannedBins,
                TotalProjectedPounds = x.TotalProjectedPounds,
                TotalProjectedBoxes = x.TotalProjectedBoxes,
                TotalRoundedProjectedBoxes = x.TotalRoundedProjectedBoxes,
                TotalPackedProjectedPounds = x.TotalPackedProjectedPounds,
                TotalPackedProjectedBoxes = x.TotalPackedProjectedBoxes,
                TotalCullProjectedBoxes = x.TotalCullProjectedBoxes,
                Creator = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                UpdatedAt = x.UpdatedAt,
                SourceCount = x.Sources.Count,
                ConvertedSourceCount = x.Sources.Count(source => source.ActualBinsRunEntryId != null),
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                DeletionReason = x.DeletionReason
            })
            .ToListAsync(cancellationToken);

        var selectedId = projectionId is long requestedId && records.Any(x => x.Id == requestedId)
            ? requestedId
            : records.FirstOrDefault()?.Id;
        var recentActivity = await ApplyPlannerFilters(
                dbContext.RunProjections.AsNoTracking(),
                selectedFacility,
                selectedDeletionStatus)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .Take(12)
            .Select(x => new RunProjectionListItemViewModel
            {
                Id = x.Id,
                PlannedRunDate = x.PlannedRunDate,
                Name = x.Name,
                Status = x.Status,
                ProjectionMode = x.ProjectionMode,
                FacilityWarehouseId = x.FacilityWarehouseId,
                FacilityCode = x.FacilityWarehouse == null
                    ? (x.FacilityCodeSnapshot ?? "Unassigned")
                    : x.FacilityWarehouse.Code,
                TotalPlannedBins = x.TotalPlannedBins,
                TotalProjectedPounds = x.TotalProjectedPounds,
                TotalProjectedBoxes = x.TotalProjectedBoxes,
                TotalRoundedProjectedBoxes = x.TotalRoundedProjectedBoxes,
                TotalPackedProjectedPounds = x.TotalPackedProjectedPounds,
                TotalPackedProjectedBoxes = x.TotalPackedProjectedBoxes,
                TotalCullProjectedBoxes = x.TotalCullProjectedBoxes,
                Creator = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                UpdatedAt = x.UpdatedAt,
                SourceCount = x.Sources.Count,
                ConvertedSourceCount = x.Sources.Count(source => source.ActualBinsRunEntryId != null),
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                DeletionReason = x.DeletionReason
            })
            .ToListAsync(cancellationToken);
        var totals = await filtered
            .Where(x => x.PlannedRunDate == selectedDate)
            .GroupBy(x => x.FacilityWarehouse == null
                ? (x.FacilityCodeSnapshot ?? "Unassigned")
                : x.FacilityWarehouse.Code)
            .Select(x => new RunProjectionFacilityTotalsViewModel
            {
                FacilityCode = x.Key,
                ProjectionCount = x.Count(),
                PlannedBins = x.Sum(y => y.TotalPlannedBins),
                GrossPounds = x.Sum(y => y.TotalProjectedPounds),
                PackedPounds = x.Sum(y => y.TotalPackedProjectedPounds),
                PackedBoxes = x.Sum(y => y.TotalPackedProjectedBoxes),
                CullBoxes = x.Sum(y => y.TotalCullProjectedBoxes)
            })
            .OrderByDescending(x => x.FacilityCode == "WP")
            .ThenBy(x => x.FacilityCode)
            .ToListAsync(cancellationToken);
        var unassignedCount = canAdmin
            ? await dbContext.RunProjections.AsNoTracking().CountAsync(x => !x.IsDeleted && x.FacilityWarehouseId == null, cancellationToken)
            : 0;
        var preferredFacilityId = facilities
            .FirstOrDefault(x => x.Code.Equals(selectedFacility, StringComparison.OrdinalIgnoreCase))
            ?.WarehouseId;
        RunProjectionDetailViewModel? selectedProjection = null;
        string? plannerWarning = null;
        string? diagnosticReference = null;
        if (selectedId is not null)
        {
            try
            {
                selectedProjection = await GetDetailAsync(selectedId.Value, canEdit, canAdmin, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnosticReference = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];
                plannerWarning = $"Projection {selectedId.Value} could not be displayed. Other projections remain available. Reference {diagnosticReference}.";
                logger?.LogError(
                    exception,
                    "Run planner selected projection failed. Route={Route} ProjectionId={ProjectionId} CorrelationId={CorrelationId} Category={Category}",
                    "/BinsRun",
                    selectedId.Value,
                    diagnosticReference,
                    DatabaseFailureDiagnostics.Classify(exception).Category);
            }
        }
        if (selectedProjection?.IsDeleted == true
            && projectionId == selectedProjection.Id
            && canAdmin)
        {
            var userId = await CurrentUserIdAsync(user, cancellationToken);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "InspectDeleted",
                EntityName = nameof(RunProjection),
                EntityKey = selectedProjection.Id.ToString(CultureInfo.InvariantCulture),
                UserId = userId,
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    selectedProjection.Id,
                    selectedProjection.FacilityCode,
                    selectedProjection.DeletedAt,
                    Result = "Viewed"
                }),
                SourceApplication = SourceApplication,
                CreatedAt = businessTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return new RunProjectionPlannerViewModel
        {
            SelectedDate = selectedDate,
            CalendarDays = Enumerable.Range(0, settings.VisibilityPastDays + settings.VisibilityFutureDays + 1)
                .Select(offset => start.AddDays(offset))
                .Select(day =>
                {
                    var rows = calendarRows.Where(x => x.Date == day).ToList();
                    return new RunProjectionCalendarDayViewModel
                    {
                        Date = day,
                        ProjectionCount = rows.Sum(x => x.Count),
                        WpProjectionCount = rows.Where(x => x.Facility == "WP").Sum(x => x.Count),
                        WpPlannedBins = rows.Where(x => x.Facility == "WP").Sum(x => x.PlannedBins),
                        EbsProjectionCount = rows.Where(x => x.Facility == "EBS").Sum(x => x.Count),
                        EbsPlannedBins = rows.Where(x => x.Facility == "EBS").Sum(x => x.PlannedBins),
                        UnassignedProjectionCount = rows.Where(x => x.Facility == "Unassigned").Sum(x => x.Count),
                        UnassignedPlannedBins = rows.Where(x => x.Facility == "Unassigned").Sum(x => x.PlannedBins),
                        IsSelected = day == selectedDate,
                        IsToday = day == today
                    };
                })
                .ToList(),
            Projections = records,
            RecentActivity = recentActivity,
            FacilityOptions = facilities,
            FacilityTotals = totals,
            SelectedProjection = selectedProjection,
            CreateForm = new RunProjectionCreateForm
            {
                PlannedRunDate = selectedDate,
                Name = $"Run {records.Count + 1}",
                FacilityWarehouseId = preferredFacilityId
            },
            SelectedFacility = selectedFacility,
            SelectedDeletionStatus = selectedDeletionStatus,
            SelectedSort = selectedSort,
            UnassignedProjectionCount = unassignedCount,
            CanEdit = canEdit,
            CanAdmin = canAdmin,
            CanViewDeleted = canAdmin,
            VisibilityPastDays = settings.VisibilityPastDays,
            VisibilityFutureDays = settings.VisibilityFutureDays,
            DefaultExpectedPackoutPercent = settings.DefaultExpectedPackoutPercent,
            PlannerWarning = plannerWarning,
            DiagnosticReference = diagnosticReference
        };
    }

    public async Task<ProjectionOutcomeViewModel?> GetOutcomeAsync(
        long id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var canAdmin = await CanAsync(user, PageAccessLevel.Admin, cancellationToken);
        var detail = await GetDetailAsync(id, false, canAdmin, cancellationToken);
        return detail is null || detail.IsDeleted
            ? null
            : ProjectionOutcomeCalculator.Build(detail, businessTime.UtcNow);
    }

    public async Task<IReadOnlyList<RunProjectionSourceCandidateViewModel>> SearchSourcesAsync(
        string? query,
        int? facilityWarehouseId,
        int? roomId,
        string? projectionMode,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var mode = string.Equals(projectionMode, RunProjectionModes.Preharvest, StringComparison.OrdinalIgnoreCase)
            ? RunProjectionModes.Preharvest
            : RunProjectionModes.Inventory;
        var normalized = query?.Trim() ?? "";
        var normalizedUpper = normalized.ToUpperInvariant();
        var activeCropYear = cropYearService.GetCurrentCropYear(businessTime.NowPacific);
        var validFacilityId = facilityWarehouseId is int requestedFacilityId
            && await OperationalFacilities().AnyAsync(x => x.Id == requestedFacilityId, cancellationToken)
                ? requestedFacilityId
                : (int?)null;
        IReadOnlyList<RunProjectionInventorySource> inventory = mode == RunProjectionModes.Inventory
            && validFacilityId is not null
            ? await binsRunService.SearchPlanningInventoryAsync(normalized, validFacilityId, roomId, 50, cancellationToken)
            : [];
        var receiptIds = inventory.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToList();
        var blockProfileKeys = inventory
            .Where(x => x.CanonicalOrchardBlockId != null && x.FruitProfileId != null)
            .Select(x => new { BlockId = x.CanonicalOrchardBlockId!.Value, ProfileId = x.FruitProfileId!.Value })
            .Distinct()
            .ToList();
        var blockIds = blockProfileKeys.Select(x => x.BlockId).Distinct().ToList();
        var profileIds = blockProfileKeys.Select(x => x.ProfileId).Distinct().ToList();

        var receiptQcIds = receiptIds.Count == 0
            ? []
            : await UsableQcSamples(activeCropYear)
                .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                .Select(x => x.ReceiptId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        var fieldMatches = blockIds.Count == 0 || profileIds.Count == 0
            ? []
            : await UsableFieldSamples(activeCropYear)
                .Where(x => blockIds.Contains(x.CanonicalOrchardBlockId!.Value)
                    && profileIds.Contains(x.FieldSampleFruitProfileId!.Value))
                .Select(x => new { BlockId = x.CanonicalOrchardBlockId!.Value, ProfileId = x.FieldSampleFruitProfileId!.Value })
                .Distinct()
                .ToListAsync(cancellationToken);
        var fieldMatchKeys = fieldMatches.Select(x => $"{x.BlockId}:{x.ProfileId}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = inventory.Select(x => new RunProjectionSourceCandidateViewModel(
                x.InventoryKey,
                RunProjectionSourceTypes.Inventory,
                $"{x.Facility} / {x.Room} — {x.Grower}"
                    + (string.IsNullOrWhiteSpace(x.GrowerNumber) ? "" : $" — grower # {x.GrowerNumber}")
                    + (string.IsNullOrWhiteSpace(x.ReceiptReference) ? "" : $" — receipt {x.ReceiptReference}")
                    + $" — lot {x.Lot} — {x.Variety} — {x.CurrentBins} bins",
                x.Facility,
                x.Room,
                x.Lot,
                x.GrowerNumber,
                x.Grower,
                null,
                null,
                x.Variety,
                RunProjectionCalculationService.NormalizeCommodity(x.FruitType),
                x.CurrentBins,
                x.ReceiptId is not null && receiptQcIds.Contains(x.ReceiptId.Value),
                x.CanonicalOrchardBlockId is not null
                    && x.FruitProfileId is not null
                    && fieldMatchKeys.Contains($"{x.CanonicalOrchardBlockId}:{x.FruitProfileId}"),
                x.ReceiptDate,
                x.CanonicalOrchardBlockId,
                x.FruitProfileId,
                null))
            .ToList();

        var fieldQuery = UsableFieldSamples(activeCropYear)
            .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
            .Include(x => x.FieldSampleFruitProfile)
            .Where(x => normalized.Length == 0
                || x.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName.ToUpper().Contains(normalizedUpper)
                || x.CanonicalOrchardBlock.CanonicalBlockName.ToUpper().Contains(normalizedUpper)
                || (x.FieldSampleGrowerName ?? "").ToUpper().Contains(normalizedUpper)
                || (x.FieldSampleGrowerNumber ?? "").ToUpper().Contains(normalizedUpper)
                || x.FieldSampleFruitProfile!.Name.ToUpper().Contains(normalizedUpper)
                || x.FieldSampleFruitProfile.VarietyCode.ToUpper().Contains(normalizedUpper));
        if (mode == RunProjectionModes.Preharvest)
        {
            var fieldRows = await fieldQuery
                .OrderByDescending(x => x.SampleTakenAt)
                .Take(200)
                .ToListAsync(cancellationToken);
            candidates.AddRange(fieldRows
                .GroupBy(x => new { x.CanonicalOrchardBlockId, x.FieldSampleFruitProfileId })
                .Select(x => x.OrderByDescending(y => y.SampleTakenAt).ThenByDescending(y => y.Id).First())
                .Take(50)
                .Select(x => new RunProjectionSourceCandidateViewModel(
                $"B:{x.CanonicalOrchardBlockId}:{x.FieldSampleFruitProfileId}",
                RunProjectionSourceTypes.FieldSample,
                $"{x.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName} — {x.CanonicalOrchardBlock.CanonicalBlockName} — {x.FieldSampleFruitProfile!.Name} — Field Sample {businessTime.FormatPacific(x.SampleTakenAt, "MMM d, yyyy", false)}",
                null,
                null,
                null,
                x.CanonicalOrchardBlock.CanonicalOrchard.OrchardName,
                x.FieldSampleGrowerName,
                x.FieldSampleGrowerNumber,
                x.CanonicalOrchardBlock.CanonicalBlockName,
                x.FieldSampleFruitProfile.Name,
                RunProjectionCalculationService.NormalizeCommodity(x.FieldSampleFruitProfile.FruitType),
                null,
                false,
                true,
                x.SampleTakenAt,
                x.CanonicalOrchardBlockId,
                x.FieldSampleFruitProfileId,
                x.Id)));
        }

        return candidates
            .OrderBy(x => x.SourceType)
            .ThenBy(x => x.Facility)
            .ThenBy(x => x.Room)
            .ThenBy(x => x.Orchard)
            .ThenBy(x => x.Block)
            .ThenBy(x => x.Variety)
            .Take(100)
            .ToList();
    }

    public async Task<IReadOnlyList<RunProjectionQcChoiceViewModel>> GetFieldSampleChoicesAsync(
        long projectionId,
        int canonicalBlockId,
        int fruitProfileId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.View, cancellationToken);
        var projection = await dbContext.RunProjections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == projectionId, cancellationToken);
        if (projection is null || projection.ProjectionMode != RunProjectionModes.Preharvest)
        {
            return [];
        }

        var samples = await UsableFieldSamples(projection.CropYear)
            .Where(x => x.CanonicalOrchardBlockId == canonicalBlockId
                && x.FieldSampleFruitProfileId == fruitProfileId)
            .OrderByDescending(x => x.SampleTakenAt)
            .ThenByDescending(x => x.Id)
            .Include(x => x.SampleType)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
            .Take(25)
            .ToListAsync(cancellationToken);
        return samples.Select((x, index) => QcChoice(x, RunProjectionQcSourceTypes.FieldSample, index == 0)).ToList();
    }

    public async Task<(long? Id, string? Error)> CreateAsync(
        RunProjectionCreateForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to create projections.");
        }

        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
        {
            return (null, "Projection name is required and must be 100 characters or fewer.");
        }
        var requestedMode = form.ProjectionMode?.Trim() ?? "";
        if (!RunProjectionModes.All.Contains(requestedMode))
        {
            return (null, "Select Preharvest or Inventory planning mode.");
        }
        var projectionMode = requestedMode.Equals(RunProjectionModes.Preharvest, StringComparison.OrdinalIgnoreCase)
            ? RunProjectionModes.Preharvest
            : RunProjectionModes.Inventory;
        var facility = form.FacilityWarehouseId is int facilityId
            ? await OperationalFacilities().SingleOrDefaultAsync(x => x.Id == facilityId, cancellationToken)
            : null;
        if (facility is null)
        {
            return (null, "Select WP or EBS for this projection.");
        }

        var settings = await LoadSettingsAsync(cancellationToken);
        var now = businessTime.UtcNow;
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var projection = new RunProjection
        {
            PlannedRunDate = form.PlannedRunDate,
            Name = name,
            Status = RunProjectionStatuses.Draft,
            ProjectionMode = projectionMode,
            FacilityWarehouseId = facility.Id,
            FacilityCodeSnapshot = facility.Code,
            CropYear = cropYearService.GetCurrentCropYear(businessTime.NowPacific),
            ApplePoundsPerBin = settings.ApplePoundsPerBin,
            PearPoundsPerBin = settings.PearPoundsPerBin,
            StandardBoxWeightPounds = settings.StandardBoxWeightPounds,
            ExpiresAt = now.AddDays(settings.DraftExpirationDays),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        dbContext.RunProjections.Add(projection);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("Create", projection, userId, null, ProjectionSnapshot(projection), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (projection.Id, null);
    }

    public async Task<string?> UpdateHeaderAsync(RunProjectionHeaderForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.Id, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100) return "Projection name is required and must be 100 characters or fewer.";
        var facility = form.FacilityWarehouseId is int facilityId
            ? await OperationalFacilities().SingleOrDefaultAsync(x => x.Id == facilityId, cancellationToken)
            : null;
        if (facility is null) return "Select WP or EBS for this projection.";
        if (projection.FacilityWarehouseId != facility.Id)
        {
            if (projection.Status != RunProjectionStatuses.Draft)
            {
                return $"Facility cannot be changed on a {projection.Status} projection. Duplicate it to the other facility instead.";
            }
            var incompatibleInventorySources = projection.Sources
                .Where(x => x.SourceType == RunProjectionSourceTypes.Inventory && x.WarehouseId != facility.Id)
                .Select(x => x.Id)
                .ToList();
            if (incompatibleInventorySources.Count > 0)
            {
                return $"Remove or remap inventory source(s) {string.Join(", ", incompatibleInventorySources)} before changing the facility.";
            }
        }
        var before = ProjectionSnapshot(projection);
        projection.Name = name;
        projection.PlannedRunDate = form.PlannedRunDate;
        projection.FacilityWarehouseId = facility.Id;
        projection.FacilityCodeSnapshot = facility.Code;
        Touch(projection, userId);
        await AddAuditAsync("UpdateHeader", projection, userId, before, ProjectionSnapshot(projection), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> AddSourceAsync(RunProjectionAddSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (form.PlannedBins <= 0) return "Planned bins must be greater than zero.";
        if (form.ExpectedPackoutPercent is < 0 or > 100) return "Expected Packout % must be between 0 and 100.";
        var settings = await LoadSettingsAsync(cancellationToken);
        var expectedPackout = form.ExpectedPackoutPercent ?? settings.DefaultExpectedPackoutPercent;
        var source = await ResolveSourceAsync(
            projection,
            form.SourceKey,
            form.SelectedQcSource,
            form.PlannedBins,
            expectedPackout,
            settings.MinimumDistributionFruit,
            form.AvailabilityOverrideAcknowledged,
            cancellationToken);
        if (source.Error is not null || source.Entity is null)
        {
            return source.Error ?? "The selected projection source could not be resolved.";
        }
        if (projection.ProjectionMode == RunProjectionModes.Preharvest
            && source.Entity.SourceType != RunProjectionSourceTypes.FieldSample)
        {
            return "Preharvest projections accept confirmed Field Sample blocks only.";
        }
        if (projection.ProjectionMode == RunProjectionModes.Inventory
            && source.Entity.SourceType != RunProjectionSourceTypes.Inventory)
        {
            return "Inventory projections require a real receipt or inventory lot.";
        }
        source.Entity.ExpectedPackoutUsedDefault =
            form.ExpectedPackoutPercent is null || form.ExpectedPackoutUsedDefault;
        if (projection.Sources.Any(x => string.Equals(x.SourceType, source.Entity.SourceType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.InventoryKey, source.Entity.InventoryKey, StringComparison.OrdinalIgnoreCase)
            && x.FieldSampleId == source.Entity.FieldSampleId))
        {
            return "That source is already part of this projection.";
        }

        source.Entity.SortOrder = projection.Sources.Count == 0 ? 1 : projection.Sources.Max(x => x.SortOrder) + 1;
        projection.Sources.Add(source.Entity);
        RecalculateTotals(projection);
        RecalculatePackAllocationFromSavedSnapshot(projection);
        Touch(projection, userId);
        await AddAuditAsync("AddSource", projection, userId, null, SourceSnapshot(source.Entity), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> UpdateSourceAsync(RunProjectionUpdateSourceForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == form.SourceId);
        if (source is null) return "Projection source was not found.";
        if (source.ActualBinsRunEntryId is not null) return "A source linked to an actual run cannot be changed.";
        if (form.PlannedBins <= 0) return "Planned bins must be greater than zero.";
        if (form.ExpectedPackoutPercent is < 0 or > 100) return "Expected Packout % must be between 0 and 100.";
        var settings = await LoadSettingsAsync(cancellationToken);

        var before = SourceSnapshot(source);
        if (source.ExpectedPackoutPercent != form.ExpectedPackoutPercent)
        {
            source.ExpectedPackoutUsedDefault = false;
        }
        source.PlannedBins = form.PlannedBins;
        source.AvailabilityOverrideAcknowledged = form.AvailabilityOverrideAcknowledged;
        source.SortOrder = Math.Max(0, form.SortOrder);
        source.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        if (source.AvailableBinsSnapshot is int available && source.PlannedBins > available && !source.AvailabilityOverrideAcknowledged)
        {
            return $"Planned bins exceed the saved available quantity of {available}. Confirm the planning override to continue.";
        }

        var requestedChoice = string.IsNullOrWhiteSpace(form.SelectedQcSource)
            ? RunProjectionQcSourceTypes.Automatic
            : form.SelectedQcSource.Trim();
        if (projection.ProjectionMode == RunProjectionModes.Preharvest
            && !requestedChoice.StartsWith("FieldSample:", StringComparison.OrdinalIgnoreCase))
        {
            return "A Preharvest source requires a specific confirmed Field Sample.";
        }
        var selectionChanged = !ChoiceMatchesSnapshot(source, requestedChoice);
        source.ExpectedPackoutPercent = form.ExpectedPackoutPercent;
        if (selectionChanged)
        {
            var qc = await ResolveQcSampleAsync(projection.CropYear, source, requestedChoice, cancellationToken);
            if (qc.Error is not null) return qc.Error;
            await ApplyQcAndCalculationAsync(projection, source, qc.Sample, qc.SourceType, settings.MinimumDistributionFruit, cancellationToken);
        }
        else
        {
            RecalculateFromSnapshot(projection, source, settings.MinimumDistributionFruit);
        }
        source.UpdatedAt = businessTime.UtcNow;
        RecalculateTotals(projection);
        RecalculatePackAllocationFromSavedSnapshot(projection);
        Touch(projection, userId);
        await AddAuditAsync("UpdateSource", projection, userId, before, SourceSnapshot(source), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ApplyPackoutToAllAsync(
        RunProjectionApplyPackoutForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (form.ExpectedPackoutPercent is < 0 or > 100) return "Expected Packout % must be between 0 and 100.";
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (projection.FacilityWarehouseId is null) return "Assign WP or EBS before adding projection sources.";
        var before = projection.Sources.Select(x => new { x.Id, x.ExpectedPackoutPercent }).ToList();
        var settings = await LoadSettingsAsync(cancellationToken);
        foreach (var source in projection.Sources.Where(x => x.ActualBinsRunEntryId is null))
        {
            source.ExpectedPackoutPercent = decimal.Round(form.ExpectedPackoutPercent, 2);
            source.ExpectedPackoutUsedDefault = false;
            RecalculateFromSnapshot(projection, source, settings.MinimumDistributionFruit);
            source.UpdatedAt = businessTime.UtcNow;
        }
        RecalculateTotals(projection);
        RecalculatePackAllocationFromSavedSnapshot(projection);
        Touch(projection, userId);
        await AddAuditAsync(
            "ApplyPackoutToAll",
            projection,
            userId,
            before,
            new { form.ExpectedPackoutPercent, Sources = projection.Sources.Select(x => new { x.Id, x.ExpectedPackoutPercent }) },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(RunProjectionPackPlanPreviewViewModel? Preview, string? Error)> PreviewPackPlanAsync(
        RunProjectionPackPlanForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, _) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return (null, error);
        if (projection.Sources.Count == 0) return (null, "Add projection sources before selecting a commercial pack plan.");
        var plan = await BuildPackPlanSnapshotAsync(form.CommercialPackPlanId, projection.CropYear, cancellationToken);
        if (plan is null) return (null, "The selected active commercial pack plan is unavailable for this crop year.");
        if (!PlanMatchesProjection(plan, projection))
        {
            return (null, "The selected commercial pack plan does not cover every commodity in this projection.");
        }
        var configurationJson = JsonSerializer.Serialize(plan);
        var proposed = CalculatePackAllocation(projection, plan);
        var current = DeserializePackAllocation(projection.PackAllocationSnapshotJson);
        return (new RunProjectionPackPlanPreviewViewModel
        {
            ProjectionId = projection.Id,
            ProjectionName = projection.Name,
            PlannedRunDate = projection.PlannedRunDate,
            ConcurrencyVersion = projection.ConcurrencyVersion,
            CommercialPackPlanId = plan.PlanId,
            ProposedPlanName = plan.DisplayName,
            ProposedPlanType = plan.PlanType,
            ConfigurationHash = ConfigurationHash(configurationJson),
            CurrentPacks = MapPacks(current?.Packs ?? []),
            ProposedPacks = MapPacks(proposed.Packs),
            CurrentUnallocated = MapUnallocated(current?.Unallocated ?? []),
            ProposedUnallocated = MapUnallocated(proposed.Unallocated),
            ProposedWarnings = proposed.Warnings,
            CurrentAssignedPounds = current?.TotalAssignedPounds ?? 0m,
            ProposedAssignedPounds = proposed.TotalAssignedPounds
        }, null);
    }

    public async Task<string?> ApplyPackPlanAsync(
        RunProjectionPackPlanForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.ProjectionId, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (projection.Sources.Count == 0) return "Add projection sources before selecting a commercial pack plan.";
        var plan = await BuildPackPlanSnapshotAsync(form.CommercialPackPlanId, projection.CropYear, cancellationToken);
        if (plan is null) return "The selected active commercial pack plan is unavailable for this crop year.";
        if (!PlanMatchesProjection(plan, projection))
        {
            return "The selected commercial pack plan does not cover every commodity in this projection.";
        }
        var configurationJson = JsonSerializer.Serialize(plan);
        if (string.IsNullOrWhiteSpace(form.ConfigurationHash)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(form.ConfigurationHash),
                Encoding.UTF8.GetBytes(ConfigurationHash(configurationJson))))
        {
            return "Commercial pack configuration changed after preview. Preview the plan again before applying it.";
        }
        var before = new
        {
            projection.CommercialPackPlanId,
            projection.PackPlanCodeSnapshot,
            projection.PackPlanNameSnapshot,
            projection.PackCalculationVersion,
            projection.PackAllocationSnapshotJson
        };
        projection.CommercialPackPlanId = plan.PlanId;
        projection.PackPlanCodeSnapshot = plan.Code;
        projection.PackPlanNameSnapshot = plan.DisplayName;
        projection.PackPlanTypeSnapshot = plan.PlanType;
        projection.PackConfigurationSnapshotJson = configurationJson;
        RecalculatePackAllocationFromSavedSnapshot(projection);
        Touch(projection, userId);
        await AddAuditAsync(
            "ApplyCommercialPackPlan",
            projection,
            userId,
            before,
            new
            {
                projection.CommercialPackPlanId,
                projection.PackPlanCodeSnapshot,
                projection.PackPlanNameSnapshot,
                projection.PackPlanTypeSnapshot,
                projection.PackCalculationVersion,
                projection.PackCalculatedAt
            },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> RefreshSourceAsync(
        long projectionId,
        long sourceId,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(projectionId, concurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == sourceId);
        if (source is null) return "Projection source was not found.";
        if (source.SelectedQcSampleId is null) return "Select a QC sample before refreshing.";
        var before = SourceSnapshot(source);
        var settings = await LoadSettingsAsync(cancellationToken);
        var choice = source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample
            ? $"FieldSample:{source.SelectedQcSampleId}"
            : $"ReceiptQc:{source.SelectedQcSampleId}";
        var qc = await ResolveQcSampleAsync(projection.CropYear, source, choice, cancellationToken);
        if (qc.Error is not null || qc.Sample is null) return qc.Error ?? "The selected QC sample is no longer available.";
        await ApplyQcAndCalculationAsync(projection, source, qc.Sample, qc.SourceType, settings.MinimumDistributionFruit, cancellationToken);
        source.UpdatedAt = businessTime.UtcNow;
        RecalculateTotals(projection);
        RecalculatePackAllocationFromSavedSnapshot(projection);
        Touch(projection, userId);
        await AddAuditAsync("RefreshQcSnapshot", projection, userId, before, SourceSnapshot(source), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> RemoveSourceAsync(
        long projectionId,
        long sourceId,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(projectionId, concurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        var source = projection.Sources.SingleOrDefault(x => x.Id == sourceId);
        if (source is null) return "Projection source was not found.";
        if (source.ActualBinsRunEntryId is not null) return "A source linked to an actual run cannot be removed.";
        var before = SourceSnapshot(source);
        dbContext.RunProjectionSources.Remove(source);
        RecalculateTotals(projection, sourceId);
        RecalculatePackAllocationFromSavedSnapshot(projection, sourceId);
        Touch(projection, userId);
        await AddAuditAsync("RemoveSource", projection, userId, before, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> MarkReadyAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var (projection, error, userId) = await LoadForEditAsync(form.Id, form.ConcurrencyVersion, user, cancellationToken);
        if (error is not null || projection is null) return error;
        if (projection.FacilityWarehouseId is null) return "Select WP or EBS before marking the projection Ready.";
        if (projection.Sources.Count == 0) return "Add at least one source before marking the projection Ready.";
        if (projection.Sources.Any(x => x.PlannedBins <= 0)) return "Every source needs an estimated or planned bin quantity greater than zero.";
        if (projection.ProjectionMode == RunProjectionModes.Preharvest
            && projection.Sources.Any(x =>
                x.SourceType != RunProjectionSourceTypes.FieldSample
                || x.CanonicalOrchardBlockId is null
                || x.FieldSampleId is null
                || x.SelectedQcSampleId is null))
        {
            return "Every Preharvest source needs a confirmed block, variety, and usable Field Sample.";
        }
        if (projection.ProjectionMode == RunProjectionModes.Inventory
            && projection.Sources.Any(x => x.SourceType != RunProjectionSourceTypes.Inventory
                || string.IsNullOrWhiteSpace(x.InventoryKey)
                || x.WarehouseId != projection.FacilityWarehouseId))
        {
            return "Every Inventory projection source must be mapped to a real receipt or inventory lot at the projection's assigned facility.";
        }
        if (projection.Sources.Any(x => x.Commodity == "Unknown")) return "Resolve every source commodity before marking the projection Ready.";
        if (projection.Sources.Any(x => x.SizeResults.Count == 0)) return "Every source needs usable calculated size data before the projection can be Ready.";
        if (projection.Sources.Any(x => x.ExpectedPackoutPercent is null))
        {
            await AddAuditAsync(
                "ReadyBlockedMissingPackout",
                projection,
                userId,
                new { projection.Status },
                new { MissingSourceIds = projection.Sources.Where(x => x.ExpectedPackoutPercent is null).Select(x => x.Id) },
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return "Expected Packout % is required for every source before marking the projection Ready.";
        }
        if (projection.CommercialPackPlanId is null || string.IsNullOrWhiteSpace(projection.PackConfigurationSnapshotJson))
        {
            return "Select, preview, and apply a commercial pack plan before marking the projection Ready.";
        }
        var packAllocation = DeserializePackAllocation(projection.PackAllocationSnapshotJson);
        if (packAllocation is null) return "Recalculate the commercial pack plan before marking the projection Ready.";
        if (packAllocation.Warnings.Count > 0)
        {
            return $"Resolve commercial pack configuration warnings before marking Ready: {string.Join(" ", packAllocation.Warnings)}";
        }
        var before = projection.Status;
        projection.Status = RunProjectionStatuses.Ready;
        Touch(projection, userId);
        await AddAuditAsync("MarkReady", projection, userId, new { Status = before }, new { projection.Status }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> CancelAsync(RunProjectionStatusForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken)) return "Bins Run Edit access is required to cancel projections.";
        if (string.IsNullOrWhiteSpace(form.Reason)) return "Cancellation reason is required.";
        var projection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (projection is null) return "Projection was not found.";
        if (projection.IsDeleted) return "Deleted projections are read-only.";
        if (projection.ConcurrencyVersion != form.ConcurrencyVersion) return ConflictMessage;
        if (!RunProjectionStatuses.Editable.Contains(projection.Status, StringComparer.OrdinalIgnoreCase))
        {
            return $"A {projection.Status} projection cannot be cancelled.";
        }
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var before = projection.Status;
        projection.Status = RunProjectionStatuses.Cancelled;
        projection.CancelReason = form.Reason.Trim();
        projection.CancelledAt = businessTime.UtcNow;
        projection.CancelledByUserId = userId;
        Touch(projection, userId);
        await AddAuditAsync("Cancel", projection, userId, new { Status = before }, new { projection.Status, projection.CancelReason }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(long? Id, string? Error)> DuplicateAsync(
        RunProjectionDuplicateForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to duplicate projections.");
        }

        var sourceProjection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (sourceProjection is null) return (null, "Projection was not found.");
        if (sourceProjection.IsDeleted) return (null, "Deleted projections cannot be duplicated.");
        var targetFacility = form.FacilityWarehouseId is int targetFacilityId
            ? await OperationalFacilities().SingleOrDefaultAsync(x => x.Id == targetFacilityId, cancellationToken)
            : null;
        if (targetFacility is null) return (null, "Select WP or EBS for the duplicate.");
        var cloneName = string.IsNullOrWhiteSpace(form.Name)
            ? $"{sourceProjection.Name[..Math.Min(sourceProjection.Name.Length, 95)]} copy"
            : form.Name.Trim();
        if (cloneName.Length > 100) return (null, "Projection name must be 100 characters or fewer.");
        var now = businessTime.UtcNow;
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var clone = new RunProjection
        {
            PlannedRunDate = form.PlannedRunDate,
            Name = cloneName,
            Status = RunProjectionStatuses.Draft,
            ProjectionMode = sourceProjection.ProjectionMode,
            CropYear = sourceProjection.CropYear,
            SourceProjectionId = sourceProjection.Id,
            FacilityWarehouseId = targetFacility.Id,
            FacilityCodeSnapshot = targetFacility.Code,
            CommercialPackPlanId = sourceProjection.CommercialPackPlanId,
            PackPlanCodeSnapshot = sourceProjection.PackPlanCodeSnapshot,
            PackPlanNameSnapshot = sourceProjection.PackPlanNameSnapshot,
            PackPlanTypeSnapshot = sourceProjection.PackPlanTypeSnapshot,
            PackConfigurationSnapshotJson = sourceProjection.PackConfigurationSnapshotJson,
            ApplePoundsPerBin = sourceProjection.ApplePoundsPerBin,
            PearPoundsPerBin = sourceProjection.PearPoundsPerBin,
            StandardBoxWeightPounds = sourceProjection.StandardBoxWeightPounds,
            TotalPlannedBins = sourceProjection.TotalPlannedBins,
            TotalProjectedPounds = sourceProjection.TotalProjectedPounds,
            TotalProjectedBoxes = sourceProjection.TotalProjectedBoxes,
            TotalRoundedProjectedBoxes = sourceProjection.TotalRoundedProjectedBoxes,
            TotalPackedProjectedPounds = sourceProjection.TotalPackedProjectedPounds,
            TotalPackedProjectedBoxes = sourceProjection.TotalPackedProjectedBoxes,
            TotalRoundedPackedProjectedBoxes = sourceProjection.TotalRoundedPackedProjectedBoxes,
            TotalCullProjectedPounds = sourceProjection.TotalCullProjectedPounds,
            TotalCullProjectedBoxes = sourceProjection.TotalCullProjectedBoxes,
            TotalRoundedCullProjectedBoxes = sourceProjection.TotalRoundedCullProjectedBoxes,
            ExpiresAt = now.AddDays((await LoadSettingsAsync(cancellationToken)).DraftExpirationDays),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        var crossFacility = sourceProjection.FacilityWarehouseId != targetFacility.Id;
        var omittedSourceIds = new List<long>();
        foreach (var source in sourceProjection.Sources.OrderBy(x => x.SortOrder))
        {
            if (crossFacility && source.SourceType == RunProjectionSourceTypes.Inventory)
            {
                omittedSourceIds.Add(source.Id);
                continue;
            }
            clone.Sources.Add(CloneSource(source, now));
        }
        RecalculateTotals(clone);

        dbContext.RunProjections.Add(clone);
        await dbContext.SaveChangesAsync(cancellationToken);
        RecalculatePackAllocationFromSavedSnapshot(clone);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(
            "Duplicate",
            clone,
            userId,
            new { SourceProjectionId = sourceProjection.Id, SourceFacility = sourceProjection.FacilityCodeSnapshot },
            new { Projection = ProjectionSnapshot(clone), CrossFacility = crossFacility, OmittedInventorySourceIds = omittedSourceIds },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (clone.Id, null);
    }

    public async Task<(long? Id, string? Error)> CreateInventoryFromPreharvestAsync(
        RunProjectionCreateInventoryForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to create an inventory projection.");
        }

        var sourceProjection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (sourceProjection is null) return (null, "Preharvest projection was not found.");
        if (sourceProjection.IsDeleted) return (null, "Deleted projections cannot create inventory projections.");
        if (sourceProjection.FacilityWarehouseId is null) return (null, "Assign WP or EBS before mapping inventory.");
        if (form.FacilityWarehouseId != sourceProjection.FacilityWarehouseId)
        {
            return (null, "The mapped Inventory projection must use the same planned facility as the Preharvest projection.");
        }
        if (sourceProjection.ProjectionMode != RunProjectionModes.Preharvest)
        {
            return (null, "Only a Preharvest projection can create a mapped Inventory projection.");
        }
        if (!RunProjectionStatuses.Editable.Contains(sourceProjection.Status))
        {
            return (null, $"A {sourceProjection.Status} Preharvest projection cannot create another Inventory projection.");
        }
        if (sourceProjection.ConcurrencyVersion != form.ConcurrencyVersion) return (null, ConflictMessage);
        if (sourceProjection.Sources.Count == 0) return (null, "Add at least one Preharvest source before mapping inventory.");
        if (form.Mappings.Count != sourceProjection.Sources.Count
            || form.Mappings.Select(x => x.PreharvestSourceId).Distinct().Count() != sourceProjection.Sources.Count)
        {
            return (null, "Map every Preharvest block to exactly one real inventory lot.");
        }

        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
        {
            return (null, "Projection name is required and must be 100 characters or fewer.");
        }

        var sourceBeforeMapping = ProjectionSnapshot(sourceProjection);
        var now = businessTime.UtcNow;
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        var settings = await LoadSettingsAsync(cancellationToken);
        var inventoryProjection = new RunProjection
        {
            PlannedRunDate = form.PlannedRunDate,
            Name = name,
            Status = RunProjectionStatuses.Draft,
            ProjectionMode = RunProjectionModes.Inventory,
            CropYear = sourceProjection.CropYear,
            SourceProjectionId = sourceProjection.Id,
            FacilityWarehouseId = sourceProjection.FacilityWarehouseId,
            FacilityCodeSnapshot = sourceProjection.FacilityCodeSnapshot,
            CommercialPackPlanId = sourceProjection.CommercialPackPlanId,
            PackPlanCodeSnapshot = sourceProjection.PackPlanCodeSnapshot,
            PackPlanNameSnapshot = sourceProjection.PackPlanNameSnapshot,
            PackPlanTypeSnapshot = sourceProjection.PackPlanTypeSnapshot,
            PackConfigurationSnapshotJson = sourceProjection.PackConfigurationSnapshotJson,
            ApplePoundsPerBin = sourceProjection.ApplePoundsPerBin,
            PearPoundsPerBin = sourceProjection.PearPoundsPerBin,
            StandardBoxWeightPounds = sourceProjection.StandardBoxWeightPounds,
            ExpiresAt = now.AddDays(settings.DraftExpirationDays),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };

        foreach (var preharvestSource in sourceProjection.Sources.OrderBy(x => x.SortOrder))
        {
            var mapping = form.Mappings.SingleOrDefault(x => x.PreharvestSourceId == preharvestSource.Id);
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.InventoryKey))
            {
                return (null, $"Map {preharvestSource.SourceLabelSnapshot} to a real inventory lot.");
            }
            if (preharvestSource.SelectedQcSampleId is null)
            {
                return (null, $"{preharvestSource.SourceLabelSnapshot} needs a usable Field Sample before inventory mapping.");
            }
            var inventory = await binsRunService.GetPlanningInventoryAsync(mapping.InventoryKey, cancellationToken);
            if (inventory is null)
            {
                return (null, $"The mapped inventory for {preharvestSource.SourceLabelSnapshot} is no longer available.");
            }
            if (inventory.WarehouseId != sourceProjection.FacilityWarehouseId)
            {
                return (null, $"The mapped inventory for {preharvestSource.SourceLabelSnapshot} must belong to {sourceProjection.FacilityCodeSnapshot}.");
            }
            if (inventory.CanonicalOrchardBlockId != preharvestSource.CanonicalOrchardBlockId
                || inventory.FruitProfileId != preharvestSource.FruitProfileId)
            {
                return (null, $"The mapped inventory for {preharvestSource.SourceLabelSnapshot} must use the same confirmed block and variety.");
            }

            var resolved = await ResolveSourceAsync(
                inventoryProjection,
                inventory.InventoryKey,
                $"FieldSample:{preharvestSource.SelectedQcSampleId}",
                preharvestSource.PlannedBins,
                preharvestSource.ExpectedPackoutPercent,
                settings.MinimumDistributionFruit,
                mapping.AvailabilityOverrideAcknowledged,
                cancellationToken);
            if (resolved.Error is not null || resolved.Entity is null)
            {
                return (null, resolved.Error ?? "The mapped inventory source could not be resolved.");
            }
            resolved.Entity.SourceProjectionSourceId = preharvestSource.Id;
            resolved.Entity.ExpectedPackoutUsedDefault = preharvestSource.ExpectedPackoutUsedDefault;
            resolved.Entity.SortOrder = preharvestSource.SortOrder;
            resolved.Entity.Notes = preharvestSource.Notes;
            inventoryProjection.Sources.Add(resolved.Entity);
        }

        RecalculateTotals(inventoryProjection);
        sourceProjection.Status = RunProjectionStatuses.Superseded;
        Touch(sourceProjection, userId);
        dbContext.RunProjections.Add(inventoryProjection);
        await dbContext.SaveChangesAsync(cancellationToken);
        RecalculatePackAllocationFromSavedSnapshot(inventoryProjection);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(
            "CreateInventoryProjection",
            sourceProjection,
            userId,
            sourceBeforeMapping,
            new
            {
                sourceProjection.Status,
                InventoryProjectionId = inventoryProjection.Id,
                MappedSourceIds = inventoryProjection.Sources.Select(x => x.Id)
            },
            cancellationToken);
        await AddAuditAsync(
            "CreateFromPreharvest",
            inventoryProjection,
            userId,
            new { SourceProjectionId = sourceProjection.Id },
            ProjectionSnapshot(inventoryProjection),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (inventoryProjection.Id, null);
    }

    public async Task<RunProjectionDeletionConfirmationViewModel?> GetDeletionConfirmationAsync(
        long id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RequireAsync(user, PageAccessLevel.Edit, cancellationToken);
        var canAdmin = await CanAsync(user, PageAccessLevel.Admin, cancellationToken);
        var projection = await dbContext.RunProjections.AsNoTracking()
            .Include(x => x.FacilityWarehouse)
            .Include(x => x.CreatedByUser)
            .Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (projection is null) return null;

        var linkedActualIds = projection.Sources
            .Where(x => x.ActualBinsRunEntryId is not null)
            .Select(x => x.ActualBinsRunEntryId!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        string? blockingReason = null;
        if (projection.Status == RunProjectionStatuses.Converted || linkedActualIds.Count > 0)
        {
            blockingReason = "This projection is linked to an actual Bins Run and cannot be deleted. Actual operational history must be preserved.";
        }
        else if (!RunProjectionStatuses.Editable.Contains(projection.Status) && !canAdmin)
        {
            blockingReason = $"Bins Run Admin access is required to delete a {projection.Status} projection.";
        }

        return new RunProjectionDeletionConfirmationViewModel
        {
            Id = projection.Id,
            Name = projection.Name,
            FacilityCode = projection.FacilityWarehouse?.Code ?? projection.FacilityCodeSnapshot ?? "Unassigned",
            PlannedRunDate = projection.PlannedRunDate,
            Status = projection.Status,
            ProjectionMode = projection.ProjectionMode,
            SourceCount = projection.Sources.Count,
            TotalPlannedBins = projection.TotalPlannedBins,
            TotalProjectedBoxes = projection.TotalProjectedBoxes,
            LinkedActualRunIds = linkedActualIds,
            Creator = projection.CreatedByUser?.DisplayName ?? "",
            UpdatedAt = projection.UpdatedAt,
            BlockingReason = blockingReason,
            Form = new DeleteRunProjectionForm
            {
                Id = projection.Id,
                ConcurrencyVersion = projection.ConcurrencyVersion,
                OperationToken = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)
            }
        };
    }

    public async Task<string?> DeleteAsync(
        DeleteRunProjectionForm form,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return "Bins Run Edit access is required to delete projections.";
        }
        if (!Guid.TryParse(form.OperationToken, out var operationId))
        {
            return "The deletion confirmation expired. Reopen the projection deletion page.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Trim().Length < 10)
        {
            return "A detailed deletion reason of at least 10 characters is required.";
        }
        if (!form.ConfirmDeletion)
        {
            return "Select the second confirmation before deleting the projection.";
        }

        var projection = await LoadProjectionAsync(form.Id, cancellationToken);
        if (projection is null) return "Projection was not found.";
        if (projection.IsDeleted)
        {
            return projection.DeletionOperationId == operationId
                ? null
                : "Projection was already deleted.";
        }
        var confirmation = form.ConfirmationValue.Trim();
        if (confirmation != projection.Id.ToString(CultureInfo.InvariantCulture)
            && !confirmation.Equals(projection.Name, StringComparison.OrdinalIgnoreCase))
        {
            return $"Type the exact projection ID {projection.Id} or name \"{projection.Name}\" to confirm deletion.";
        }
        if (projection.ConcurrencyVersion != form.ConcurrencyVersion) return ConflictMessage;

        var linkedActualIds = projection.Sources
            .Where(x => x.ActualBinsRunEntryId is not null)
            .Select(x => x.ActualBinsRunEntryId!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        var userId = await CurrentUserIdAsync(user, cancellationToken);
        if (projection.Status == RunProjectionStatuses.Converted || linkedActualIds.Count > 0)
        {
            await AddAuditAsync(
                "DeleteBlockedActualRun",
                projection,
                userId,
                ProjectionSnapshot(projection),
                new { Result = "Blocked", LinkedActualRunIds = linkedActualIds, OperationId = operationId },
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return "This projection is linked to an actual Bins Run and cannot be deleted.";
        }
        var canAdmin = await CanAsync(user, PageAccessLevel.Admin, cancellationToken);
        if (!RunProjectionStatuses.Editable.Contains(projection.Status) && !canAdmin)
        {
            return $"Bins Run Admin access is required to delete a {projection.Status} projection.";
        }

        var now = businessTime.UtcNow;
        var before = ProjectionSnapshot(projection);
        projection.IsDeleted = true;
        projection.DeletedAt = now;
        projection.DeletedByUserId = userId;
        projection.DeletionReason = form.Reason.Trim();
        projection.DeletionOperationId = operationId;
        projection.DeletedFromStatus = projection.Status;
        Touch(projection, userId);
        await AddAuditAsync(
            "Delete",
            projection,
            userId,
            before,
            new
            {
                Projection = ProjectionSnapshot(projection),
                projection.DeletedAt,
                DeletedAtPacific = businessTime.FormatPacific(now, "O"),
                projection.DeletionReason,
                projection.DeletionOperationId,
                Facility = projection.FacilityWarehouse?.Code ?? projection.FacilityCodeSnapshot ?? "Unassigned",
                SourceCount = projection.Sources.Count,
                projection.TotalPlannedBins,
                LinkedActualRunIds = linkedActualIds,
                Result = "SoftDeleted"
            },
            cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictMessage;
        }
        return null;
    }

    private async Task<RunProjectionDetailViewModel?> GetDetailAsync(long id, bool canEdit, bool canAdmin, CancellationToken cancellationToken)
    {
        var projection = await LoadProjectionAsync(id, cancellationToken);
        if (projection is null) return null;
        var sourceModels = new List<RunProjectionSourceViewModel>();
        foreach (var source in projection.Sources.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            var choices = await GetQcChoicesAsync(source, projection.CropYear, cancellationToken);
            IReadOnlyList<RunProjectionInventoryMappingChoiceViewModel> mappingChoices = [];
            if (projection.ProjectionMode == RunProjectionModes.Preharvest
                && source.CanonicalOrchardBlockId is not null)
            {
                var inventoryMatches = await binsRunService.SearchPlanningInventoryAsync(
                    source.BlockSnapshot ?? source.OrchardSnapshot,
                    projection.FacilityWarehouseId,
                    null,
                    100,
                    cancellationToken);
                mappingChoices = inventoryMatches
                    .Where(x => x.CanonicalOrchardBlockId == source.CanonicalOrchardBlockId
                        && x.FruitProfileId == source.FruitProfileId)
                    .OrderBy(x => x.Facility)
                    .ThenBy(x => x.Room)
                    .ThenBy(x => x.Lot)
                    .Select(x => new RunProjectionInventoryMappingChoiceViewModel(
                        x.InventoryKey,
                        $"{x.Facility} / {x.Room} — {x.Grower} — lot {x.Lot} — {x.Variety} — {x.CurrentBins} bins available",
                        x.CurrentBins))
                    .ToList();
            }
            sourceModels.Add(new RunProjectionSourceViewModel
            {
                Id = source.Id,
                SourceType = source.SourceType,
                InventoryKey = source.InventoryKey,
                CanonicalOrchardBlockId = source.CanonicalOrchardBlockId,
                FruitProfileId = source.FruitProfileId,
                WarehouseId = source.WarehouseId,
                RoomId = source.RoomId,
                SourceLabel = source.SourceLabelSnapshot,
                Facility = source.FacilitySnapshot,
                Room = source.RoomSnapshot,
                Lot = source.LotSnapshot,
                Orchard = source.OrchardSnapshot,
                Grower = source.GrowerSnapshot,
                GrowerNumber = source.GrowerNumberSnapshot,
                Block = source.BlockSnapshot,
                Variety = source.VarietySnapshot,
                Commodity = source.Commodity,
                PlannedBins = source.PlannedBins,
                AvailableBinsSnapshot = source.AvailableBinsSnapshot,
                AvailabilityOverrideAcknowledged = source.AvailabilityOverrideAcknowledged,
                SortOrder = source.SortOrder,
                Notes = source.Notes,
                SelectedQcSourceType = source.SelectedQcSourceType,
                SelectedQcSampleId = source.SelectedQcSampleId,
                QcBasis = QcBasis(source),
                QcSampleDate = source.QcSampleDateSnapshot,
                QcSampleType = source.QcSampleTypeSnapshot,
                QcSampleStatus = source.QcSampleStatusSnapshot,
                QcSampleId = source.SelectedQcSampleId,
                QcFruitCount = source.QcFruitCountSnapshot,
                SizeBasisFruitCount = source.SizeBasisFruitCount,
                GradeBasisFruitCount = source.GradeBasisFruitCount,
                JointSizeGradeBasisFruitCount = source.JointSizeGradeBasisFruitCount,
                AverageWeightGrams = source.AverageWeightGramsSnapshot,
                AveragePressureLbs = source.AveragePressureLbsSnapshot,
                GradeSummary = source.GradeSummarySnapshot,
                DefectSummary = source.DefectSummarySnapshot,
                FieldSampleTrendSnapshotJson = source.FieldSampleTrendSnapshotJson,
                PoundsPerBin = source.PoundsPerBinUsed,
                ProjectedPounds = source.ProjectedPounds,
                ProjectedBoxes = source.ProjectedBoxes,
                RoundedProjectedBoxes = source.RoundedProjectedBoxes,
                ExpectedPackoutPercent = source.ExpectedPackoutPercent,
                ExpectedCullPercent = source.ExpectedCullPercent,
                ExpectedPackoutUsedDefault = source.ExpectedPackoutUsedDefault,
                PackedProjectedPounds = source.PackedProjectedPounds,
                PackedProjectedBoxes = source.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = source.RoundedPackedProjectedBoxes,
                CullProjectedPounds = source.CullProjectedPounds,
                CullProjectedBoxes = source.CullProjectedBoxes,
                RoundedCullProjectedBoxes = source.RoundedCullProjectedBoxes,
                CalculationVersion = source.CalculationVersion,
                Warning = source.ProjectionWarning,
                ActualBinsRunEntryId = source.ActualBinsRunEntryId,
                SizeResults = source.SizeResults.OrderBy(x => x.SizeCategory)
                    .Select(x => new RunProjectionSizeResultViewModel(
                        x.Commodity, x.SizeCategory, x.SampleCount, x.Percentage,
                        x.UnroundedProjectedBoxes, x.RoundedProjectedBoxes,
                        x.PackedProjectedBoxes, x.RoundedPackedProjectedBoxes,
                        x.CullProjectedBoxes, x.RoundedCullProjectedBoxes))
                    .ToList(),
                GradeResults = source.GradeResults.OrderBy(x => x.GradeCode)
                    .Select(x => new RunProjectionGradeResultViewModel(
                        x.GradeCode, x.SampleCount, x.Percentage,
                        x.GrossProjectedBoxes, x.RoundedGrossProjectedBoxes,
                        x.PackedProjectedBoxes, x.RoundedPackedProjectedBoxes,
                        x.CullProjectedBoxes, x.RoundedCullProjectedBoxes))
                    .ToList(),
                QcChoices = choices,
                InventoryMappingChoices = mappingChoices
            });
        }

        var sourceCommodities = sourceModels.Select(x => x.Commodity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var packPlanRows = await dbContext.CommercialPackPlans.AsNoTracking()
            .Where(x => x.IsActive
                && (x.EffectiveCropYearStart == null || x.EffectiveCropYearStart <= projection.CropYear)
                && (x.EffectiveCropYearEnd == null || x.EffectiveCropYearEnd >= projection.CropYear))
            .OrderBy(x => x.Commodity)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var packPlanOptions = packPlanRows
            .Where(x => sourceCommodities.Count == 0
                || x.Commodity.Equals("All", StringComparison.OrdinalIgnoreCase)
                || sourceCommodities.Contains(x.Commodity, StringComparer.OrdinalIgnoreCase))
            .Select(x => new RunProjectionPackPlanOptionViewModel(
                x.Id,
                x.Code,
                x.DisplayName,
                x.Commodity,
                x.PlanType))
            .ToList();
        var packAllocation = DeserializePackAllocation(projection.PackAllocationSnapshotJson);
        var mappedPacks = MapPacks(packAllocation?.Packs ?? []);

        return new RunProjectionDetailViewModel
        {
            Id = projection.Id,
            PlannedRunDate = projection.PlannedRunDate,
            Name = projection.Name,
            Status = projection.Status,
            ProjectionMode = projection.ProjectionMode,
            FacilityWarehouseId = projection.FacilityWarehouseId,
            FacilityCode = projection.FacilityWarehouse?.Code ?? projection.FacilityCodeSnapshot ?? "Unassigned",
            CropYear = projection.CropYear,
            SourceProjectionId = projection.SourceProjectionId,
            ApplePoundsPerBin = projection.ApplePoundsPerBin,
            PearPoundsPerBin = projection.PearPoundsPerBin,
            StandardBoxWeightPounds = projection.StandardBoxWeightPounds,
            TotalPlannedBins = projection.TotalPlannedBins,
            TotalProjectedPounds = projection.TotalProjectedPounds,
            TotalProjectedBoxes = projection.TotalProjectedBoxes,
            TotalRoundedProjectedBoxes = projection.TotalRoundedProjectedBoxes,
            TotalPackedProjectedPounds = projection.TotalPackedProjectedPounds,
            TotalPackedProjectedBoxes = projection.TotalPackedProjectedBoxes,
            TotalRoundedPackedProjectedBoxes = projection.TotalRoundedPackedProjectedBoxes,
            TotalCullProjectedPounds = projection.TotalCullProjectedPounds,
            TotalCullProjectedBoxes = projection.TotalCullProjectedBoxes,
            TotalRoundedCullProjectedBoxes = projection.TotalRoundedCullProjectedBoxes,
            ConcurrencyVersion = projection.ConcurrencyVersion,
            Creator = projection.CreatedByUser?.DisplayName ?? "",
            UpdatedAt = projection.UpdatedAt,
            SourceCount = projection.Sources.Count,
            ConvertedSourceCount = projection.Sources.Count(x => x.ActualBinsRunEntryId != null),
            CancelReason = projection.CancelReason,
            IsDeleted = projection.IsDeleted,
            DeletedAt = projection.DeletedAt,
            DeletionReason = projection.DeletionReason,
            DeletedFromStatus = projection.DeletedFromStatus,
            DeletionOperationId = projection.DeletionOperationId,
            Sources = sourceModels,
            CombinedSizes = sourceModels
                .SelectMany(x => x.SizeResults)
                .GroupBy(x => new { x.Commodity, x.Size })
                .OrderBy(x => x.Key.Commodity)
                .ThenBy(x => x.Key.Size)
                .Select(x => new RunProjectionCombinedSizeViewModel(
                    x.Key.Commodity,
                    x.Key.Size,
                    x.Sum(y => y.UnroundedBoxes),
                    x.Sum(y => y.RoundedBoxes),
                    x.Sum(y => y.PackedBoxes),
                    x.Sum(y => y.RoundedPackedBoxes),
                    x.Sum(y => y.CullBoxes),
                    x.Sum(y => y.RoundedCullBoxes)))
                .ToList(),
            CombinedGrades = sourceModels
                .SelectMany(x => x.GradeResults)
                .GroupBy(x => x.Grade, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new RunProjectionCombinedGradeViewModel(
                    x.Key,
                    x.Sum(y => y.GrossBoxes),
                    x.Sum(y => y.RoundedGrossBoxes),
                    x.Sum(y => y.PackedBoxes),
                    x.Sum(y => y.RoundedPackedBoxes),
                    x.Sum(y => y.CullBoxes),
                    x.Sum(y => y.RoundedCullBoxes)))
                .ToList(),
            CommercialPackPlanId = projection.CommercialPackPlanId,
            PackPlanCode = projection.PackPlanCodeSnapshot,
            PackPlanName = projection.PackPlanNameSnapshot,
            PackPlanType = projection.PackPlanTypeSnapshot,
            PackCalculationVersion = projection.PackCalculationVersion,
            PackCalculatedAt = projection.PackCalculatedAt,
            PackPlanOptions = packPlanOptions,
            PackResults = mappedPacks,
            UnallocatedFruit = MapUnallocated(packAllocation?.Unallocated ?? []),
            PackWarnings = packAllocation?.Warnings ?? [],
            PackAssignedPounds = packAllocation?.TotalAssignedPounds ?? 0m,
            PackUnallocatedPounds = packAllocation?.TotalUnallocatedPounds ?? 0m,
            PackRoundingResidualPounds = mappedPacks.Sum(x => x.RoundingResidualPounds),
            CanEditRecord = canEdit && !projection.IsDeleted && RunProjectionStatuses.Editable.Contains(projection.Status),
            CanDeleteRecord = !projection.IsDeleted
                && projection.Status != RunProjectionStatuses.Converted
                && projection.Sources.All(x => x.ActualBinsRunEntryId is null)
                && (canEdit && RunProjectionStatuses.Editable.Contains(projection.Status)
                    || canAdmin && projection.Status is RunProjectionStatuses.Cancelled or RunProjectionStatuses.Expired or RunProjectionStatuses.Superseded)
        };
    }

    private async Task<(RunProjectionSource? Entity, string? Error)> ResolveSourceAsync(
        RunProjection projection,
        string sourceKey,
        string selectedQcSource,
        int plannedBins,
        decimal? expectedPackoutPercent,
        int minimumDistributionFruit,
        bool availabilityOverride,
        CancellationToken cancellationToken)
    {
        var now = businessTime.UtcNow;
        QcSample? selectedFieldSample = null;
        if (sourceKey.StartsWith("F:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(sourceKey.AsSpan(2), out var fieldSampleId))
        {
            selectedFieldSample = await UsableFieldSamples(projection.CropYear)
                .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
                .Include(x => x.FieldSampleFruitProfile)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
                .SingleOrDefaultAsync(x => x.Id == fieldSampleId, cancellationToken);
        }
        else if (sourceKey.StartsWith("B:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = sourceKey.Split(':');
            if (parts.Length != 3
                || !int.TryParse(parts[1], out var fieldBlockId)
                || !int.TryParse(parts[2], out var profileId))
            {
                return (null, "Select a valid confirmed orchard block and variety.");
            }
            long? requestedSampleId = null;
            if (selectedQcSource?.StartsWith("FieldSample:", StringComparison.OrdinalIgnoreCase) == true
                && long.TryParse(selectedQcSource.AsSpan("FieldSample:".Length), out var parsedSampleId))
            {
                requestedSampleId = parsedSampleId;
            }
            selectedFieldSample = await UsableFieldSamples(projection.CropYear)
                .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
                .Include(x => x.FieldSampleFruitProfile)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
                .Where(x => x.CanonicalOrchardBlockId == fieldBlockId && x.FieldSampleFruitProfileId == profileId)
                .Where(x => requestedSampleId == null || x.Id == requestedSampleId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (selectedFieldSample is not null)
        {
            var sample = selectedFieldSample;
            var source = new RunProjectionSource
            {
                SourceType = RunProjectionSourceTypes.FieldSample,
                CanonicalOrchardBlockId = sample.CanonicalOrchardBlockId,
                FruitProfileId = sample.FieldSampleFruitProfileId!.Value,
                FieldSampleId = sample.Id,
                PlannedBins = plannedBins,
                AvailableBinsSnapshot = null,
                AvailabilityOverrideAcknowledged = false,
                SelectedQcSourceType = RunProjectionQcSourceTypes.FieldSample,
                ExpectedPackoutPercent = expectedPackoutPercent,
                SourceLabelSnapshot = $"Planning-only block — {sample.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName} / {sample.CanonicalOrchardBlock.CanonicalBlockName}",
                OrchardSnapshot = sample.CanonicalOrchardBlock.CanonicalOrchard.OrchardName,
                GrowerSnapshot = sample.FieldSampleGrowerName,
                GrowerNumberSnapshot = sample.FieldSampleGrowerNumber,
                BlockSnapshot = sample.CanonicalOrchardBlock.CanonicalBlockName,
                VarietySnapshot = sample.FieldSampleFruitProfile!.Name,
                Commodity = RunProjectionCalculationService.NormalizeCommodity(sample.FieldSampleFruitProfile.FruitType),
                CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            var qc = await ResolveQcSampleAsync(projection.CropYear, source, selectedQcSource, cancellationToken, sample);
            if (qc.Error is not null) return (null, qc.Error);
            await ApplyQcAndCalculationAsync(projection, source, qc.Sample, qc.SourceType, minimumDistributionFruit, cancellationToken);
            return (source, null);
        }
        if (sourceKey.StartsWith("F:", StringComparison.OrdinalIgnoreCase)
            || sourceKey.StartsWith("B:", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The selected confirmed same-block, same-variety Field Sample is no longer available.");
        }

        var inventory = await binsRunService.GetPlanningInventoryAsync(sourceKey, cancellationToken);
        if (inventory is null) return (null, "The selected inventory source is no longer available.");
        if (projection.FacilityWarehouseId is null)
        {
            return (null, "Assign WP or EBS before adding inventory.");
        }
        if (inventory.WarehouseId != projection.FacilityWarehouseId)
        {
            return (null, $"The selected inventory belongs to {inventory.Facility}, not the projection's {projection.FacilityCodeSnapshot ?? "selected"} facility.");
        }
        if (plannedBins > inventory.CurrentBins && !availabilityOverride)
        {
            return (null, $"Planned bins exceed the current available quantity of {inventory.CurrentBins}. Confirm the planning override to continue.");
        }

        var inventorySource = new RunProjectionSource
        {
            SourceType = RunProjectionSourceTypes.Inventory,
            InventoryKey = inventory.InventoryKey,
            ReceiptId = inventory.ReceiptId,
            SourceInventoryAdjustmentId = inventory.InventoryAdjustmentId,
            WarehouseId = inventory.WarehouseId,
            RoomId = inventory.RoomId,
            CanonicalOrchardBlockId = inventory.CanonicalOrchardBlockId,
            FruitProfileId = inventory.FruitProfileId ?? 0,
            PlannedBins = plannedBins,
            AvailableBinsSnapshot = inventory.CurrentBins,
            AvailabilityOverrideAcknowledged = availabilityOverride,
            SelectedQcSourceType = RunProjectionQcSourceTypes.Automatic,
            ExpectedPackoutPercent = expectedPackoutPercent,
            SourceLabelSnapshot = $"{inventory.Facility} / {inventory.Room} — {inventory.Grower}"
                + (string.IsNullOrWhiteSpace(inventory.GrowerNumber) ? "" : $" — grower # {inventory.GrowerNumber}")
                + (string.IsNullOrWhiteSpace(inventory.ReceiptReference) ? "" : $" — receipt {inventory.ReceiptReference}")
                + $" — lot {inventory.Lot}",
            FacilitySnapshot = inventory.Facility,
            RoomSnapshot = inventory.Room,
            LotSnapshot = inventory.Lot,
            GrowerSnapshot = inventory.Grower,
            GrowerNumberSnapshot = inventory.GrowerNumber,
            VarietySnapshot = inventory.Variety,
            Commodity = RunProjectionCalculationService.NormalizeCommodity(inventory.FruitType),
            CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (inventorySource.FruitProfileId <= 0)
        {
            return (null, "The inventory source has no configured fruit profile. Configure its variety before adding it to a projection.");
        }

        if (inventory.CanonicalOrchardBlockId is int blockId)
        {
            var identity = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
                .Where(x => x.Id == blockId)
                .Select(x => new
                {
                    Orchard = x.CanonicalOrchard.OrchardName,
                    Block = x.CanonicalBlockName,
                    Grower = x.CanonicalGrower == null ? null : x.CanonicalGrower.DisplayName
                })
                .SingleOrDefaultAsync(cancellationToken);
            inventorySource.OrchardSnapshot = identity?.Orchard;
            inventorySource.BlockSnapshot = identity?.Block;
            inventorySource.GrowerSnapshot = identity?.Grower ?? inventorySource.GrowerSnapshot;
        }

        var inventoryQc = await ResolveQcSampleAsync(projection.CropYear, inventorySource, selectedQcSource, cancellationToken);
        if (inventoryQc.Error is not null) return (null, inventoryQc.Error);
        await ApplyQcAndCalculationAsync(projection, inventorySource, inventoryQc.Sample, inventoryQc.SourceType, minimumDistributionFruit, cancellationToken);
        return (inventorySource, null);
    }

    private async Task<(QcSample? Sample, string SourceType, string? Error)> ResolveQcSampleAsync(
        int cropYear,
        RunProjectionSource source,
        string? requested,
        CancellationToken cancellationToken,
        QcSample? knownFieldSample = null)
    {
        var choice = string.IsNullOrWhiteSpace(requested) ? RunProjectionQcSourceTypes.Automatic : requested.Trim();
        if (choice.Equals(RunProjectionQcSourceTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return (null, RunProjectionQcSourceTypes.None, null);
        }

        if (choice.StartsWith("ReceiptQc:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(choice.AsSpan("ReceiptQc:".Length), out var receiptQcId))
        {
            var sample = await LoadUsableSampleAsync(receiptQcId, cropYear, cancellationToken);
            return sample is null || sample.ReceiptId != source.ReceiptId
                ? (null, "", "The selected receipt QC sample is not valid for this source.")
                : (sample, RunProjectionQcSourceTypes.ReceiptQc, null);
        }

        if (choice.StartsWith("FieldSample:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(choice.AsSpan("FieldSample:".Length), out var fieldId))
        {
            var sample = knownFieldSample?.Id == fieldId ? knownFieldSample : await LoadUsableSampleAsync(fieldId, cropYear, cancellationToken);
            return sample is null
                   || sample.ReceiptId is not null
                   || sample.CanonicalOrchardBlockId != source.CanonicalOrchardBlockId
                   || sample.FieldSampleFruitProfileId != source.FruitProfileId
                   || IsUnconfirmedFieldSample(sample)
                ? (null, "", "The selected Field Sample is not a confirmed same-block, same-variety source.")
                : (sample, RunProjectionQcSourceTypes.FieldSample, null);
        }

        if (!choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase)
            && !choice.Equals(RunProjectionQcSourceTypes.FieldSample, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "", "Select a valid QC data source.");
        }
        var useAutomaticPriority = choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase);

        if (source.SourceType == RunProjectionSourceTypes.FieldSample && knownFieldSample is not null)
        {
            return (await LoadUsableSampleAsync(knownFieldSample.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.FieldSample, null);
        }

        if (useAutomaticPriority && source.ReceiptId is long receiptId)
        {
            var receiptQc = await UsableQcSamples(cropYear)
                .Where(x => x.ReceiptId == receiptId)
                .Where(x => x.FruitReadings.Any(row => row.SizeCategory != null)
                    && x.FruitReadings.Any(row => row.GradeId != null))
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (receiptQc is not null) return (await LoadUsableSampleAsync(receiptQc.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.ReceiptQc, null);
        }

        if (source.CanonicalOrchardBlockId is int blockId)
        {
            var field = await UsableFieldSamples(cropYear)
                .Where(x => x.CanonicalOrchardBlockId == blockId && x.FieldSampleFruitProfileId == source.FruitProfileId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (field is not null) return (await LoadUsableSampleAsync(field.Id, cropYear, cancellationToken), RunProjectionQcSourceTypes.FieldSample, null);
        }

        return (null, RunProjectionQcSourceTypes.None, null);
    }

    private async Task<QcSample?> LoadUsableSampleAsync(long sampleId, int cropYear, CancellationToken cancellationToken)
    {
        var fieldWindow = FieldSampleCropWindow(cropYear);
        return await dbContext.QcSamples
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId
                && !x.IsDeleted
                && ((x.ReceiptId != null
                        && x.Receipt!.CropYear == cropYear
                        && (x.Status == "Complete"
                            || x.Status == "Ready to Send"
                            || x.Status == "Sent"
                            || x.Status == "Needs Resend")
                        && x.SampleType.IsActive
                        && x.SampleType.Name != FieldSampleTypeName)
                    || (x.ReceiptId == null
                        && x.SampleType.Name == FieldSampleTypeName
                        && x.SampleTakenAt >= fieldWindow.Start
                        && x.SampleTakenAt < fieldWindow.End
                        && x.CanonicalOrchardBlockId != null
                        && x.FieldSampleFruitProfileId != null
                        && x.FieldSampleBlockResolution != "Suggested"))
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null), cancellationToken);
    }

    private async Task ApplyQcAndCalculationAsync(
        RunProjection projection,
        RunProjectionSource source,
        QcSample? sample,
        string selectedSourceType,
        int minimumDistributionFruit,
        CancellationToken cancellationToken)
    {
        source.SelectedQcSourceType = selectedSourceType;
        source.SelectedQcSampleId = sample?.Id;
        if (source.SourceType == RunProjectionSourceTypes.FieldSample
            && selectedSourceType == RunProjectionQcSourceTypes.FieldSample)
        {
            source.FieldSampleId = sample?.Id;
        }
        source.SizeResults.Clear();
        source.GradeResults.Clear();
        var readings = sample?.FruitReadings.Where(HasEnteredFruitData).ToList() ?? [];
        var calculation = RunProjectionCalculationService.Calculate(
            source.Commodity,
            source.PlannedBins,
            projection.ApplePoundsPerBin,
            projection.PearPoundsPerBin,
            projection.StandardBoxWeightPounds,
            source.ExpectedPackoutPercent,
            readings.Where(x => x.SizeCategory != null).Select(x => new RunProjectionSizeObservation(x.SizeCategory!.Value)),
            readings.Where(x => x.Grade != null).Select(x => new RunProjectionGradeObservation(x.Grade!.Code)),
            readings.Count(x => x.SizeCategory != null && x.Grade != null),
            minimumDistributionFruit);

        source.PoundsPerBinUsed = calculation.PoundsPerBin;
        source.ProjectedPounds = calculation.ProjectedPounds;
        source.ProjectedBoxes = calculation.ProjectedBoxes;
        source.RoundedProjectedBoxes = calculation.RoundedProjectedBoxes;
        source.ExpectedCullPercent = calculation.ExpectedCullPercent;
        source.PackedProjectedPounds = calculation.PackedProjectedPounds;
        source.PackedProjectedBoxes = calculation.PackedProjectedBoxes;
        source.RoundedPackedProjectedBoxes = calculation.RoundedPackedProjectedBoxes;
        source.CullProjectedPounds = calculation.CullProjectedPounds;
        source.CullProjectedBoxes = calculation.CullProjectedBoxes;
        source.RoundedCullProjectedBoxes = calculation.RoundedCullProjectedBoxes;
        source.QcSampleDateSnapshot = sample?.SampleTakenAt;
        source.QcSampleTypeSnapshot = sample?.SampleType.Name;
        source.QcSampleStatusSnapshot = sample?.Status;
        source.QcFruitCountSnapshot = readings.Count;
        source.SizeBasisFruitCount = calculation.SizeBasisFruitCount;
        source.GradeBasisFruitCount = calculation.GradeBasisFruitCount;
        source.JointSizeGradeBasisFruitCount = calculation.JointSizeGradeBasisFruitCount;
        source.JointSizeGradeSnapshotJson = JsonSerializer.Serialize(readings
            .Where(x => x.SizeCategory != null && x.Grade != null)
            .GroupBy(x => new { Size = x.SizeCategory!.Value, Grade = x.Grade!.Code })
            .OrderBy(x => x.Key.Size)
            .ThenBy(x => x.Key.Grade)
            .Select(x => new CommercialPackJointSizeGradeSnapshot(x.Key.Size, x.Key.Grade, x.Count()))
            .ToList());
        source.AverageWeightGramsSnapshot = Average(readings.Where(x => x.WeightGrams != null).Select(x => x.WeightGrams!.Value));
        source.AveragePressureLbsSnapshot = Average(PressureCalculationService.ValidSideReadings(readings.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs))));
        source.GradeSummarySnapshot = Distribution(readings.Where(x => x.Grade != null).Select(x => x.Grade!.Code));
        var inspected = readings.Where(x => x.DefectsInspected).ToList();
        source.DefectSummarySnapshot = inspected.Count == 0
            ? "Not inspected"
            : $"{inspected.Count(x => x.Defects.Count > 0)} of {inspected.Count} inspected fruit affected; "
              + Distribution(inspected.SelectMany(x => x.Defects).Select(x => x.DefectType.Name));
        source.FieldSampleTrendSnapshotJson = null;
        if (sample is not null
            && sample.SampleType.Name.Equals(FieldSampleTypeName, StringComparison.OrdinalIgnoreCase))
        {
            var trend = await FieldSampleTrends.GetForSampleAsync(sample.Id, cancellationToken);
            source.FieldSampleTrendSnapshotJson = trend is null
                ? null
                : JsonSerializer.Serialize(trend.Points.Select(point => new RunProjectionTrendPointSnapshot(
                    point.SampleId,
                    point.SampleTakenAt,
                    point.Variety,
                    point.CompletionStatus,
                    point.TargetSampleSize,
                    point.Summary.EnteredFruitCount,
                    point.Summary.AverageWeightGrams,
                    point.Summary.AveragePressureLbs,
                    point.Summary.AverageStarch,
                    point.Summary.DefectAffectedPercentage,
                    point.SizeDistribution.Select(size =>
                        new RunProjectionTrendSizePoint(size.Size, size.Percentage)).ToList())).ToList());
        }
        source.ProjectionWarning = calculation.Warning;
        source.CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion;
        foreach (var allocation in calculation.SizeAllocations)
        {
            source.SizeResults.Add(new RunProjectionSizeResult
            {
                Commodity = allocation.Commodity,
                SizeCategory = allocation.SizeCategory,
                SampleCount = allocation.SampleCount,
                Percentage = allocation.Percentage,
                UnroundedProjectedBoxes = allocation.UnroundedProjectedBoxes,
                RoundedProjectedBoxes = allocation.RoundedProjectedBoxes,
                PackedProjectedBoxes = allocation.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = allocation.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = allocation.CullProjectedBoxes,
                RoundedCullProjectedBoxes = allocation.RoundedCullProjectedBoxes
            });
        }
        foreach (var allocation in calculation.GradeAllocations)
        {
            source.GradeResults.Add(new RunProjectionGradeResult
            {
                GradeCode = allocation.Key,
                SampleCount = allocation.SampleCount,
                Percentage = allocation.Percentage,
                GrossProjectedBoxes = allocation.GrossBoxes,
                RoundedGrossProjectedBoxes = allocation.RoundedGrossBoxes,
                PackedProjectedBoxes = allocation.PackedBoxes,
                RoundedPackedProjectedBoxes = allocation.RoundedPackedBoxes,
                CullProjectedBoxes = allocation.CullBoxes,
                RoundedCullProjectedBoxes = allocation.RoundedCullBoxes
            });
        }
    }

    private async Task<IReadOnlyList<RunProjectionQcChoiceViewModel>> GetQcChoicesAsync(
        RunProjectionSource source,
        int cropYear,
        CancellationToken cancellationToken)
    {
        var choices = new List<RunProjectionQcChoiceViewModel>();
        if (source.SourceType != RunProjectionSourceTypes.FieldSample)
        {
            choices.Add(new(RunProjectionQcSourceTypes.Automatic, "Automatic — latest completed receipt sample with size and grade, then confirmed Field Sample", null, RunProjectionQcSourceTypes.Automatic, null, null, null, 0, null, null, false, false, false, source.SelectedQcSourceType == RunProjectionQcSourceTypes.Automatic));
            choices.Add(new(RunProjectionQcSourceTypes.None, "No usable QC data", null, RunProjectionQcSourceTypes.None, null, null, null, 0, null, null, false, false, false, source.SelectedQcSourceType == RunProjectionQcSourceTypes.None));
        }
        if (source.ReceiptId is long receiptId)
        {
            var receiptSamples = await UsableQcSamples(cropYear)
                .Where(x => x.ReceiptId == receiptId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
                .Take(25)
                .ToListAsync(cancellationToken);
            choices.AddRange(receiptSamples.Select(x => QcChoice(x, RunProjectionQcSourceTypes.ReceiptQc, source.SelectedQcSampleId == x.Id)));
        }

        if (source.CanonicalOrchardBlockId is int blockId)
        {
            var fieldSamples = await UsableFieldSamples(cropYear)
                .Where(x => x.CanonicalOrchardBlockId == blockId && x.FieldSampleFruitProfileId == source.FruitProfileId)
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .Include(x => x.SampleType)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
                .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
                .Take(25)
                .ToListAsync(cancellationToken);
            choices.AddRange(fieldSamples.Select(x => QcChoice(x, RunProjectionQcSourceTypes.FieldSample, source.SelectedQcSampleId == x.Id)));
        }

        if (source.SelectedQcSampleId is long savedSampleId && choices.All(x => !x.IsSelected))
        {
            var savedSourceType = source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample
                ? RunProjectionQcSourceTypes.FieldSample
                : RunProjectionQcSourceTypes.ReceiptQc;
            choices.Insert(Math.Min(1, choices.Count), new RunProjectionQcChoiceViewModel(
                $"{savedSourceType}:{savedSampleId}",
                $"Saved snapshot — {source.QcSampleTypeSnapshot ?? savedSourceType} #{savedSampleId} — current sample is unavailable or no longer eligible",
                savedSampleId,
                savedSourceType,
                source.QcSampleDateSnapshot,
                source.QcSampleTypeSnapshot,
                source.QcSampleStatusSnapshot,
                source.QcFruitCountSnapshot ?? 0,
                source.AverageWeightGramsSnapshot,
                source.AveragePressureLbsSnapshot,
                source.SizeBasisFruitCount > 0,
                source.GradeBasisFruitCount > 0,
                !string.IsNullOrWhiteSpace(source.DefectSummarySnapshot),
                true));
        }
        return choices;
    }

    private RunProjectionQcChoiceViewModel QcChoice(QcSample sample, string sourceType, bool selected)
    {
        var rows = sample.FruitReadings.Where(HasEnteredFruitData).ToList();
        var averageWeight = Average(rows.Where(x => x.WeightGrams != null).Select(x => x.WeightGrams!.Value));
        var averagePressure = Average(PressureCalculationService.ValidSideReadings(rows.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs))));
        var hasSize = rows.Any(x => x.SizeCategory != null);
        var hasGrade = rows.Any(x => x.GradeId != null);
        var hasDefects = rows.Any(x => x.DefectsInspected);
        var label = $"{sample.SampleType.Name} #{sample.Id} — {businessTime.FormatPacific(sample.SampleTakenAt, "MMM d, yyyy h:mm tt")} — {sample.Status} — {rows.Count} fruit"
            + $" — avg wt {(averageWeight?.ToString("0.##") ?? "n/a")} g"
            + $" — avg pressure {(averagePressure?.ToString("0.##") ?? "n/a")} lb"
            + $" — size {(hasSize ? "yes" : "no")}, grade {(hasGrade ? "yes" : "no")}, defects {(hasDefects ? "yes" : "no")}";
        return new RunProjectionQcChoiceViewModel(
            $"{sourceType}:{sample.Id}",
            label,
            sample.Id,
            sourceType,
            sample.SampleTakenAt,
            sample.SampleType.Name,
            sample.Status,
            rows.Count,
            averageWeight,
            averagePressure,
            hasSize,
            hasGrade,
            hasDefects,
            selected);
    }

    private IQueryable<QcSample> UsableQcSamples(int cropYear) =>
        dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId != null
                && x.Receipt!.CropYear == cropYear
                && (x.Status == "Complete"
                    || x.Status == "Ready to Send"
                    || x.Status == "Sent"
                    || x.Status == "Needs Resend")
                && x.SampleType.IsActive
                && x.SampleType.Name != FieldSampleTypeName
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null));

    private IQueryable<QcSample> UsableFieldSamples(int cropYear)
    {
        var window = FieldSampleCropWindow(cropYear);
        return dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId == null
                && x.SampleType.Name == FieldSampleTypeName
                && x.SampleTakenAt >= window.Start
                && x.SampleTakenAt < window.End
                && x.CanonicalOrchardBlockId != null
                && x.FieldSampleFruitProfileId != null
                && x.FieldSampleBlockResolution != "Suggested"
                && x.FruitReadings.Any(row => row.SizeCategory != null || row.GradeId != null));
    }

    private UtcDayRange FieldSampleCropWindow(int cropYear)
    {
        // Existing crop-year rules allow early-season samples beginning in May and
        // late-season samples through the following December.
        var start = businessTime.UtcRangeForPacificDate(new DateOnly(cropYear, 5, 1)).Start;
        var end = businessTime.UtcRangeForPacificDate(new DateOnly(cropYear + 2, 1, 1)).Start;
        return new UtcDayRange(start, end);
    }

    private static bool IsUnconfirmedFieldSample(QcSample sample) =>
        sample.CanonicalOrchardBlockId is null
        || string.Equals(sample.FieldSampleBlockResolution, "Suggested", StringComparison.OrdinalIgnoreCase);

    private async Task<RunProjection?> LoadProjectionAsync(long id, CancellationToken cancellationToken) =>
        await dbContext.RunProjections
            .Include(x => x.FacilityWarehouse)
            .Include(x => x.CreatedByUser)
            .Include(x => x.DeletedByUser)
            .Include(x => x.Sources).ThenInclude(x => x.SizeResults)
            .Include(x => x.Sources).ThenInclude(x => x.GradeResults)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task<(RunProjection? Projection, string? Error, int? UserId)> LoadForEditAsync(
        long id,
        long concurrencyVersion,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, PageAccessLevel.Edit, cancellationToken))
        {
            return (null, "Bins Run Edit access is required to change projections.", null);
        }

        var projection = await LoadProjectionAsync(id, cancellationToken);
        if (projection is null) return (null, "Projection was not found.", null);
        if (projection.IsDeleted) return (null, "Deleted projections are read-only.", null);
        if (!RunProjectionStatuses.Editable.Contains(projection.Status)) return (null, $"A {projection.Status} projection cannot be edited.", null);
        if (projection.ConcurrencyVersion != concurrencyVersion) return (null, ConflictMessage, null);
        return (projection, null, await CurrentUserIdAsync(user, cancellationToken));
    }

    private static bool ChoiceMatchesSnapshot(RunProjectionSource source, string choice)
    {
        if (choice.Equals(RunProjectionQcSourceTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return source.SelectedQcSampleId is null;
        }
        if (choice.Equals(RunProjectionQcSourceTypes.Automatic, StringComparison.OrdinalIgnoreCase))
        {
            return source.SelectedQcSourceType == RunProjectionQcSourceTypes.Automatic;
        }
        var separator = choice.IndexOf(':');
        return separator > 0
            && long.TryParse(choice[(separator + 1)..], out var sampleId)
            && source.SelectedQcSampleId == sampleId;
    }

    private static void RecalculateFromSnapshot(RunProjection projection, RunProjectionSource source, int minimumDistributionFruit)
    {
        var sizeObservations = source.SizeResults
            .SelectMany(x => Enumerable.Repeat(new RunProjectionSizeObservation(x.SizeCategory), x.SampleCount))
            .ToList();
        var gradeObservations = source.GradeResults
            .SelectMany(x => Enumerable.Repeat(new RunProjectionGradeObservation(x.GradeCode), x.SampleCount))
            .ToList();
        var calculation = RunProjectionCalculationService.Calculate(
            source.Commodity,
            source.PlannedBins,
            projection.ApplePoundsPerBin,
            projection.PearPoundsPerBin,
            projection.StandardBoxWeightPounds,
            source.ExpectedPackoutPercent,
            sizeObservations,
            gradeObservations,
            source.JointSizeGradeBasisFruitCount,
            minimumDistributionFruit);
        source.PoundsPerBinUsed = calculation.PoundsPerBin;
        source.ProjectedPounds = calculation.ProjectedPounds;
        source.ProjectedBoxes = calculation.ProjectedBoxes;
        source.RoundedProjectedBoxes = calculation.RoundedProjectedBoxes;
        source.ExpectedCullPercent = calculation.ExpectedCullPercent;
        source.PackedProjectedPounds = calculation.PackedProjectedPounds;
        source.PackedProjectedBoxes = calculation.PackedProjectedBoxes;
        source.RoundedPackedProjectedBoxes = calculation.RoundedPackedProjectedBoxes;
        source.CullProjectedPounds = calculation.CullProjectedPounds;
        source.CullProjectedBoxes = calculation.CullProjectedBoxes;
        source.RoundedCullProjectedBoxes = calculation.RoundedCullProjectedBoxes;
        source.ProjectionWarning = calculation.Warning;
        source.CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion;
        foreach (var result in source.SizeResults)
        {
            var allocation = calculation.SizeAllocations.Single(x => x.SizeCategory == result.SizeCategory);
            result.Percentage = allocation.Percentage;
            result.UnroundedProjectedBoxes = allocation.UnroundedProjectedBoxes;
            result.RoundedProjectedBoxes = allocation.RoundedProjectedBoxes;
            result.PackedProjectedBoxes = allocation.PackedProjectedBoxes;
            result.RoundedPackedProjectedBoxes = allocation.RoundedPackedProjectedBoxes;
            result.CullProjectedBoxes = allocation.CullProjectedBoxes;
            result.RoundedCullProjectedBoxes = allocation.RoundedCullProjectedBoxes;
        }
        foreach (var result in source.GradeResults)
        {
            var allocation = calculation.GradeAllocations.Single(x => x.Key.Equals(result.GradeCode, StringComparison.OrdinalIgnoreCase));
            result.Percentage = allocation.Percentage;
            result.GrossProjectedBoxes = allocation.GrossBoxes;
            result.RoundedGrossProjectedBoxes = allocation.RoundedGrossBoxes;
            result.PackedProjectedBoxes = allocation.PackedBoxes;
            result.RoundedPackedProjectedBoxes = allocation.RoundedPackedBoxes;
            result.CullProjectedBoxes = allocation.CullBoxes;
            result.RoundedCullProjectedBoxes = allocation.RoundedCullBoxes;
        }
    }

    private void RecalculateTotals(RunProjection projection, long? excludingSourceId = null)
    {
        var sources = projection.Sources.Where(x => x.Id != excludingSourceId).ToList();
        projection.TotalPlannedBins = sources.Sum(x => x.PlannedBins);
        projection.TotalProjectedPounds = sources.Sum(x => x.ProjectedPounds);
        projection.TotalProjectedBoxes = sources.Sum(x => x.ProjectedBoxes);
        projection.TotalRoundedProjectedBoxes = sources.Sum(x => x.RoundedProjectedBoxes);
        projection.TotalPackedProjectedPounds = sources.Sum(x => x.PackedProjectedPounds);
        projection.TotalPackedProjectedBoxes = sources.Sum(x => x.PackedProjectedBoxes);
        projection.TotalRoundedPackedProjectedBoxes = sources.Sum(x => x.RoundedPackedProjectedBoxes);
        projection.TotalCullProjectedPounds = sources.Sum(x => x.CullProjectedPounds);
        projection.TotalCullProjectedBoxes = sources.Sum(x => x.CullProjectedBoxes);
        projection.TotalRoundedCullProjectedBoxes = sources.Sum(x => x.RoundedCullProjectedBoxes);
    }

    private async Task<CommercialPackPlanSnapshot?> BuildPackPlanSnapshotAsync(
        int planId,
        int cropYear,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.CommercialPackPlans.AsNoTracking()
            .Include(x => x.Items).ThenInclude(x => x.CommercialPackDefinition).ThenInclude(x => x.EligibleSizes)
            .Include(x => x.Items).ThenInclude(x => x.CommercialPackDefinition).ThenInclude(x => x.FruitProfileRestrictions)
            .SingleOrDefaultAsync(x => x.Id == planId
                && x.IsActive
                && (x.EffectiveCropYearStart == null || x.EffectiveCropYearStart <= cropYear)
                && (x.EffectiveCropYearEnd == null || x.EffectiveCropYearEnd >= cropYear),
                cancellationToken);
        if (plan is null) return null;
        var packs = plan.Items
            .Where(x => x.CommercialPackDefinition.IsActive
                && (x.CommercialPackDefinition.EffectiveCropYearStart == null || x.CommercialPackDefinition.EffectiveCropYearStart <= cropYear)
                && (x.CommercialPackDefinition.EffectiveCropYearEnd == null || x.CommercialPackDefinition.EffectiveCropYearEnd >= cropYear))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CommercialPackDefinition.Code)
            .Select(x => new CommercialPackDefinitionSnapshot(
                x.CommercialPackDefinitionId,
                x.CommercialPackDefinition.Code,
                x.CommercialPackDefinition.DisplayName,
                x.CommercialPackDefinition.Commodity,
                x.CommercialPackDefinition.PackType,
                x.CommercialPackDefinition.PackageWeightPounds,
                x.CommercialPackDefinition.AllowsMixedSizes,
                x.CommercialPackDefinition.MixRule,
                x.Priority,
                x.CommercialPackDefinition.FruitProfileRestrictions.Select(y => y.FruitProfileId).OrderBy(y => y).ToList(),
                x.CommercialPackDefinition.EligibleSizes
                    .OrderBy(y => y.Priority)
                    .ThenBy(y => y.SizeCategory)
                    .Select(y => new CommercialPackEligibleSizeSnapshot(
                        y.SizeCategory,
                        y.Priority,
                        y.TargetPercent,
                        y.MinimumPercent,
                        y.MaximumPercent))
                    .ToList()))
            .ToList();
        return new CommercialPackPlanSnapshot(
            plan.Id,
            plan.Code,
            plan.DisplayName,
            plan.Commodity,
            plan.PlanType,
            cropYear,
            packs);
    }

    private CommercialPackAllocationResult CalculatePackAllocation(
        RunProjection projection,
        CommercialPackPlanSnapshot plan,
        long? excludingSourceId = null)
    {
        var pools = projection.Sources
            .Where(source => source.Id != excludingSourceId)
            .SelectMany(source =>
            {
                var joint = DeserializeJointSnapshot(source.JointSizeGradeSnapshotJson);
                return source.SizeResults.Select(size => new CommercialPackSizePool(
                    source.Id,
                    source.SourceLabelSnapshot,
                    source.FruitProfileId,
                    source.Commodity,
                    size.SizeCategory,
                    size.UnroundedProjectedBoxes * projection.StandardBoxWeightPounds,
                    size.PackedProjectedBoxes * projection.StandardBoxWeightPounds,
                    size.CullProjectedBoxes * projection.StandardBoxWeightPounds,
                    joint.Where(x => x.SizeCategory == size.SizeCategory)
                        .Select(x => new CommercialPackJointGradeCount(x.GradeCode, x.Count))
                        .ToList()));
            })
            .ToList();
        return CommercialPackAllocationService.Allocate(
            plan,
            pools,
            projection.StandardBoxWeightPounds,
            RunProjectionSettings.DefaultMinimumDistributionFruit);
    }

    private static bool PlanMatchesProjection(CommercialPackPlanSnapshot plan, RunProjection projection) =>
        plan.Commodity.Equals("All", StringComparison.OrdinalIgnoreCase)
        || projection.Sources.All(x => x.Commodity.Equals(plan.Commodity, StringComparison.OrdinalIgnoreCase));

    private void RecalculatePackAllocationFromSavedSnapshot(RunProjection projection, long? excludingSourceId = null)
    {
        if (string.IsNullOrWhiteSpace(projection.PackConfigurationSnapshotJson)) return;
        try
        {
            var plan = JsonSerializer.Deserialize<CommercialPackPlanSnapshot>(projection.PackConfigurationSnapshotJson);
            if (plan is null) return;
            var result = CalculatePackAllocation(projection, plan, excludingSourceId);
            projection.PackAllocationSnapshotJson = JsonSerializer.Serialize(result);
            projection.PackCalculationVersion = result.CalculationVersion;
            projection.PackCalculatedAt = businessTime.UtcNow;
        }
        catch (JsonException)
        {
            projection.PackAllocationSnapshotJson = JsonSerializer.Serialize(new CommercialPackAllocationResult(
                CommercialPackAllocationResult.CurrentVersion,
                0m,
                0m,
                0m,
                0m,
                0m,
                [],
                [],
                ["The saved commercial pack configuration snapshot could not be read. Preview a current pack plan before recalculating."]));
            projection.PackCalculationVersion = CommercialPackAllocationResult.CurrentVersion;
            projection.PackCalculatedAt = businessTime.UtcNow;
        }
    }

    private static IReadOnlyList<CommercialPackJointSizeGradeSnapshot> DeserializeJointSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CommercialPackJointSizeGradeSnapshot>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static CommercialPackAllocationResult? DeserializePackAllocation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CommercialPackAllocationResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ConfigurationHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static IReadOnlyList<RunProjectionPackResultViewModel> MapPacks(IEnumerable<CommercialPackOutput> packs) =>
        packs.Select(x => new RunProjectionPackResultViewModel(
            x.DefinitionId,
            x.PackCode,
            x.PackName,
            x.Commodity,
            x.PackType,
            x.PackageWeightPounds,
            x.IsMixedSize,
            x.MixRule,
            x.EligibleSizes,
            x.GrossAssignedPounds,
            x.AssignedPounds,
            x.CullPounds,
            x.UnroundedPacks,
            (int)decimal.Floor(x.UnroundedPacks),
            x.AssignedPounds - decimal.Floor(x.UnroundedPacks) * x.PackageWeightPounds,
            x.PercentageOfProjectedPackout,
            x.Contributions.Select(y => new RunProjectionPackContributionViewModel(
                y.SourceId,
                y.SourceLabel,
                y.SizeCategory,
                y.AssignedPounds,
                y.GrossPounds,
                y.CullPounds)).ToList(),
            x.JointBasisFruitCount,
            x.GradeAllocations.Select(y => new RunProjectionPackGradeViewModel(y.GradeCode, y.AssignedPounds)).ToList(),
            x.GradeWarning)).ToList();

    private static IReadOnlyList<RunProjectionUnallocatedFruitViewModel> MapUnallocated(
        IEnumerable<CommercialPackUnallocatedFruit> rows) =>
        rows.Select(x => new RunProjectionUnallocatedFruitViewModel(
            x.SourceId,
            x.SourceLabel,
            x.Commodity,
            x.SizeCategory,
            x.Pounds,
            x.StandardBoxEquivalents,
            x.Reason)).ToList();

    private void Touch(RunProjection projection, int? userId)
    {
        projection.ConcurrencyVersion++;
        projection.UpdatedAt = businessTime.UtcNow;
        projection.UpdatedByUserId = userId;
    }

    private async Task ExpireDraftsAsync(int expirationDays, CancellationToken cancellationToken)
    {
        var cutoff = businessTime.PacificDate(businessTime.UtcNow).AddDays(-expirationDays);
        var drafts = await dbContext.RunProjections
            .Where(x => !x.IsDeleted && x.Status == RunProjectionStatuses.Draft && x.PlannedRunDate < cutoff)
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0) return;
        foreach (var projection in drafts)
        {
            projection.Status = RunProjectionStatuses.Expired;
            projection.UpdatedAt = businessTime.UtcNow;
            projection.ConcurrencyVersion++;
            await AddAuditAsync("Expire", projection, null, new { Status = RunProjectionStatuses.Draft }, new { projection.Status }, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectionSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            RunProjectionSettings.ApplePoundsPerBinKey,
            RunProjectionSettings.PearPoundsPerBinKey,
            RunProjectionSettings.StandardBoxWeightKey,
            RunProjectionSettings.DraftExpirationDaysKey,
            RunProjectionSettings.VisibilityPastDaysKey,
            RunProjectionSettings.VisibilityFutureDaysKey,
            RunProjectionSettings.DefaultExpectedPackoutPercentKey,
            RunProjectionSettings.MinimumDistributionFruitKey
        };
        var values = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        return new ProjectionSettingsSnapshot(
            ReadDecimal(values, RunProjectionSettings.ApplePoundsPerBinKey, RunProjectionCalculationService.DefaultApplePoundsPerBin),
            ReadDecimal(values, RunProjectionSettings.PearPoundsPerBinKey, RunProjectionCalculationService.DefaultPearPoundsPerBin),
            ReadDecimal(values, RunProjectionSettings.StandardBoxWeightKey, RunProjectionCalculationService.DefaultStandardBoxWeightPounds),
            ReadInt(values, RunProjectionSettings.DraftExpirationDaysKey, RunProjectionSettings.DefaultDraftExpirationDays, 1, 365),
            ReadInt(values, RunProjectionSettings.VisibilityPastDaysKey, RunProjectionSettings.DefaultVisibilityPastDays, 1, 365),
            ReadInt(values, RunProjectionSettings.VisibilityFutureDaysKey, RunProjectionSettings.DefaultVisibilityFutureDays, 1, 365),
            ReadPercent(values, RunProjectionSettings.DefaultExpectedPackoutPercentKey, RunProjectionCalculationService.DefaultExpectedPackoutPercent),
            ReadInt(values, RunProjectionSettings.MinimumDistributionFruitKey, RunProjectionSettings.DefaultMinimumDistributionFruit, 1, 50));
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static decimal ReadPercent(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) && parsed is >= 0 and <= 100 ? parsed : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback, int min, int max) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed >= min && parsed <= max ? parsed : fallback;

    private IQueryable<Warehouse> OperationalFacilities() =>
        dbContext.Warehouses.Where(x => x.IsActive && OperationalFacilityCodes.Contains(x.Code));

    private static string NormalizeFacilityFilter(string? facility, bool canAdmin)
    {
        var normalized = facility?.Trim() ?? "All";
        if (OperationalFacilityCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return OperationalFacilityCodes.Single(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }
        if (canAdmin && normalized.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)) return "Unassigned";
        return "All";
    }

    private static string NormalizeDeletionStatus(string? status, bool canAdmin)
    {
        if (!canAdmin) return "Active";
        return status?.Trim().ToUpperInvariant() switch
        {
            "DELETED" => "Deleted",
            "ALL" => "All",
            _ => "Active"
        };
    }

    private static string NormalizeSort(string? sort) =>
        sort?.Trim().ToUpperInvariant() switch
        {
            "RUNDATE" => "RunDate",
            "STATUS" => "Status",
            "UPDATED" => "Updated",
            "PLANNEDBINS" => "PlannedBins",
            _ => "Facility"
        };

    private static IQueryable<RunProjection> ApplyPlannerFilters(
        IQueryable<RunProjection> query,
        string facility,
        string deletionStatus)
    {
        query = deletionStatus switch
        {
            "Deleted" => query.Where(x => x.IsDeleted),
            "All" => query,
            _ => query.Where(x => !x.IsDeleted)
        };
        return facility switch
        {
            "WP" => query.Where(x => x.FacilityWarehouse != null && x.FacilityWarehouse.Code == "WP"),
            "EBS" => query.Where(x => x.FacilityWarehouse != null && x.FacilityWarehouse.Code == "EBS"),
            "Unassigned" => query.Where(x => x.FacilityWarehouseId == null),
            _ => query.Where(x => x.FacilityWarehouse != null
                && (x.FacilityWarehouse.Code == "WP" || x.FacilityWarehouse.Code == "EBS"))
        };
    }

    private async Task RequireAsync(ClaimsPrincipal user, PageAccessLevel level, CancellationToken cancellationToken)
    {
        if (!await CanAsync(user, level, cancellationToken))
        {
            throw new UnauthorizedAccessException($"Bins Run {level} access is required.");
        }
    }

    private Task<bool> CanAsync(ClaimsPrincipal user, PageAccessLevel level, CancellationToken cancellationToken) =>
        userAccessService.HasAccessAsync(user, ApplicationAreas.BinsRun, level, cancellationToken);

    private async Task<int?> CurrentUserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task AddAuditAsync(
        string action,
        RunProjection projection,
        int? userId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(RunProjection),
            EntityKey = projection.Id.ToString(),
            UserId = userId,
            BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterValuesJson = after is null ? null : JsonSerializer.Serialize(after),
            SourceApplication = SourceApplication,
            CreatedAt = businessTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private static object ProjectionSnapshot(RunProjection x) => new
    {
        x.Id,
        x.PlannedRunDate,
        x.Name,
        x.Status,
        x.ProjectionMode,
        x.FacilityWarehouseId,
        x.FacilityCodeSnapshot,
        x.CommercialPackPlanId,
        x.PackPlanCodeSnapshot,
        x.PackPlanNameSnapshot,
        x.PackPlanTypeSnapshot,
        x.PackCalculationVersion,
        x.PackCalculatedAt,
        x.CropYear,
        x.SourceProjectionId,
        x.ApplePoundsPerBin,
        x.PearPoundsPerBin,
        x.StandardBoxWeightPounds,
        x.TotalPlannedBins,
        x.TotalProjectedPounds,
        x.TotalProjectedBoxes,
        x.TotalPackedProjectedPounds,
        x.TotalPackedProjectedBoxes,
        x.TotalCullProjectedPounds,
        x.TotalCullProjectedBoxes,
        x.ConcurrencyVersion,
        x.IsDeleted,
        x.DeletedAt,
        x.DeletedByUserId,
        x.DeletionReason,
        x.DeletionOperationId,
        x.DeletedFromStatus
    };

    private static object SourceSnapshot(RunProjectionSource x) => new
    {
        x.Id,
        x.SourceType,
        x.InventoryKey,
        x.ReceiptId,
        x.CanonicalOrchardBlockId,
        x.FieldSampleId,
        x.SourceProjectionSourceId,
        x.SelectedQcSourceType,
        x.SelectedQcSampleId,
        x.PlannedBins,
        x.AvailableBinsSnapshot,
        x.AvailabilityOverrideAcknowledged,
        x.ProjectedPounds,
        x.ProjectedBoxes,
        x.RoundedProjectedBoxes,
        x.ExpectedPackoutPercent,
        x.ExpectedCullPercent,
        x.ExpectedPackoutUsedDefault,
        x.PackedProjectedPounds,
        x.PackedProjectedBoxes,
        x.CullProjectedPounds,
        x.CullProjectedBoxes,
        x.QcSampleTypeSnapshot,
        x.QcSampleDateSnapshot,
        x.SizeBasisFruitCount,
        x.GradeBasisFruitCount,
        x.JointSizeGradeBasisFruitCount,
        HasFieldSampleTrendSnapshot = !string.IsNullOrWhiteSpace(x.FieldSampleTrendSnapshotJson),
        x.CalculationVersion,
        x.ActualBinsRunEntryId
    };

    private static RunProjectionSource CloneSource(RunProjectionSource source, DateTimeOffset now)
    {
        var clone = new RunProjectionSource
        {
            SourceType = source.SourceType,
            InventoryKey = source.InventoryKey,
            ReceiptId = source.ReceiptId,
            SourceInventoryAdjustmentId = source.SourceInventoryAdjustmentId,
            WarehouseId = source.WarehouseId,
            RoomId = source.RoomId,
            CanonicalOrchardBlockId = source.CanonicalOrchardBlockId,
            FruitProfileId = source.FruitProfileId,
            FieldSampleId = source.FieldSampleId,
            SourceProjectionSourceId = source.SourceProjectionSourceId,
            SelectedQcSourceType = source.SelectedQcSourceType,
            SelectedQcSampleId = source.SelectedQcSampleId,
            PlannedBins = source.PlannedBins,
            AvailableBinsSnapshot = source.AvailableBinsSnapshot,
            AvailabilityOverrideAcknowledged = source.AvailabilityOverrideAcknowledged,
            SortOrder = source.SortOrder,
            Notes = source.Notes,
            Commodity = source.Commodity,
            PoundsPerBinUsed = source.PoundsPerBinUsed,
            ProjectedPounds = source.ProjectedPounds,
            ProjectedBoxes = source.ProjectedBoxes,
            RoundedProjectedBoxes = source.RoundedProjectedBoxes,
            ExpectedPackoutPercent = source.ExpectedPackoutPercent,
            ExpectedCullPercent = source.ExpectedCullPercent,
            ExpectedPackoutUsedDefault = source.ExpectedPackoutUsedDefault,
            PackedProjectedPounds = source.PackedProjectedPounds,
            PackedProjectedBoxes = source.PackedProjectedBoxes,
            RoundedPackedProjectedBoxes = source.RoundedPackedProjectedBoxes,
            CullProjectedPounds = source.CullProjectedPounds,
            CullProjectedBoxes = source.CullProjectedBoxes,
            RoundedCullProjectedBoxes = source.RoundedCullProjectedBoxes,
            SourceLabelSnapshot = source.SourceLabelSnapshot,
            FacilitySnapshot = source.FacilitySnapshot,
            RoomSnapshot = source.RoomSnapshot,
            LotSnapshot = source.LotSnapshot,
            OrchardSnapshot = source.OrchardSnapshot,
            GrowerSnapshot = source.GrowerSnapshot,
            GrowerNumberSnapshot = source.GrowerNumberSnapshot,
            BlockSnapshot = source.BlockSnapshot,
            VarietySnapshot = source.VarietySnapshot,
            QcSampleDateSnapshot = source.QcSampleDateSnapshot,
            QcSampleTypeSnapshot = source.QcSampleTypeSnapshot,
            QcSampleStatusSnapshot = source.QcSampleStatusSnapshot,
            QcFruitCountSnapshot = source.QcFruitCountSnapshot,
            SizeBasisFruitCount = source.SizeBasisFruitCount,
            GradeBasisFruitCount = source.GradeBasisFruitCount,
            JointSizeGradeBasisFruitCount = source.JointSizeGradeBasisFruitCount,
            AverageWeightGramsSnapshot = source.AverageWeightGramsSnapshot,
            AveragePressureLbsSnapshot = source.AveragePressureLbsSnapshot,
            GradeSummarySnapshot = source.GradeSummarySnapshot,
            DefectSummarySnapshot = source.DefectSummarySnapshot,
            JointSizeGradeSnapshotJson = source.JointSizeGradeSnapshotJson,
            FieldSampleTrendSnapshotJson = source.FieldSampleTrendSnapshotJson,
            ProjectionWarning = source.ProjectionWarning,
            CalculationVersion = source.CalculationVersion,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var size in source.SizeResults)
        {
            clone.SizeResults.Add(new RunProjectionSizeResult
            {
                Commodity = size.Commodity,
                SizeCategory = size.SizeCategory,
                SampleCount = size.SampleCount,
                Percentage = size.Percentage,
                UnroundedProjectedBoxes = size.UnroundedProjectedBoxes,
                RoundedProjectedBoxes = size.RoundedProjectedBoxes,
                PackedProjectedBoxes = size.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = size.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = size.CullProjectedBoxes,
                RoundedCullProjectedBoxes = size.RoundedCullProjectedBoxes
            });
        }
        foreach (var grade in source.GradeResults)
        {
            clone.GradeResults.Add(new RunProjectionGradeResult
            {
                GradeCode = grade.GradeCode,
                SampleCount = grade.SampleCount,
                Percentage = grade.Percentage,
                GrossProjectedBoxes = grade.GrossProjectedBoxes,
                RoundedGrossProjectedBoxes = grade.RoundedGrossProjectedBoxes,
                PackedProjectedBoxes = grade.PackedProjectedBoxes,
                RoundedPackedProjectedBoxes = grade.RoundedPackedProjectedBoxes,
                CullProjectedBoxes = grade.CullProjectedBoxes,
                RoundedCullProjectedBoxes = grade.RoundedCullProjectedBoxes
            });
        }
        return clone;
    }

    private static bool HasEnteredFruitData(QcFruitReading row) =>
        row.Pressure1Lbs is not null
        || row.Pressure2Lbs is not null
        || row.WeightGrams is not null
        || row.GradeId is not null
        || row.StarchScaleValueId is not null
        || row.SizeCategory is not null
        || row.DefectsInspected
        || row.Defects.Count > 0;

    private static decimal? Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : decimal.Round(list.Average(), 2);
    }

    private static string Distribution(IEnumerable<string> values)
    {
        var list = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0) return "No data";
        return string.Join(", ", list.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => $"{x.Key} {x.Count() / (decimal)list.Count:P0}"));
    }

    private static string QcBasis(RunProjectionSource source) =>
        source.SelectedQcSampleId is null
            ? "No usable QC data"
            : $"{source.QcSampleTypeSnapshot ?? (source.SelectedQcSourceType == RunProjectionQcSourceTypes.FieldSample ? "Field Sample" : "Receipt QC")} #{source.SelectedQcSampleId}";

    private const string ConflictMessage = "This projection changed after the page loaded. Reload it before saving so another user's work is not overwritten.";

    private sealed record ProjectionSettingsSnapshot(
        decimal ApplePoundsPerBin,
        decimal PearPoundsPerBin,
        decimal StandardBoxWeightPounds,
        int DraftExpirationDays,
        int VisibilityPastDays,
        int VisibilityFutureDays,
        decimal DefaultExpectedPackoutPercent,
        int MinimumDistributionFruit);
}
