using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace CropQc.Web.Services;

public interface IDashboardDataService
{
    Task<HomeDashboardViewModel> GetHomeDashboardAsync(RoomSummaryFilterForm? roomSummaryFilter, CancellationToken cancellationToken);
    Task<RoomsPageViewModel> GetRoomsAsync(RoomSummaryFilterForm? roomSummaryFilter, CancellationToken cancellationToken);
    Task<CurrentGrowerLotsPageViewModel> GetCurrentGrowerLotsAsync(CurrentGrowerLotsFilterForm filter, CancellationToken cancellationToken);
    Task<CropYearReviewPageViewModel> GetCropYearReviewAsync(CropYearReviewFilterForm filter, CancellationToken cancellationToken);
    Task<MasterDataPageViewModel> GetMasterDataPageAsync(string type, CancellationToken cancellationToken);
    Task<ReceiptListViewModel> SearchReceiptsAsync(ReceiptSearchForm search, CancellationToken cancellationToken);
    Task<CreateReceiptResult> CreateReceiptAsync(CreateReceiptForm form, CancellationToken cancellationToken);
    Task<ReceiptDetailViewModel> GetReceiptDetailAsync(long id, CancellationToken cancellationToken);
    Task<EditReceiptPageViewModel> GetReceiptEditAsync(long id, CancellationToken cancellationToken);
    Task<string?> UpdateReceiptAsync(UpdateReceiptForm form, CancellationToken cancellationToken);
    Task<string?> SoftDeleteReceiptAsync(DeleteReceiptForm form, CancellationToken cancellationToken);
    Task<(long? SampleId, int? SampleSequenceNumber, string? Warning, string? Error)> CreateSampleAsync(long receiptId, int sampleTypeId, CancellationToken cancellationToken);
    Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken);
    Task<SampleRefreshViewModel?> GetSampleRefreshAsync(long id, CancellationToken cancellationToken);
    Task<DeleteSampleConfirmationViewModel> GetDeleteSampleConfirmationAsync(long id, CancellationToken cancellationToken);
    Task<(long? ReceiptId, string? Error)> SoftDeleteSampleAsync(long id, string? reason, CancellationToken cancellationToken);
    Task<string?> UpdateSampleTypeAsync(UpdateSampleTypeForm form, CancellationToken cancellationToken);
    Task<string?> SaveFruitReadingsAsync(SaveFruitReadingsForm form, CancellationToken cancellationToken);
    Task<FieldSampleAutosaveResult> AutosaveFruitReadingsAsync(long sampleId, FieldSampleAutosaveRequest request, CancellationToken cancellationToken);
    Task<ReceiptReportPreviewViewModel> GetQcReportPreviewAsync(long sampleId, CancellationToken cancellationToken);
    Task<StarchTestViewModel> GetStarchTestAsync(long id, CancellationToken cancellationToken);
    Task<string?> SaveStarchTestAsync(SaveStarchTestForm form, CancellationToken cancellationToken);
    Task<OverrideSendViewModel> GetOverrideSendAsync(long id, CancellationToken cancellationToken);
    Task<string?> SendQcSummaryAsync(long sampleId, CancellationToken cancellationToken);
    Task<string?> LogOverrideSendAsync(OverrideSendForm form, CancellationToken cancellationToken);
    Task<string?> AddPhotoMetadataAsync(AddPhotoMetadataForm form, CancellationToken cancellationToken);
    Task<string?> AddSamplePhotoMetadataAsync(long sampleId, AddPhotoMetadataForm form, CancellationToken cancellationToken);
    Task<PhotoReclassificationResult> ReclassifySamplePhotoAsync(long sampleId, long photoId, string targetPhotoType, CancellationToken cancellationToken);
    Task<string?> RemoveReceiptPhotoAsync(long receiptId, long photoId, CancellationToken cancellationToken);
    Task<string?> RemoveSamplePhotoAsync(long sampleId, long photoId, CancellationToken cancellationToken);
    Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, CancellationToken cancellationToken);
    Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, string? facility, CancellationToken cancellationToken);
    Task<RoomDetailViewModel> GetRoomDetailAsync(int roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomLotSummaryViewModel>> GetAuthoritativeCurrentRoomLotsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken);
    Task<BinsRunProjectionViewModel> GetRoomProjectionAsync(int roomId, RoomProjectionRequest request, CancellationToken cancellationToken);
    Task<RoomCountBreakdownViewModel> GetRoomCountBreakdownAsync(int roomId, CancellationToken cancellationToken);
    Task<string?> CreateRoomDepletionAsync(RoomDepletionForm form, CancellationToken cancellationToken);
    Task<string?> VoidRoomDepletionAsync(VoidRoomDepletionForm form, CancellationToken cancellationToken);
    Task<string?> CreateRoomInventoryTrueUpAsync(RoomInventoryTrueUpForm form, CancellationToken cancellationToken);
    Task<string?> CreateRoomTransferAsync(RoomTransferForm form, CancellationToken cancellationToken);
    Task<string?> ReverseRoomTransferAsync(ReverseRoomTransferForm form, CancellationToken cancellationToken);
}

public enum FruitRowEntryStatus
{
    Empty,
    InProgress,
    Complete
}

public static class FruitRowEntryStatusExtensions
{
    public static string ToDisplayName(this FruitRowEntryStatus status) => status switch
    {
        FruitRowEntryStatus.Empty => "Empty",
        FruitRowEntryStatus.InProgress => "In Progress",
        FruitRowEntryStatus.Complete => "Complete",
        _ => status.ToString()
    };
}

public sealed class DashboardDataService(
    CropQcDbContext dbContext,
    IFileStorageService fileStorageService,
    FileStorageOptions fileStorageOptions,
    EmailOptions emailOptions,
    IQcEmailRecipientResolver qcEmailRecipientResolver,
    GoogleAuthenticationOptions authOptions,
    IGoogleCredentialStore googleCredentialStore,
    IQcEmailSender emailSender,
    IQcPhotoRequirementPolicy photoRequirementPolicy,
    IQcSummaryEmailComposer emailComposer,
    ICropYearService cropYearService,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<DashboardDataService> logger,
    IUserAccessService? userAccessService = null,
    ICanonicalGrowerService? canonicalGrowerService = null,
    IBusinessTimeService? businessTime = null,
    IFacilityContextService? facilityContextService = null,
    IRoomInventoryLedgerQueryService? roomInventoryLedgerQueryService = null,
    IInventoryDeductionInvariantService? inventoryDeductionInvariantService = null,
    IVarietyColorService? varietyColorService = null,
    IRoomInventoryLossService? roomInventoryLossService = null,
    IRoomTreatmentService? roomTreatmentService = null,
    IReviewedGrowerLotPolicy? reviewedGrowerLotPolicy = null) : IDashboardDataService
{
    private const string SharedDriveQuotaGuidance = "The configured Google Drive folder is not being treated as a Shared Drive upload target. Confirm GoogleDrive__UseSharedDrive=true, GoogleDrive__RootFolderId is a folder inside the Shared Drive, GoogleDrive__SharedDriveId is set, and the service account has Content Manager access.";
    private const int MaximumLotEvidenceLinks = 8;
    private const int MaximumCurrentStorageSourceRows = RoomInventoryLedgerQueryService.MaximumRoomLotRows * 5;
    private const string TransferInventoryReconciliationError = "Transfer inventory does not reconcile with the Room's current inventory. No transfer was recorded. Refresh or review inventory reconciliation.";
    private static readonly string[] ReceiptTypeOptions = ["Truck receipt", "Door sample", "Lot sample"];
    private IBusinessTimeService BusinessTime { get; } = businessTime ?? new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock());
    private IFacilityContextService FacilityContext { get; } = facilityContextService ?? new FacilityContextService(dbContext);
    private IRoomInventoryLedgerQueryService RoomInventoryLedger { get; } =
        roomInventoryLedgerQueryService ?? new RoomInventoryLedgerQueryService(dbContext);
    private IInventoryDeductionInvariantService InventoryInvariant { get; } =
        inventoryDeductionInvariantService
        ?? new InventoryDeductionInvariantService(dbContext, NullLogger<InventoryDeductionInvariantService>.Instance);
    private IRoomInventoryLossService? RoomInventoryLosses { get; } = roomInventoryLossService;
    private IRoomTreatmentService? RoomTreatments { get; } = roomTreatmentService;

    private async Task<IReadOnlyList<GrowerLot>> GetReceivingGrowerLotsAsync(CancellationToken cancellationToken) =>
        reviewedGrowerLotPolicy is null
            ? await dbContext.GrowerLots.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Grower).ThenBy(x => x.LotNumber).ToListAsync(cancellationToken)
            : await reviewedGrowerLotPolicy.GetAlignedActiveGrowerLotsAsync(cancellationToken);

    private async Task<GrowerLot?> GetReceivingGrowerLotAsync(int id, CancellationToken cancellationToken) =>
        reviewedGrowerLotPolicy is null
            ? await dbContext.GrowerLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken)
            : (await reviewedGrowerLotPolicy.GetAlignedActiveGrowerLotsAsync(cancellationToken)).SingleOrDefault(x => x.Id == id);

    public async Task<HomeDashboardViewModel> GetHomeDashboardAsync(RoomSummaryFilterForm? roomSummaryFilter, CancellationToken cancellationToken)
    {
        var normalizedRoomFilter = NormalizeRoomSummaryFilter(roomSummaryFilter);
        var activeCropYear = cropYearService.GetCurrentCropYear(BusinessTime.NowPacific);
        IReadOnlyList<SampleListItemViewModel> todaySamples = [];
        string? qcWarning = null;
        try
        {
            var todayRange = BusinessTime.UtcRangeForPacificDate(BusinessTime.PacificDate(BusinessTime.UtcNow));
            todaySamples = (await BuildTodayDashboardSamplesAsync(todayRange, cancellationToken))
                .Where(x => FacilityContext.Matches(x.Warehouse, x.Warehouse, normalizedRoomFilter.Facility))
                .ToList();
        }
        catch (Exception ex)
        {
            qcWarning = DatabaseWarning(
                ex,
                "Home dashboard QC",
                "Today's QC cards and samples could not be loaded. Other dashboard sections remain available.");
        }

        IReadOnlyList<DashboardInventorySnapshot> dashboardLots = [];
        IReadOnlyList<RoomSummaryItemViewModel> roomSummaries = [];
        string? inventoryWarning = null;
        try
        {
            dashboardLots = (await BuildDashboardCurrentInventorySnapshotsAsync(null, cancellationToken))
                .Where(x => x.CurrentBins > 0 && FacilityContext.Matches(x.Facility, x.Facility, normalizedRoomFilter.Facility))
                .ToList();
            roomSummaries = await BuildDashboardRoomSummariesAsync(dashboardLots, normalizedRoomFilter, cancellationToken);
        }
        catch (Exception ex)
        {
            inventoryWarning = DatabaseWarning(
                ex,
                "Home dashboard room inventory",
                "Room inventory cards and summaries could not be loaded. Today's QC cards and samples remain available.");
        }

        var totalCurrentBins = dashboardLots.Sum(x => x.CurrentBins);
        var currentGrowerLots = dashboardLots.Select(x => CurrentDashboardLotKey(x.RoomId, x.Lot, x.Variety)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new HomeDashboardViewModel
        {
            ActiveCropYear = activeCropYear,
            DataWarning = JoinWarnings(qcWarning, inventoryWarning),
            Cards = BuildHomeCards(
                todaySamples.Count(x => x.SampleType.Contains("Receiving", StringComparison.OrdinalIgnoreCase)),
                todaySamples.Count(IsReadyToEmail),
                todaySamples.Count(x => !x.IsReady),
                todaySamples.Count(x => x.EmailStatus == "Sent"),
                todaySamples.Count(x => x.ReviewReasons.Count > 0),
                totalCurrentBins,
                currentGrowerLots,
                normalizedRoomFilter.Facility),
            TodaySamples = todaySamples,
            RoomSummaryFilter = normalizedRoomFilter,
            RoomSummaries = roomSummaries,
            CanManageRoomSeals = RoomSealingService.CanManage(httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal()),
            StorageByFacility = dashboardLots
                .GroupBy(x => x.Facility)
                .Select(x => new StorageFacilitySummaryViewModel
                {
                    Facility = x.Key,
                    CurrentBins = x.Sum(y => y.CurrentBins),
                    CurrentGrowerLots = x.Select(y => CurrentDashboardLotKey(y.RoomId, y.Lot, y.Variety)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CurrentRooms = x.Select(y => y.RoomId).Distinct().Count()
                })
                .OrderBy(x => x.Facility)
                .ToList()
        };
    }

    public async Task<IReadOnlyList<RoomLotSummaryViewModel>> GetAuthoritativeCurrentRoomLotsAsync(
        IReadOnlyCollection<int> roomIds,
        CancellationToken cancellationToken) =>
        (await BuildRoomLotSummariesAsync(null, cancellationToken, roomIds))
        .Where(x => x.CurrentBins > 0)
        .OrderBy(x => x.RoomId)
        .ThenBy(x => x.CanonicalVarietyName)
        .ThenBy(x => x.ProductionType)
        .ThenBy(x => x.IsOrganic)
        .ThenBy(x => x.GrowerNumber)
        .ThenBy(x => x.GrowerLotId)
        .ThenBy(x => x.InventoryKey)
        .ToList();

    public async Task<RoomsPageViewModel> GetRoomsAsync(RoomSummaryFilterForm? roomSummaryFilter, CancellationToken cancellationToken)
    {
        var normalizedRoomFilter = NormalizeRoomSummaryFilter(roomSummaryFilter);
        normalizedRoomFilter.RoomStatus = string.IsNullOrWhiteSpace(roomSummaryFilter?.RoomStatus) ? "All" : normalizedRoomFilter.RoomStatus;
        try
        {
            IReadOnlyList<RoomSummaryItemViewModel> rooms;
            if (normalizedRoomFilter.RoomStatus.Equals("WithFruit", StringComparison.OrdinalIgnoreCase))
            {
                var currentLots = (await BuildDashboardCurrentInventorySnapshotsAsync(null, cancellationToken))
                    .Where(x => x.CurrentBins > 0 && FacilityContext.Matches(x.Facility, x.Facility, normalizedRoomFilter.Facility))
                    .ToList();
                rooms = await BuildDashboardRoomSummariesAsync(currentLots, normalizedRoomFilter, cancellationToken);
            }
            else
            {
                rooms = await BuildRoomSummariesAsync(cancellationToken, roomSummaryFilter: normalizedRoomFilter);
            }

            return new RoomsPageViewModel
            {
                Filter = normalizedRoomFilter,
                Rooms = rooms,
                CanApplyTreatment = await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken),
                CanManageRoomSeals = RoomSealingService.CanManage(httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal())
            };
        }
        catch (Exception ex)
        {
            return new RoomsPageViewModel { Filter = normalizedRoomFilter, DataWarning = DatabaseWarning(ex, "Rooms dashboard") };
        }
    }

    private IReadOnlyList<StatusCountCard> BuildHomeCards(int todaySamples, int ready, int missing, int sent, int review, int currentBins, int currentGrowerLots, string facility)
    {
        var encodedFacility = Uri.EscapeDataString(FacilityContext.Normalize(facility));
        return
        [
            new("Total Bins In Storage", currentBins, $"/Inventory/ByVariety?Facility={encodedFacility}", "ready", "Current bins grouped by canonical variety."),
            new("Grower Lots In Storage", currentGrowerLots, $"/GrowerLots/Current?Facility={encodedFacility}", "info", "Grower lots with fruit currently left in storage."),
            new("Today's Receiving Samples", todaySamples, $"/Receipts?Facility={encodedFacility}&DateFilter=today&SampleType=Receiving", "info", "Receipts with receiving QC activity today."),
            new("Samples Ready to Email", ready, $"/DailyQc?Facility={encodedFacility}&status=ReadyToSend", "ready", "Samples with required data and photos ready for QC summary email."),
            new("QC Emails Sent", sent, $"/DailyQc?Facility={encodedFacility}&status=Sent", "sent", "Samples with a QC Summary email sent today."),
            new("Samples Missing Data", missing, $"/DailyQc?Facility={encodedFacility}&status=MissingData", "missing", "Samples missing required fields/photos."),
            new("Samples Needing Review", review, $"/DailyQc?Facility={encodedFacility}&status=NeedsReview", "review", "Samples with pressure, starch, defect, or variance review flags.")
        ];
    }

    public async Task<CurrentGrowerLotsPageViewModel> GetCurrentGrowerLotsAsync(CurrentGrowerLotsFilterForm filter, CancellationToken cancellationToken)
    {
        try
        {
            filter.Facility = FacilityContext.Normalize(filter.Facility);
            filter.CropYear ??= cropYearService.GetCurrentCropYear(BusinessTime.NowPacific);
            var facilityWarehouseIds = await FacilityContext.GetWarehouseIdsAsync(filter.Facility, cancellationToken);
            var scopedRoomQuery = dbContext.Rooms.AsNoTracking().Where(x => facilityWarehouseIds.Contains(x.WarehouseId));
            if (filter.WarehouseId is not null)
            {
                scopedRoomQuery = scopedRoomQuery.Where(x => x.WarehouseId == filter.WarehouseId.Value);
            }
            if (filter.RoomId is not null)
            {
                scopedRoomQuery = scopedRoomQuery.Where(x => x.Id == filter.RoomId.Value);
            }
            var scopedRoomIds = await scopedRoomQuery.Select(x => x.Id).ToListAsync(cancellationToken);
            var canonicalVarietyFilter = await BuildCanonicalVarietyFilterAsync(filter.Variety, cancellationToken);
            var reconciledLots = await BuildRoomLotSummariesAsync(
                    null,
                    cancellationToken,
                    scopedRoomIds,
                    filter.CropYear,
                    canonicalVarietyFilter);
            var currentCandidates = reconciledLots
                .Where(x => x.CurrentBins > 0)
                .ToList();
            await DecorateReceiptProvenanceAsync(currentCandidates, cancellationToken);
            var currentLots = currentCandidates
                .Where(x => CurrentLotMatchesFilter(x, filter, canonicalVarietyFilter))
                .ToList();

            await DecorateCurrentRoomLotsAsync(
                currentLots,
                new Dictionary<string, RoomLotProjectionDistribution>(StringComparer.OrdinalIgnoreCase),
                cancellationToken);
            var rows = currentLots.Select(ToCurrentGrowerLot).OrderBy(x => x.GrowerNumber).ThenBy(x => x.Lot).ThenBy(x => x.Room).ToList();
            var growers = BuildCurrentStorageGrowers(currentLots);

            return new CurrentGrowerLotsPageViewModel
            {
                Filter = filter,
                Lots = rows,
                Growers = growers,
                CropYears = await cropYearService.GetAvailableCropYearsAsync(cancellationToken),
                Warehouses = (await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken))
                    .Where(x => FacilityContext.Matches(x.Code, x.Name, filter.Facility)).ToList(),
                Rooms = (await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).OrderBy(x => x.WarehouseId).ThenBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(cancellationToken))
                    .Where(x => FacilityContext.Matches(x.Warehouse.Code, x.Warehouse.Name, filter.Facility)).ToList(),
                GrowerOptions = rows.SelectMany(x => new[] { x.Grower, x.GrowerNumber }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                Varieties = rows.Select(x => x.Variety).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
            };
        }
        catch (Exception ex)
        {
            var referenceId = Guid.NewGuid().ToString("N")[..10];
            logger.LogError(ex, "Current Grower Lots page failed. Reference {ReferenceId}.", referenceId);
            return new CurrentGrowerLotsPageViewModel
            {
                Filter = filter,
                DataWarning = $"Current Lots could not fully load. Reference {referenceId}. The full exception was logged without exposing secrets."
            };
        }
    }

    public async Task<CropYearReviewPageViewModel> GetCropYearReviewAsync(CropYearReviewFilterForm filter, CancellationToken cancellationToken)
    {
        try
        {
            filter.CropYear ??= cropYearService.GetCurrentCropYear(BusinessTime.NowPacific);
            var query = QuerySamples().Where(x => x.Receipt.CropYear == filter.CropYear);
            if (filter.WarehouseId is not null) query = query.Where(x => x.Receipt.WarehouseId == filter.WarehouseId);
            if (!string.IsNullOrWhiteSpace(filter.Variety)) query = query.Where(x => x.Receipt.FruitProfile.VarietyCode.Contains(filter.Variety));

            var samples = await query.OrderBy(x => x.SampleTakenAt).Take(1000).ToListAsync(cancellationToken);
            var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
            var pressureByLot = samples
                .GroupBy(x => QcConditionLotKey(x.Receipt), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Select(s => new { Sample = s, Pressure = AverageOrNull(PressureValues([s]).ToList()) }).Where(y => y.Pressure is not null).OrderBy(y => y.Sample.SampleTakenAt).ToList(), StringComparer.OrdinalIgnoreCase);

            var sampleRows = samples.Select(sample =>
            {
                var pressures = PressureValues([sample]).ToList();
                var starch = StarchValues([sample]).ToList();
                var key = QcConditionLotKey(sample.Receipt);
                pressureByLot.TryGetValue(key, out var lotPressures);
                var first = lotPressures?.FirstOrDefault();
                var last = lotPressures?.LastOrDefault();
                var days = first is not null && last is not null ? Math.Max(0, (int)(last.Sample.SampleTakenAt.Date - first.Sample.SampleTakenAt.Date).TotalDays) : (int?)null;
                var change = first?.Pressure is not null && last?.Pressure is not null ? decimal.Round(last.Pressure.Value - first.Pressure.Value, 2) : (decimal?)null;
                return new CropYearReviewRowViewModel
                {
                    SampleDate = sample.SampleTakenAt,
                    Grower = sample.Receipt.GrowerName,
                    Lot = ReceiptLotNumber(sample.Receipt),
                    Variety = sample.Receipt.FruitProfile.VarietyCode,
                    Warehouse = sample.Receipt.Warehouse.Code,
                    Room = sample.Receipt.Room.CropQcRoomName ?? sample.Receipt.Room.DisplayName ?? sample.Receipt.Room.Code,
                    SampleType = sample.SampleType.Name,
                    AveragePressure = AverageOrNull(pressures),
                    PressureStdDev = StandardDeviationOrNull(pressures),
                    StarchAverage = AverageOrNull(starch),
                    EnteredFruitCount = sample.FruitReadings.Count(HasEnteredFruitData),
                    EarliestPressure = first?.Pressure,
                    LatestPressure = last?.Pressure,
                    PressureChange = change,
                    DaysBetweenSamples = days,
                    PressureLossPerWeek = days is > 0 && change < 0 ? decimal.Round(Math.Abs(change.Value) / days.Value * 7m, 2) : null
                };
            }).ToList();

            var growerRows = samples.Zip(sampleRows, (sample, row) => new
            {
                Sample = sample,
                Row = row,
                Identity = growerResolver.Resolve(sample.Receipt.GrowerName, sample.Receipt.GrowerNumber)
            })
                .ToList();

            if (!string.IsNullOrWhiteSpace(filter.Grower))
            {
                var search = filter.Grower.Trim();
                growerRows = growerRows
                    .Where(x => ContainsIgnoreCase(x.Identity.DisplayName, search)
                        || ContainsIgnoreCase(x.Sample.Receipt.GrowerName, search)
                        || ContainsIgnoreCase(x.Sample.Receipt.GrowerNumber, search)
                        || ContainsIgnoreCase(x.Sample.Receipt.LotCode, search)
                        || ContainsIgnoreCase(string.Join(" ", x.Identity.Key.Split('_')), search))
                    .ToList();
            }

            var growers = growerRows
                .GroupBy(x => x.Identity.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var rows = group.Select(x => x.Row).OrderByDescending(x => x.SampleDate).ToList();
                    var receipts = group.Select(x => x.Sample.Receipt).DistinctBy(x => x.Id).ToList();
                    return new CropYearReviewGrowerViewModel
                    {
                        CanonicalGrowerKey = group.First().Identity.Key,
                        CanonicalGrowerName = group.First().Identity.DisplayName,
                        IsMapped = group.First().Identity.IsMapped,
                        GrowerNumbers = receipts.Select(x => x.GrowerNumber ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                        SourceGrowerNames = receipts.Select(x => x.GrowerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                        SourceGrowerName = receipts.Select(x => x.GrowerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                        SourceGrowerNumber = receipts.Select(x => x.GrowerNumber ?? "").FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                        SourceFacility = receipts.Select(x => x.Warehouse.Code).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                        SourceIdentityCount = receipts.Select(x => $"{x.GrowerName}|{x.GrowerNumber}".ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        TotalReceipts = receipts.Count,
                        TotalLots = receipts.Select(x => ReceiptLotNumber(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        TotalBinsReceived = receipts.Sum(x => x.BinCount),
                        QcSampleCount = group.Select(x => x.Sample.Id).Distinct().Count(),
                        Varieties = receipts.Select(x => x.FruitProfile.VarietyCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                        Warehouses = receipts.Select(x => x.Warehouse.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                        FirstSampleDate = rows.LastOrDefault()?.SampleDate,
                        LastSampleDate = rows.FirstOrDefault()?.SampleDate,
                        AveragePressure = AverageOrNull(rows.Where(x => x.AveragePressure is not null).Select(x => x.AveragePressure!.Value).ToList()),
                        LatestPressure = rows.Where(x => x.AveragePressure is not null).OrderByDescending(x => x.SampleDate).Select(x => x.AveragePressure).FirstOrDefault(),
                        StarchAverage = AverageOrNull(rows.Where(x => x.StarchAverage is not null).Select(x => x.StarchAverage!.Value).ToList()),
                        Rows = rows
                    };
                })
                .OrderBy(x => x.CanonicalGrowerName)
                .ThenBy(x => x.CanonicalGrowerKey)
                .ToList();

            return new CropYearReviewPageViewModel
            {
                Filter = filter,
                Growers = growers,
                CropYears = await cropYearService.GetAvailableCropYearsAsync(cancellationToken),
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken),
                GrowerOptions = growers
                    .SelectMany(x => new[] { x.CanonicalGrowerName }.Concat(x.SourceGrowerNames).Concat(x.GrowerNumbers))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList(),
                Varieties = growers.SelectMany(x => x.Varieties).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CropYearReviewPageViewModel { Filter = filter, DataWarning = DatabaseWarning(ex, "Crop year review") };
        }
    }

    public async Task<RoomDetailViewModel> GetRoomDetailAsync(int roomId, CancellationToken cancellationToken)
    {
        try
        {
            var summaries = await BuildRoomSummariesAsync(cancellationToken, roomId);
            var summary = summaries.SingleOrDefault();
            if (summary is null)
            {
                return new RoomDetailViewModel { DataWarning = "Room not found." };
            }

            var lotSummaries = await BuildRoomLotSummariesAsync(roomId, cancellationToken);
            var activeLots = lotSummaries.Where(x => x.CurrentBins > 0).ToList();
            var depletedLots = lotSummaries.Where(x => x.CurrentBins <= 0 && x.OriginalBins > 0).ToList();
            var depletions = await BuildRoomDepletionHistoryAsync(roomId, cancellationToken);
            var inventoryAdjustments = await BuildRoomInventoryAdjustmentHistoryAsync(roomId, cancellationToken);
            var linkedReceipts = await BuildRoomLinkedReceiptsAsync(roomId, cancellationToken);
            var transferDestinations = await BuildRoomTransferDestinationsAsync(roomId, cancellationToken);
            var sampleDistributions = await BuildRoomProjectionSampleDataAsync(activeLots, cancellationToken);
            await DecorateCurrentRoomLotsAsync(activeLots, sampleDistributions, cancellationToken);
            var likelySourceReceipts = await DecorateReceiptProvenanceAsync(activeLots, cancellationToken);
            var currentGrowers = BuildRoomGrowerSummaries(activeLots);
            var canManage = await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
            var canManageRoomSeals = RoomSealingService.CanManage(httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
            var sealHistory = await dbContext.RoomSealEvents.AsNoTracking()
                .Where(x => x.RoomId == roomId)
                .OrderByDescending(x => x.ChangedAt)
                .Take(50)
                .Select(x => new RoomSealHistoryItemViewModel(
                    x.Action,
                    x.EffectiveAt,
                    x.PreviousEffectiveAt,
                    x.ChangedAt,
                    x.ChangedByUser.DisplayName,
                    x.Note))
                .ToListAsync(cancellationToken);
            var inventoryLossData = RoomInventoryLosses is null
                ? new RoomInventoryLossPageData([], [], false, false)
                : await RoomInventoryLosses.GetRoomDataAsync(roomId, cancellationToken);
            var treatmentData = new RoomTreatmentData([], [], false, false);
            var transferProjection = RoomTransferInventoryProjection.Failed(activeLots.Sum(x => x.CurrentBins), 0);
            string? treatmentWarning = null;
            try
            {
                if (RoomTreatments is not null)
                {
                    treatmentData = await RoomTreatments.GetRoomDataAsync(roomId, cancellationToken);
                }
                transferProjection = await BuildTreatmentTransferProjectionAsync(activeLots, roomId, cancellationToken);
                if (!transferProjection.Reconciles)
                {
                    treatmentWarning = transferProjection.Error;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                treatmentWarning = DatabaseWarning(
                    ex,
                    "Room treatment transfer projection",
                    "Current inventory is shown, but exact treatment/source allocation could not be resolved. Transfer is disabled for this room until the inventory identity is reviewed.");
            }

            return new RoomDetailViewModel
            {
                Summary = summary,
                CurrentLots = activeLots,
                CurrentGrowers = currentGrowers,
                DepletedLots = depletedLots,
                Depletions = depletions,
                InventoryAdjustments = inventoryAdjustments,
                LinkedReceipts = linkedReceipts,
                LikelySourceReceipts = likelySourceReceipts,
                BaselineProjection = BuildRoomProjection(activeLots, sampleDistributions, isSelection: false),
                ProjectionLots = BuildRoomProjectionLots(activeLots, sampleDistributions, BusinessTime.NowPacific),
                SampleTimeline = await BuildRoomSampleTimelineAsync(roomId, cancellationToken),
                DepletionReceiptOptions = activeLots
                    .Where(x => x.ReceiptId is not null)
                    .Select(x => new RoomReceiptOptionViewModel(x.ReceiptId!.Value, $"{x.DisplayReceiptId} - {x.GrowerName} {x.LotCode} {x.VarietyCode} ({x.CurrentBins} bins current)", x.CurrentBins))
                    .ToList(),
                TransferLotOptions = transferProjection.Options,
                TransferCurrentRoomBins = transferProjection.CurrentRoomBins,
                TransferAvailableBins = transferProjection.AvailableBins,
                TransferInventoryReconciles = transferProjection.Reconciles,
                TransferInventoryError = transferProjection.Error,
                TransferDestinationFacilities = transferDestinations.Facilities,
                TransferDestinationOptions = transferDestinations.Rooms,
                DepletionForm = new RoomDepletionForm { RoomId = roomId, DepletedAt = BusinessTime.NowPacific },
                TrueUpForm = new RoomInventoryTrueUpForm { RoomId = roomId, AdjustmentAt = BusinessTime.NowPacific },
                TransferForm = new RoomTransferForm
                {
                    FromRoomId = roomId,
                    DestinationWarehouseId = transferDestinations.SourceWarehouseId,
                    TransferAt = BusinessTime.NowPacific
                },
                CanManageDepletions = canManage,
                InventoryLossOptions = inventoryLossData.Options,
                InventoryLosses = inventoryLossData.History,
                InventoryLossForm = new RoomInventoryLossForm { RoomId = roomId, OccurredAt = BusinessTime.NowPacific },
                CanRecordInventoryLoss = inventoryLossData.CanRecord,
                CanReverseInventoryLoss = inventoryLossData.CanReverse
                ,
                CurrentTreatmentStatus = treatmentData.Current
                ,
                TreatmentApplicationHistory = treatmentData.History
                ,
                CanApplyTreatment = treatmentData.CanApply
                ,
                CanReverseTreatment = treatmentData.CanReverse
                ,
                CanManageRoomSeals = canManageRoomSeals
                ,
                SealHistory = sealHistory
                ,
                DataWarning = treatmentWarning
            };
        }
        catch (Exception ex)
        {
            return new RoomDetailViewModel { DataWarning = DatabaseWarning(ex, "Room detail") };
        }
    }

    public async Task<BinsRunProjectionViewModel> GetRoomProjectionAsync(int roomId, RoomProjectionRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.Rooms, PageAccessLevel.View, cancellationToken))
        {
            throw new InvalidOperationException("Room view access is required.");
        }

        var activeLots = (await BuildRoomLotSummariesAsync(roomId, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .ToList();
        var selectedKeys = request.InventoryKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lots = activeLots;
        var isSelection = selectedKeys.Count > 0;
        if (isSelection)
        {
            var byKey = activeLots.ToDictionary(RoomProjectionInventoryKey, StringComparer.OrdinalIgnoreCase);
            if (selectedKeys.Any(x => !byKey.ContainsKey(x)))
            {
                throw new InvalidOperationException("Selected inventory is not available in this room.");
            }

            lots = selectedKeys.Select(x => byKey[x]).ToList();
        }

        var sampleDistributions = await BuildRoomProjectionSampleDataAsync(activeLots, cancellationToken);
        return BuildRoomProjection(lots, sampleDistributions, isSelection);
    }

    public async Task<RoomCountBreakdownViewModel> GetRoomCountBreakdownAsync(int roomId, CancellationToken cancellationToken)
    {
        try
        {
            var summaries = await BuildRoomSummariesAsync(cancellationToken, roomId);
            var summary = summaries.SingleOrDefault();
            if (summary is null)
            {
                return new RoomCountBreakdownViewModel { DataWarning = "Room not found." };
            }

            return new RoomCountBreakdownViewModel
            {
                Summary = summary,
                Rows = await BuildRoomCountBreakdownRowsAsync(roomId, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            return new RoomCountBreakdownViewModel { DataWarning = DatabaseWarning(ex, "Room count breakdown") };
        }
    }

    public async Task<string?> CreateRoomDepletionAsync(RoomDepletionForm form, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken))
        {
            return "Room Transactions Edit access is required to record room depletion.";
        }

        if (form.BinCount <= 0)
        {
            return "Bin count must be positive.";
        }

        await using var transaction = await BeginInventoryTransactionIfSupportedAsync(cancellationToken);
        var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [form.RoomId], [], BusinessTime, cancellationToken);
        if (sealError is not null) return sealError;
        var receipt = await dbContext.Receipts
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
                .ThenInclude(x => x!.CanonicalOrchard)
            .SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null || receipt.RoomId != form.RoomId)
        {
            return "Room lot was not found.";
        }

        var currentBins = await GetCurrentBinsForReceiptAsync(receipt.Id, cancellationToken);
        if (form.BinCount > currentBins && !form.ConfirmOverDepletion)
        {
            return $"Cannot deplete {form.BinCount} bins because only {currentBins} bins are currently known in this room. Confirm override if the current bin count is unknown or needs correction.";
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var depletion = new RoomDepletion
        {
            ReceiptId = receipt.Id,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotCode = receipt.LotCode,
            BinCountDepleted = form.BinCount,
            Destination = string.IsNullOrWhiteSpace(form.Destination) ? null : form.Destination.Trim(),
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            DepletedAt = form.DepletedAt,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.RoomDepletions.Add(depletion);
        await dbContext.SaveChangesAsync(cancellationToken);
        var adjustment = AddRoomInventoryAdjustment(
            receipt,
            currentUser,
            "Depletion",
            oldBinCount: currentBins,
            changeAmount: -form.BinCount,
            newBinCount: Math.Max(0, currentBins - form.BinCount),
            adjustmentAt: form.DepletedAt,
            reason: "Bins sent to line",
            notes: depletion.Notes,
            roomDepletionId: depletion.Id);
        adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        adjustment.InventoryOperationKey = $"room-depletion:{depletion.Id}:depletion";
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.BinsRunEntries.Add(new BinsRunEntry
        {
            ReceiptId = receipt.Id,
            SourceInventoryAdjustmentId = null,
            InventoryAdjustmentId = adjustment.Id,
            InventoryAdjustment = adjustment,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            CropYear = receipt.CropYear,
            GrowerLotId = receipt.GrowerLotId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode,
            PoolStart = receipt.PoolStart,
            VarietyCode = receipt.FruitProfile.VarietyCode,
            PreviousAvailableBins = currentBins,
            BinsRun = form.BinCount,
            NewAvailableBins = Math.Max(0, currentBins - form.BinCount),
            Notes = depletion.Notes,
            RunAt = form.DepletedAt,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            TransactionType = ActualRunTransactionTypes.Legacy
        });
        await AddAuditAsync("Create", nameof(RoomDepletion), depletion.Id.ToString(), currentUser?.Email ?? "unknown", null, $"Receipt {receipt.CompuTechReceiptId}; {form.BinCount} bins depleted from {receipt.Warehouse.Code}/{receipt.Room.Code}.", cancellationToken);
        await AddAuditAsync("BinCountChange", nameof(RoomInventoryAdjustment), receipt.Id.ToString(), currentUser?.Email ?? "unknown", null, $"Depletion changed bins from {currentBins} to {Math.Max(0, currentBins - form.BinCount)}.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    public async Task<string?> VoidRoomDepletionAsync(VoidRoomDepletionForm form, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken))
        {
            return "Room Transactions Admin access is required to void room depletion records.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Void reason is required.";
        }

        await using var transaction = await BeginInventoryTransactionIfSupportedAsync(cancellationToken);
        var depletion = await dbContext.RoomDepletions
            .Include(x => x.Receipt)
                .ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt)
                .ThenInclude(x => x.Room)
            .Include(x => x.Receipt)
                .ThenInclude(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.DepletionId && x.RoomId == form.RoomId, cancellationToken);
        if (depletion is null)
        {
            return "Depletion record not found.";
        }

        if (depletion.IsVoided)
        {
            return null;
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var currentBeforeVoid = await GetCurrentBinsForReceiptAsync(depletion.ReceiptId, cancellationToken);
        depletion.IsVoided = true;
        depletion.VoidedAt = DateTimeOffset.UtcNow;
        depletion.VoidedByUserId = currentUser?.Id;
        depletion.VoidReason = form.Reason.Trim();
        var originalAdjustment = await dbContext.RoomInventoryAdjustments
            .SingleOrDefaultAsync(x => x.RoomDepletionId == depletion.Id && x.ChangeAmount < 0, cancellationToken);
        var originalEntry = originalAdjustment is null
            ? null
            : await dbContext.BinsRunEntries.SingleOrDefaultAsync(x => x.InventoryAdjustmentId == originalAdjustment.Id, cancellationToken);
        if (originalEntry is null)
        {
            return "This legacy depletion has no persisted Bins Run parent and cannot be reversed automatically.";
        }
        if (originalEntry.IsReversed)
        {
            return null;
        }
        var reversalAdjustment = AddRoomInventoryAdjustment(
            depletion.Receipt,
            currentUser,
            "Void/Reversal",
            oldBinCount: currentBeforeVoid,
            changeAmount: depletion.BinCountDepleted,
            newBinCount: currentBeforeVoid + depletion.BinCountDepleted,
            adjustmentAt: DateTimeOffset.UtcNow,
            reason: depletion.VoidReason,
            notes: $"Voided depletion {depletion.Id}.",
            roomDepletionId: depletion.Id);
        reversalAdjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        reversalAdjustment.InventoryOperationKey = $"room-depletion:{depletion.Id}:reversal";
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.BinsRunEntries.Add(new BinsRunEntry
        {
            ReceiptId = originalEntry.ReceiptId,
            SourceInventoryAdjustmentId = originalEntry.SourceInventoryAdjustmentId,
            InventoryAdjustmentId = reversalAdjustment.Id,
            InventoryAdjustment = reversalAdjustment,
            WarehouseId = originalEntry.WarehouseId,
            RoomId = originalEntry.RoomId,
            CropYear = originalEntry.CropYear,
            GrowerLotId = originalEntry.GrowerLotId,
            FruitProfileId = originalEntry.FruitProfileId,
            GrowerName = originalEntry.GrowerName,
            LotNumber = originalEntry.LotNumber,
            PoolStart = originalEntry.PoolStart,
            VarietyCode = originalEntry.VarietyCode,
            InventoryStatus = originalEntry.InventoryStatus,
            PreviousAvailableBins = currentBeforeVoid,
            BinsRun = originalEntry.BinsRun,
            NewAvailableBins = currentBeforeVoid + originalEntry.BinsRun,
            Notes = depletion.VoidReason,
            RunAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            TransactionType = ActualRunTransactionTypes.Reversal,
            ReversesBinsRunEntryId = originalEntry.Id
        });
        originalEntry.IsReversed = true;
        originalEntry.ReversedAt = DateTimeOffset.UtcNow;
        originalEntry.ReversedByUserId = currentUser?.Id;
        originalEntry.ReverseReason = depletion.VoidReason;
        await AddAuditAsync("Void", nameof(RoomDepletion), depletion.Id.ToString(), currentUser?.Email ?? "unknown", null, $"Voided depletion. Reason: {depletion.VoidReason}", cancellationToken);
        await AddAuditAsync("BinCountChange", nameof(RoomInventoryAdjustment), depletion.ReceiptId.ToString(), currentUser?.Email ?? "unknown", null, $"Void/Reversal changed bins from {currentBeforeVoid} to {currentBeforeVoid + depletion.BinCountDepleted}.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    public async Task<string?> CreateRoomInventoryTrueUpAsync(RoomInventoryTrueUpForm form, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken))
        {
            return "Room Transactions Admin access is required to true up room inventory.";
        }

        if (form.NewBinCount < 0)
        {
            return "Current bin count cannot be negative.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Reason is required for bin count true-up.";
        }

        var receipt = await dbContext.Receipts
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null || receipt.RoomId != form.RoomId)
        {
            return "Current lot was not found for this room.";
        }

        var snapshots = await RoomInventoryLedger.GetSnapshotsAsync(
            receipt.WarehouseId,
            [receipt.RoomId],
            receipt.FruitProfileId,
            cancellationToken);
        var receiptLot = string.IsNullOrWhiteSpace(receipt.GrowerNumber)
            ? receipt.LotCode
            : receipt.GrowerNumber;
        var matchingSnapshots = snapshots.Where(x =>
            x.CropYear == receipt.CropYear
            && x.GrowerLotId == receipt.GrowerLotId
            && x.FruitProfileId == receipt.FruitProfileId
            && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Variety, receipt.FruitProfile.VarietyCode, StringComparison.OrdinalIgnoreCase)
            && x.IsOrganic == receipt.FruitProfile.IsOrganic).ToList();
        if (matchingSnapshots.Count != 1)
        {
            return "The exact room inventory identity could not be resolved uniquely. Refresh the room before retrying.";
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var oldCount = matchingSnapshots[0].CurrentBins;
        var delta = form.NewBinCount - oldCount;
        if (delta < 0)
        {
            await AuditRejectedInventoryDeductionAsync(
                "ManualTrueUp",
                $"{form.RoomId}:{form.ReceiptId}",
                currentUser,
                $"Rejected requested reduction from {oldCount} to {form.NewBinCount}.",
                cancellationToken);
            return "Inventory reductions must be recorded through Dropped Bins, Bins Run, or Transfer. The true-up was not saved.";
        }
        await using var transaction = await BeginInventoryTransactionIfSupportedAsync(cancellationToken);
        if (RoomTreatments is not null && delta > 0)
        {
            var lineage = await RoomTreatments.AddUnknownAsync(
                matchingSnapshots[0],
                delta,
                $"manual-true-up:{form.OperationKey}:treatment",
                form.AdjustmentAt,
                currentUser?.Id,
                cancellationToken);
            if (!lineage.Success) return lineage.Error;
        }
        AddRoomInventoryAdjustment(
            receipt,
            currentUser,
            "ManualTrueUp",
            oldBinCount: oldCount,
            changeAmount: delta,
            newBinCount: form.NewBinCount,
            adjustmentAt: form.AdjustmentAt,
            reason: form.Reason.Trim(),
            notes: string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            roomDepletionId: null);
        await AddAuditAsync("BinCountChange", nameof(RoomInventoryAdjustment), receipt.Id.ToString(), currentUser?.Email ?? "unknown", null, $"ManualTrueUp changed bins from {oldCount} to {form.NewBinCount}. Reason: {form.Reason.Trim()}", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<string?> CreateRoomTransferAsync(RoomTransferForm form, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken))
        {
            return "Room Transactions Edit access is required to transfer room inventory.";
        }

        if (form.BinCount <= 0)
        {
            return "Transfer bin count must be positive.";
        }

        if (form.DestinationWarehouseId <= 0)
        {
            return "Select a destination facility.";
        }

        if (form.DestinationRoomId <= 0 || form.DestinationRoomId == form.FromRoomId)
        {
            return "Select a different destination room.";
        }

        if (string.IsNullOrWhiteSpace(form.SourceLotKey))
        {
            return "Select a source lot.";
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Reason is required for room transfers.";
        }

        var operationKey = string.IsNullOrWhiteSpace(form.OperationKey)
            ? Guid.NewGuid().ToString("N")
            : form.OperationKey.Trim();
        var existingTransfer = await dbContext.RoomTransfers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
        if (existingTransfer is not null)
        {
            var sameTransfer = existingTransfer.SourceRoomId == form.FromRoomId
                && existingTransfer.DestinationWarehouseId == form.DestinationWarehouseId
                && existingTransfer.DestinationRoomId == form.DestinationRoomId
                && existingTransfer.BinCount == form.BinCount
                && existingTransfer.TransferredAt.ToUniversalTime() == form.TransferAt.ToUniversalTime()
                && existingTransfer.Reason == form.Reason.Trim()
                && existingTransfer.Notes == (string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim());
            if (!sameTransfer)
            {
                return "The operation key already belongs to a different room transfer.";
            }

            if (RoomTreatments is null)
            {
                return null;
            }

            var existingLineage = await dbContext.TreatmentLineageMovements.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationKey == $"transfer:{operationKey}:treatment", cancellationToken);
            var sameTreatmentSelection = existingLineage is null
                ? form.TreatmentSegmentId is null && string.IsNullOrEmpty(form.TreatmentSignature)
                : string.Equals(existingLineage.TreatmentSignatureSnapshot, form.TreatmentSignature ?? "", StringComparison.Ordinal)
                    && (!form.TreatmentSegmentId.HasValue || existingLineage.SourceSegmentId == form.TreatmentSegmentId);
            return sameTreatmentSelection
                ? null
                : "The operation key already belongs to a transfer of a different treatment segment.";
        }

        await using var transaction = await BeginInventoryTransactionIfSupportedAsync(cancellationToken);
        var sealError = await RoomMovementSealGuard.ValidateAsync(
            dbContext,
            [form.FromRoomId],
            [form.DestinationRoomId],
            BusinessTime,
            cancellationToken);
        if (sealError is not null) return sealError;
        var sourceLots = (await BuildRoomLotSummariesAsync(form.FromRoomId, cancellationToken)).Where(x => x.CurrentBins > 0).ToList();
        var transferProjection = await BuildTreatmentTransferProjectionAsync(sourceLots, form.FromRoomId, cancellationToken);
        if (!transferProjection.Reconciles)
        {
            return TransferInventoryReconciliationError;
        }

        var selectedEntry = transferProjection.Entries.SingleOrDefault(x =>
            string.Equals(x.Option.LotKey, form.SourceLotKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Option.TreatmentSignature, form.TreatmentSignature ?? "", StringComparison.Ordinal)
            && x.Option.TreatmentSegmentId == form.TreatmentSegmentId);
        if (selectedEntry is null)
        {
            return "The selected source or treatment segment is no longer available. Refresh before retrying.";
        }
        if (form.BinCount > selectedEntry.Option.CurrentBins)
        {
            return $"Cannot transfer {form.BinCount} bins because only {selectedEntry.Option.CurrentBins} bins remain in the exact selected treatment segment. Refresh before retrying.";
        }

        var sourceLot = sourceLots.SingleOrDefault(x => string.Equals(RoomLotKey(x), form.SourceLotKey, StringComparison.OrdinalIgnoreCase));
        if (sourceLot is null)
        {
            return "Source lot was not found in this room.";
        }

        var destinationWarehouse = await dbContext.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == form.DestinationWarehouseId && x.IsActive, cancellationToken);
        if (destinationWarehouse is null)
        {
            return "The selected destination facility was not found or is inactive.";
        }

        var fromRoom = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == form.FromRoomId && x.IsActive && x.Warehouse.IsActive, cancellationToken);
        var toRoom = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == form.DestinationRoomId && x.IsActive, cancellationToken);
        if (fromRoom is null)
        {
            return "The source room was not found or is inactive.";
        }
        if (toRoom is null)
        {
            return "The selected destination room was not found or is inactive.";
        }
        if (toRoom.WarehouseId != destinationWarehouse.Id)
        {
            return "The selected destination room does not belong to the selected destination facility.";
        }
        if (toRoom.Id == fromRoom.Id)
        {
            return "Select a destination room different from the source room.";
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var reason = form.Reason.Trim();
        var notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        var sourceReceipt = selectedEntry.Option.TreatmentReceiptId is null
            ? null
            : await dbContext.Receipts.Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).SingleOrDefaultAsync(x => x.Id == selectedEntry.Option.TreatmentReceiptId && !x.IsDeleted, cancellationToken);
        var fruitProfile = await dbContext.FruitProfiles
            .AsNoTracking()
            .Where(x => sourceLot.FruitProfileId.HasValue
                ? x.Id == sourceLot.FruitProfileId.Value
                : !string.IsNullOrWhiteSpace(sourceLot.VarietyCode)
                    && !string.IsNullOrWhiteSpace(sourceLot.ProductionType)
                    && sourceLot.IsOrganic.HasValue
                    && x.VarietyCode == sourceLot.VarietyCode
                    && x.ProductionType == sourceLot.ProductionType
                    && x.IsOrganic == sourceLot.IsOrganic.Value)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var destinationCurrent = (await BuildRoomLotSummariesAsync(form.DestinationRoomId, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .Where(x => string.Equals(x.LotCode, sourceLot.LotCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.VarietyCode, sourceLot.VarietyCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.GrowerName, sourceLot.GrowerName, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CurrentBins);

        var transfer = new RoomTransfer
        {
            OperationKey = operationKey,
            SourceWarehouseId = fromRoom.WarehouseId,
            SourceRoomId = fromRoom.Id,
            DestinationWarehouseId = toRoom.WarehouseId,
            DestinationRoomId = toRoom.Id,
            CropYear = sourceLot.CropYear,
            GrowerLotId = sourceLot.GrowerLotId ?? sourceReceipt?.GrowerLotId,
            FruitProfileId = sourceLot.FruitProfileId ?? fruitProfile?.Id ?? sourceReceipt?.FruitProfileId,
            GrowerName = sourceLot.GrowerName,
            LotNumber = sourceLot.LotCode,
            PoolStart = sourceLot.PoolStart,
            VarietyCode = sourceLot.VarietyCode,
            InventoryStatus = sourceLot.InventoryStatus,
            BinCount = form.BinCount,
            Reason = reason,
            Notes = notes,
            TransferredAt = form.TransferAt,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.RoomTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (RoomTreatments is not null)
        {
            var lineage = await RoomTreatments.MoveSelectedAsync(
                selectedEntry.Snapshot,
                form.TreatmentSignature,
                form.TreatmentSegmentId,
                selectedEntry.Option.TreatmentReceiptId,
                form.BinCount,
                toRoom.WarehouseId,
                toRoom.Id,
                $"transfer:{operationKey}:treatment",
                TreatmentLineageMovementTypes.Transfer,
                transfer.Id,
                null,
                null,
                form.TransferAt,
                currentUser?.Id,
                cancellationToken);
            if (!lineage.Success) return lineage.Error;
        }

        RoomInventoryAdjustment outgoing;
        if (sourceReceipt is not null)
        {
            outgoing = AddRoomInventoryAdjustment(
                sourceReceipt,
                currentUser,
                "TransferOut",
                oldBinCount: sourceLot.CurrentBins,
                changeAmount: -form.BinCount,
                newBinCount: Math.Max(0, sourceLot.CurrentBins - form.BinCount),
                adjustmentAt: form.TransferAt,
                reason: reason,
                notes: $"Transfer to {toRoom.Warehouse.Code}/{toRoom.Code}. {notes}".Trim(),
                roomDepletionId: null,
                warehouseIdOverride: fromRoom.WarehouseId,
                roomIdOverride: fromRoom.Id);
        }
        else
        {
            outgoing = AddRoomInventoryAdjustmentRaw(
                receiptId: null,
                warehouseId: fromRoom.WarehouseId,
                roomId: fromRoom.Id,
                growerLotId: sourceLot.GrowerLotId,
                fruitProfileId: sourceLot.FruitProfileId ?? fruitProfile?.Id,
                growerName: sourceLot.GrowerName,
                lotNumber: sourceLot.LotCode,
                varietyCode: sourceLot.VarietyCode,
                oldBinCount: sourceLot.CurrentBins,
                changeAmount: -form.BinCount,
                newBinCount: Math.Max(0, sourceLot.CurrentBins - form.BinCount),
                adjustmentType: "TransferOut",
                adjustmentAt: form.TransferAt,
                currentUser: currentUser,
                reason: reason,
                notes: $"Transfer to {toRoom.Warehouse.Code}/{toRoom.Code}. {notes}".Trim());
        }

        var incoming = AddRoomInventoryAdjustmentRaw(
            receiptId: selectedEntry.Option.TreatmentReceiptId,
            warehouseId: toRoom.WarehouseId,
            roomId: toRoom.Id,
            growerLotId: sourceLot.GrowerLotId ?? sourceReceipt?.GrowerLotId,
            fruitProfileId: sourceLot.FruitProfileId ?? fruitProfile?.Id ?? sourceReceipt?.FruitProfileId,
            growerName: sourceLot.GrowerName,
            lotNumber: sourceLot.LotCode,
            varietyCode: sourceLot.VarietyCode,
            oldBinCount: destinationCurrent,
            changeAmount: form.BinCount,
            newBinCount: destinationCurrent + form.BinCount,
            adjustmentType: "TransferIn",
            adjustmentAt: form.TransferAt,
            currentUser: currentUser,
            reason: reason,
            notes: $"Transfer from {fromRoom.Warehouse.Code}/{fromRoom.Code}. {notes}".Trim());
        outgoing.CropYear = transfer.CropYear;
        outgoing.FruitProfileId = transfer.FruitProfileId;
        outgoing.RoomTransferId = transfer.Id;
        outgoing.RoomTransfer = transfer;
        outgoing.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        outgoing.InventoryOperationKey = $"transfer:{operationKey}:out";
        incoming.CropYear = transfer.CropYear;
        incoming.FruitProfileId = transfer.FruitProfileId;
        incoming.RoomTransferId = transfer.Id;
        incoming.RoomTransfer = transfer;
        incoming.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        incoming.InventoryOperationKey = $"transfer:{operationKey}:in";

        await AddAuditAsync("Transfer", nameof(RoomTransfer), transfer.Id.ToString(), currentUser?.Email ?? "unknown", null, $"Transferred {form.BinCount} bins of {sourceLot.GrowerName} {sourceLot.LotCode} {sourceLot.VarietyCode}. Reason: {reason}", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    public async Task<string?> ReverseRoomTransferAsync(
        ReverseRoomTransferForm form,
        CancellationToken cancellationToken)
    {
        var canAdminTransfer = await HasAccessAsync(
            ApplicationAreas.Transfers,
            PageAccessLevel.Admin,
            cancellationToken);
        var canAdminRoomTransactions = await HasAccessAsync(
            ApplicationAreas.RoomTransactions,
            PageAccessLevel.Admin,
            cancellationToken);
        if (!canAdminTransfer && !canAdminRoomTransactions)
        {
            return "Transfer Admin access is required to reverse a room transfer.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "Reason is required to reverse a room transfer.";
        }

        var operationKey = string.IsNullOrWhiteSpace(form.OperationKey)
            ? Guid.NewGuid().ToString("N")
            : form.OperationKey.Trim();
        await using var transaction = await BeginInventoryTransactionIfSupportedAsync(cancellationToken);
        var original = await dbContext.RoomTransfers
            .Include(x => x.InventoryAdjustments)
            .SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
        if (original is null)
        {
            return "Room transfer was not found.";
        }
        if (original.IsReversed
            || await dbContext.RoomTransfers.AsNoTracking()
                .AnyAsync(x => x.ReversesRoomTransferId == original.Id, cancellationToken))
        {
            return null;
        }
        if (await dbContext.RoomTransfers.AsNoTracking()
            .AnyAsync(x => x.OperationKey == operationKey, cancellationToken))
        {
            return null;
        }

        var sealError = await RoomMovementSealGuard.ValidateAsync(
            dbContext,
            [original.DestinationRoomId],
            [original.SourceRoomId],
            BusinessTime,
            cancellationToken);
        if (sealError is not null) return sealError;

        var snapshots = await RoomInventoryLedger.GetSnapshotsAsync(
            original.DestinationWarehouseId,
            [original.DestinationRoomId, original.SourceRoomId],
            cancellationToken);
        var destinationSnapshot = snapshots.SingleOrDefault(x =>
            x.RoomId == original.DestinationRoomId
            && x.CropYear == original.CropYear
            && x.FruitProfileId == original.FruitProfileId
            && string.Equals(x.Lot, original.LotNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.InventoryStatus, original.InventoryStatus ?? "", StringComparison.OrdinalIgnoreCase));
        if (destinationSnapshot is null || destinationSnapshot.CurrentBins < original.BinCount)
        {
            var available = destinationSnapshot?.CurrentBins ?? 0;
            return $"Transfer cannot be reversed because only {available} of the required {original.BinCount} bins remain in the destination room.";
        }
        var sourceCurrent = snapshots
            .Where(x => x.RoomId == original.SourceRoomId
                && x.CropYear == original.CropYear
                && x.FruitProfileId == original.FruitProfileId
                && string.Equals(x.Lot, original.LotNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.InventoryStatus, original.InventoryStatus ?? "", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CurrentBins);

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var reason = form.Reason.Trim();
        var reversal = new RoomTransfer
        {
            OperationKey = operationKey,
            SourceWarehouseId = original.DestinationWarehouseId,
            SourceRoomId = original.DestinationRoomId,
            DestinationWarehouseId = original.SourceWarehouseId,
            DestinationRoomId = original.SourceRoomId,
            CropYear = original.CropYear,
            GrowerLotId = original.GrowerLotId,
            FruitProfileId = original.FruitProfileId,
            GrowerName = original.GrowerName,
            LotNumber = original.LotNumber,
            PoolStart = original.PoolStart,
            VarietyCode = original.VarietyCode,
            InventoryStatus = original.InventoryStatus,
            BinCount = original.BinCount,
            Reason = $"Reversal of transfer #{original.Id}: {reason}",
            TransferredAt = now,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = now,
            ReversesRoomTransferId = original.Id,
            ReversesRoomTransfer = original
        };
        dbContext.RoomTransfers.Add(reversal);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (RoomTreatments is not null)
        {
            var lineage = await RoomTreatments.ReverseMovementsAsync(
                $"transfer:{operationKey}:treatment-reversal",
                TreatmentLineageMovementTypes.TransferReversal,
                original.Id,
                null,
                null,
                now,
                currentUser?.Id,
                cancellationToken);
            if (!lineage.Success) return lineage.Error;
        }

        var outgoing = AddRoomInventoryAdjustmentRaw(
            null,
            reversal.SourceWarehouseId,
            reversal.SourceRoomId,
            reversal.GrowerLotId,
            reversal.FruitProfileId,
            reversal.GrowerName,
            reversal.LotNumber,
            reversal.VarietyCode ?? "",
            destinationSnapshot.CurrentBins,
            -reversal.BinCount,
            destinationSnapshot.CurrentBins - reversal.BinCount,
            "TransferOut",
            now,
            currentUser,
            reversal.Reason,
            $"Reversal of room transfer #{original.Id}");
        var incoming = AddRoomInventoryAdjustmentRaw(
            null,
            reversal.DestinationWarehouseId,
            reversal.DestinationRoomId,
            reversal.GrowerLotId,
            reversal.FruitProfileId,
            reversal.GrowerName,
            reversal.LotNumber,
            reversal.VarietyCode ?? "",
            sourceCurrent,
            reversal.BinCount,
            sourceCurrent + reversal.BinCount,
            "TransferIn",
            now,
            currentUser,
            reversal.Reason,
            $"Reversal of room transfer #{original.Id}");
        foreach (var (adjustment, suffix) in new[] { (outgoing, "out"), (incoming, "in") })
        {
            adjustment.CropYear = reversal.CropYear;
            adjustment.InventoryStatus = reversal.InventoryStatus;
            adjustment.RoomTransferId = reversal.Id;
            adjustment.RoomTransfer = reversal;
            adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
            adjustment.InventoryOperationKey = $"transfer:{operationKey}:{suffix}";
        }

        original.IsReversed = true;
        original.ReversedAt = now;
        original.ReversedByUserId = currentUser?.Id;
        original.ReverseReason = reason;
        await AddAuditAsync(
            "Reverse",
            nameof(RoomTransfer),
            original.Id.ToString(),
            currentUser?.Email ?? "unknown",
            null,
            $"Reversed room transfer #{original.Id}. Reason: {reason}",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    public async Task<MasterDataPageViewModel> GetMasterDataPageAsync(string type, CancellationToken cancellationToken)
    {
        try
        {
            return type.ToLowerInvariant() switch
            {
                "warehouses" => new("Warehouses", null, ["Code", "Name", "Active"], (await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(x => Row(x.Code, x.Name, YesNo(x.IsActive))).ToList()),
                "rooms" => new("Rooms", null, ["Warehouse", "Code", "Name", "Capacity Bins", "Active"], (await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).OrderBy(x => x.Warehouse.Name).ThenBy(x => x.Name).ToListAsync(cancellationToken)).Select(x => Row(x.Warehouse.Code, x.Code, x.Name, x.CapacityBins.ToString(), YesNo(x.IsActive))).ToList()),
                "fruit-profiles" => new("Fruit profiles / variety codes", null, ["Variety Code", "Name", "Fruit Type", "Production Type", "Organic", "Active"], (await dbContext.FruitProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(x => Row(x.VarietyCode, x.Name, x.FruitType, x.ProductionType, YesNo(x.IsOrganic), YesNo(x.IsActive))).ToList()),
                "grades" => new("Grades", null, ["Code", "Name", "Active"], (await dbContext.Grades.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken)).Select(x => Row(x.Code, x.Name, YesNo(x.IsActive))).ToList()),
                "defects" => new("Defects", null, ["Name", "Active"], (await dbContext.DefectTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(x => Row(x.Name, YesNo(x.IsActive))).ToList()),
                "sample-types" => new("Sample types", null, ["Name", "Active"], (await dbContext.SampleTypes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken)).Select(x => Row(x.Name, YesNo(x.IsActive))).ToList()),
                "starch-scale-values" => new("Starch scale values", null, ["Scale", "Value", "Sort", "Active"], (await dbContext.StarchScaleValues.AsNoTracking().Include(x => x.StarchScale).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken)).Select(x => Row(x.StarchScale.Name, x.Value.ToString("0.0"), x.SortOrder.ToString(), YesNo(x.IsActive))).ToList()),
                "size-thresholds" => new("Size thresholds", null, ["Fruit Type", "Size", "Minimum Weight (g)", "Active"], (await dbContext.FruitSizeConversionThresholds.AsNoTracking().OrderBy(x => x.FruitType).ThenByDescending(x => x.MinimumWeightGrams).ToListAsync(cancellationToken)).Select(x => Row(x.FruitType, x.SizeCategory.ToString(), x.MinimumWeightGrams.ToString("0.0000"), YesNo(x.IsActive))).ToList()),
                _ => new("Master data", null, ["Page"], MasterDataLinks().Select(x => Row(x.Label)).ToList())
            };
        }
        catch (Exception ex)
        {
            return new MasterDataPageViewModel("Master data", DatabaseWarning(ex, "Master data"), ["Page"], MasterDataLinks().Select(x => Row(x.Label)).ToList());
        }
    }

    public async Task<ReceiptListViewModel> SearchReceiptsAsync(ReceiptSearchForm search, CancellationToken cancellationToken)
    {
        try
        {
            var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
            search.Facility = FacilityContext.Normalize(search.Facility);
            var facilityWarehouseIds = await FacilityContext.GetWarehouseIdsAsync(search.Facility, cancellationToken);
            search.CropYear ??= cropYearService.GetCurrentCropYear(BusinessTime.NowPacific);
            if (search.WarehouseId is int requestedWarehouseId && !facilityWarehouseIds.Contains(requestedWarehouseId))
            {
                search.WarehouseId = null;
                search.RoomId = null;
            }
            if (search.RoomId is int requestedRoomId)
            {
                var requestedRoom = await dbContext.Rooms.AsNoTracking()
                    .Where(x => x.Id == requestedRoomId)
                    .Select(x => new { x.WarehouseId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (requestedRoom is not null
                    && (!facilityWarehouseIds.Contains(requestedRoom.WarehouseId)
                        || (search.WarehouseId is int selectedWarehouseId
                            && requestedRoom.WarehouseId != selectedWarehouseId)))
                {
                    search.RoomId = null;
                }
            }

            var query = dbContext.Receipts.AsNoTracking()
                .Include(x => x.Warehouse)
                .Include(x => x.Room)
                .Include(x => x.FruitProfile)
                .Where(x => !x.IsDeleted)
                .AsQueryable();
            if (search.Facility != "All") query = query.Where(x => facilityWarehouseIds.Contains(x.WarehouseId));
            if (!search.AllCropYears && search.CropYear is not null) query = query.Where(x => x.CropYear == search.CropYear);
            if (string.Equals(search.DateFilter, "today", StringComparison.OrdinalIgnoreCase))
            {
                var todayRange = BusinessTime.UtcRangeForPacificDate(BusinessTime.PacificDate(BusinessTime.UtcNow));
                query = query.Where(x => x.ReceivedAt >= todayRange.Start && x.ReceivedAt < todayRange.End);
            }

            if (!string.IsNullOrWhiteSpace(search.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(search.ReceiptId));
            if (!string.IsNullOrWhiteSpace(search.Grower))
            {
                var growerSearch = search.Grower.Trim();
                var matchingNumbers = growerResolver.MatchingGrowerNumbers(growerSearch);
                query = query.Where(x => x.GrowerName.Contains(growerSearch)
                    || matchingNumbers.Contains(x.GrowerNumber ?? x.LotCode));
            }
            if (!string.IsNullOrWhiteSpace(search.Lot)) query = query.Where(x => x.LotCode.Contains(search.Lot));
            if (search.WarehouseId is not null) query = query.Where(x => x.WarehouseId == search.WarehouseId);
            if (search.RoomId is not null) query = query.Where(x => x.RoomId == search.RoomId);
            if (search.FruitProfileId is not null) query = query.Where(x => x.FruitProfileId == search.FruitProfileId);
            if (!string.IsNullOrWhiteSpace(search.SampleType))
            {
                var sampleType = search.SampleType.Trim();
                query = query.Where(x => dbContext.QcSamples.Any(sample =>
                    !sample.IsDeleted
                    && sample.ReceiptId == x.Id
                    && sample.SampleType.Name.Contains(sampleType)));
            }
            var receiptTypeCountRows = await query.ToListAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(search.ReceiptType))
            {
                var receiptType = NormalizeReceiptType(search.ReceiptType);
                query = query.Where(x => x.ReceiptType == receiptType);
            }

            var receipts = await query.OrderByDescending(x => x.ReceivedAt).Take(500).ToListAsync(cancellationToken);
            var receiptIds = receipts.Select(x => x.Id).ToList();
            var sampleSummaries = await dbContext.QcSamples.AsNoTracking()
                .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value) && !x.IsDeleted)
                .GroupBy(x => x.ReceiptId)
                .Select(x => new ReceiptSampleSummary(
                    x.Key!.Value,
                    x.Count(),
                    x.Max(s => s.UpdatedAt ?? s.CreatedAt),
                    x.Any(s => s.Status == "Ready to Send"),
                    x.Any(s => s.Status.Contains("Needs Review")),
                    x.Any(s => s.EmailStatus == "Sent")))
                .ToDictionaryAsync(x => x.ReceiptId, cancellationToken);
            var receiptVarietyKeys = receipts
                .Select(x => VarietyColorService.IdentityFromProfile(x.FruitProfile).Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var receiptColors = varietyColorService is null
                ? receiptVarietyKeys.ToDictionary(
                    x => x,
                    x => new VarietyColorResolved(x, VarietyColorService.NormalizeIdentity(x, x).Name, VarietyColorService.FallbackColor(x), false),
                    StringComparer.OrdinalIgnoreCase)
                : await varietyColorService.GetResolvedColorsReadOnlyAsync(receiptVarietyKeys, cancellationToken);
            var receiptRooms = await dbContext.Rooms.AsNoTracking()
                .Where(x => facilityWarehouseIds.Contains(x.WarehouseId))
                .OrderBy(x => x.WarehouseId).ThenBy(x => x.SubLocation).ThenBy(x => x.SortOrder).ThenBy(x => x.CropQcRoomName ?? x.Code)
                .ToListAsync(cancellationToken);
            NormalizeEffectiveRoomSealStates(receiptRooms);
            return new ReceiptListViewModel
            {
                Search = search,
                Receipts = receipts.Select(receipt => ReceiptListItem(receipt, sampleSummaries.GetValueOrDefault(receipt.Id), receiptColors, growerResolver)).ToList(),
                ReceiptTypeCounts = BuildReceiptTypeCounts(search, receiptTypeCountRows),
                Warehouses = await dbContext.Warehouses.AsNoTracking().Where(x => facilityWarehouseIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Rooms = receiptRooms,
                FruitProfiles = await dbContext.FruitProfiles.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.VarietyCode)
                    .ThenBy(x => x.Name)
                    .ThenBy(x => x.ProductionType)
                    .ThenBy(x => x.IsOrganic)
                    .ToListAsync(cancellationToken),
                GrowerLots = await GetReceivingGrowerLotsAsync(cancellationToken),
                AvailableCropYears = await cropYearService.GetAvailableCropYearsAsync(cancellationToken),
                CurrentCropYear = cropYearService.GetCurrentCropYear(BusinessTime.NowPacific),
                CropYearHelpText = "Crop years use the starting-year convention by default: CropYear 2026 starts 2026-08-01 and ends 2027-07-31. Confirm crop year when season dates overlap.",
                DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken)
            };
        }
        catch (Exception ex)
        {
            return new ReceiptListViewModel { Search = search, DataWarning = DatabaseWarning(ex, "Receipt list") };
        }
    }

    public async Task<CreateReceiptResult> CreateReceiptAsync(CreateReceiptForm form, CancellationToken cancellationToken)
    {
        var receiptType = NormalizeReceiptType(form.ReceiptType);
        if (string.IsNullOrWhiteSpace(form.CompuTechReceiptId) || (form.GrowerLotId is null && (string.IsNullOrWhiteSpace(form.GrowerName) || string.IsNullOrWhiteSpace(form.GrowerNumber))) || (IsInventoryReceiptType(receiptType) && form.BinCount <= 0))
        {
            return new(null, null, "Receipt ID, grower, Lot #, receipt type, and bin count for truck receipts are required.");
        }
        if (reviewedGrowerLotPolicy is not null && form.GrowerLotId is null)
        {
            return new(null, null, "Select a current Grower Number from the reviewed Grower list.");
        }

        await using var inventoryTransaction = IsInventoryReceiptType(receiptType)
            ? await BeginInventoryTransactionIfSupportedAsync(cancellationToken)
            : null;
        if (IsInventoryReceiptType(receiptType))
        {
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [], [form.RoomId], BusinessTime, cancellationToken);
            if (sealError is not null) return new(null, null, sealError);
        }

        var room = await dbContext.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.RoomId, cancellationToken);
        if (room is null)
        {
            return new(null, null, "Selected room was not found.");
        }

        if (room.WarehouseId != form.WarehouseId)
        {
            return new(null, null, "Selected room does not belong to the selected warehouse.");
        }

        var fruitProfile = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.FruitProfileId, cancellationToken);
        if (fruitProfile is null)
        {
            return new(null, null, "Selected variety was not found.");
        }

        if (cropYearService.RequiresConfirmation(form.ReceivedAt, form.CropYear) && !form.ConfirmCropYear)
        {
            var candidates = string.Join(", ", cropYearService.GetCandidateCropYears(form.ReceivedAt));
            return new(null, null, $"Confirm Crop Year before saving. Suggested crop year option(s) for this received date: {candidates}.");
        }

        GrowerLot? growerLot = null;
        if (form.GrowerLotId is not null)
        {
            growerLot = await GetReceivingGrowerLotAsync(form.GrowerLotId.Value, cancellationToken);
            if (growerLot is null)
            {
                return new(null, null, "Selected grower lot was not found or is inactive.");
            }
        }

        var lotNumber = growerLot?.LotNumber ?? form.GrowerNumber.Trim();
        var suppliedGrowerName = growerLot?.Grower ?? form.GrowerName.Trim();
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
        var growerName = growerResolver.DisplayName(suppliedGrowerName, lotNumber);
        var now = DateTimeOffset.UtcNow;
        var receipt = new Receipt
        {
            CropYear = form.CropYear,
            ReceivedAt = form.ReceivedAt,
            CompuTechReceiptId = form.CompuTechReceiptId.Trim(),
            ReceiptType = receiptType,
            WarehouseId = form.WarehouseId,
            RoomId = form.RoomId,
            FruitProfileId = form.FruitProfileId,
            GrowerLotId = growerLot?.Id,
            GrowerNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
            PoolStart = null,
            GrowerName = growerName,
            LotCode = lotNumber,
            BinCount = Math.Max(0, form.BinCount),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Receipts.Add(receipt);

        await dbContext.SaveChangesAsync(cancellationToken);
        if (!IsInventoryReceiptType(receiptType))
        {
            return new(receipt.Id, receipt.CompuTechReceiptId, null);
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        AddRoomInventoryAdjustment(
            receipt,
            currentUser,
            "ReceiptAdd",
            oldBinCount: null,
            changeAmount: receipt.BinCount,
            newBinCount: receipt.BinCount,
            adjustmentAt: receipt.ReceivedAt,
            reason: "Receiving inventory added",
            notes: $"Receipt {receipt.CompuTechReceiptId} added {receipt.BinCount} bins to {room.Code}.",
            roomDepletionId: null);
        await AddAuditAsync("BinCountChange", nameof(RoomInventoryAdjustment), receipt.CompuTechReceiptId, currentUser?.Email ?? "unknown", null, $"ReceiptAdd {receipt.BinCount} bins in room {room.Code}.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
        if (inventoryTransaction is not null) await inventoryTransaction.CommitAsync(cancellationToken);
        return new(receipt.Id, receipt.CompuTechReceiptId, null);
    }

    private async Task<string?> ValidateReceiptFormAsync(CreateReceiptForm form, string receiptType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.CompuTechReceiptId)
            || (form.GrowerLotId is null && (string.IsNullOrWhiteSpace(form.GrowerName) || string.IsNullOrWhiteSpace(form.GrowerNumber)))
            || (IsInventoryReceiptType(receiptType) && form.BinCount <= 0))
        {
            return "Receipt ID, grower, Lot #, receipt type, and bin count for truck receipts are required.";
        }

        var room = await dbContext.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.RoomId, cancellationToken);
        if (room is null)
        {
            return "Selected room was not found.";
        }

        if (room.WarehouseId != form.WarehouseId)
        {
            return "Selected room does not belong to the selected warehouse.";
        }

        if (!await dbContext.FruitProfiles.AsNoTracking().AnyAsync(x => x.Id == form.FruitProfileId, cancellationToken))
        {
            return "Selected variety was not found.";
        }

        if (cropYearService.RequiresConfirmation(form.ReceivedAt, form.CropYear) && !form.ConfirmCropYear)
        {
            var candidates = string.Join(", ", cropYearService.GetCandidateCropYears(form.ReceivedAt));
            return $"Confirm Crop Year before saving. Suggested crop year option(s) for this received date: {candidates}.";
        }

        if (form.GrowerLotId is not null
            && await GetReceivingGrowerLotAsync(form.GrowerLotId.Value, cancellationToken) is null)
        {
            return "Selected grower lot was not found or is inactive.";
        }

        return null;
    }

    private async Task<EditReceiptPageViewModel> BuildReceiptEditPageAsync(CancellationToken cancellationToken)
    {
        var rooms = await dbContext.Rooms.AsNoTracking()
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.SubLocation).ThenBy(x => x.SortOrder).ThenBy(x => x.CropQcRoomName ?? x.Code)
            .ToListAsync(cancellationToken);
        NormalizeEffectiveRoomSealStates(rooms);
        return new()
        {
            Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
            Rooms = rooms,
            FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
            GrowerLots = await GetReceivingGrowerLotsAsync(cancellationToken)
        };
    }

    private void NormalizeEffectiveRoomSealStates(IEnumerable<Room> rooms)
    {
        var now = BusinessTime.UtcNow;
        foreach (var room in rooms)
        {
            room.IsSealed = RoomSealState.IsEffectivelySealed(room, now);
        }
    }

    public async Task<ReceiptDetailViewModel> GetReceiptDetailAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await dbContext.Receipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (receipt is null)
            {
                return new ReceiptDetailViewModel { DataWarning = "Receipt not found." };
            }
            var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);

            var samples = await QuerySamples().Where(x => x.ReceiptId == id).OrderBy(x => x.SampleTakenAt).ThenBy(x => x.SampleSequenceNumber).ToListAsync(cancellationToken);
            var sampleIds = samples.Select(x => x.Id).ToList();
            var photos = await dbContext.QcPhotos.AsNoTracking()
                .Where(x => !x.IsDeleted
                    && (x.ReceiptId == id || (x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value))))
                .OrderByDescending(x => x.CapturedAt)
                .ToListAsync(cancellationToken);
            var inventoryOverrides = await dbContext.ReceiptInventoryOverrides.AsNoTracking()
                .Where(x => x.ReceiptId == id)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ReceiptInventoryOverrideHistoryViewModel(
                    x.Id,
                    x.ActionType,
                    x.AdministratorUser.DisplayName,
                    x.CreatedAt,
                    x.Reason,
                    x.OldReceiptBinCount,
                    x.NewReceiptBinCount,
                    x.InventoryDelta,
                    x.CurrentInventoryBefore,
                    x.CurrentInventoryAfter,
                    x.NegativeInventoryAcknowledged,
                    x.InventoryAdjustments.Count))
                .ToListAsync(cancellationToken);
            IReadOnlyList<RoomInventoryLossHistoryViewModel> inventoryLosses = RoomInventoryLosses is null
                ? []
                : await RoomInventoryLosses.GetReceiptHistoryAsync(id, cancellationToken);
            var treatmentApplications = await GetReceivingTreatmentHistoryAsync(id, cancellationToken);
            var receiptLot = !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber : receipt.LotCode;
            var receiptSnapshots = await RoomInventoryLedger.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], receipt.FruitProfileId, cancellationToken);
            var currentPackableBins = receiptSnapshots
                .Where(x => x.CropYear == receipt.CropYear
                    && x.GrowerLotId == receipt.GrowerLotId
                    && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase))
                .Select(x => (int?)x.CurrentBins)
                .SingleOrDefault();
            return new ReceiptDetailViewModel
            {
                Receipt = ReceiptListItem(receipt, growerResolver: growerResolver),
                Samples = await EnrichSamplesAsync(samples, cancellationToken),
                SampleTypes = await GetReceiptSampleTypesAsync(cancellationToken),
                PhotoGroups = GroupPhotos(
                    photos,
                    await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, cancellationToken),
                    deleteFromReceiptId: receipt.Id,
                    canDeleteSamplePhotos: await CanEditSamplesAsync(cancellationToken)),
                CanDeleteSamples = await HasAccessAsync(ApplicationAreas.DailyQc, PageAccessLevel.Admin, cancellationToken),
                DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken),
                InventoryOverrides = inventoryOverrides,
                InventoryLosses = inventoryLosses,
                CurrentPackableBins = currentPackableBins,
                TreatmentApplications = treatmentApplications,
                CanApplyReceivingTreatment = await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, cancellationToken),
                CanReverseReceivingTreatment = await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Admin, cancellationToken),
                AddPhotoForm = new AddPhotoMetadataForm
                {
                    ReceiptId = receipt.Id,
                    PhotoType = "BinTruck",
                    PhotoSource = "Upload File",
                    ContentType = "image/jpeg"
                }
            };
        }
        catch (Exception ex)
        {
            return new ReceiptDetailViewModel { DataWarning = DatabaseWarning(ex, "Receipt detail") };
        }
    }

    public async Task<EditReceiptPageViewModel> GetReceiptEditAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (receipt is null)
            {
                return new EditReceiptPageViewModel { DataWarning = "Receipt not found." };
            }

            var model = await BuildReceiptEditPageAsync(cancellationToken);
            var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
            model.Form = new UpdateReceiptForm
            {
                Id = receipt.Id,
                CropYear = receipt.CropYear,
                ReceivedAt = receipt.ReceivedAt,
                CompuTechReceiptId = receipt.CompuTechReceiptId,
                ReceiptType = NormalizeReceiptType(receipt.ReceiptType),
                WarehouseId = receipt.WarehouseId,
                RoomId = receipt.RoomId,
                FruitProfileId = receipt.FruitProfileId,
                GrowerLotId = receipt.GrowerLotId,
                GrowerNumber = receipt.GrowerNumber ?? "",
                GrowerName = growerResolver.DisplayName(receipt.GrowerName, receipt.GrowerNumber ?? receipt.LotCode),
                LotCode = receipt.LotCode,
                BinCount = receipt.BinCount,
                ConfirmCropYear = true
            };
            return model;
        }
        catch (Exception ex)
        {
            return new EditReceiptPageViewModel { DataWarning = DatabaseWarning(ex, "Receipt edit") };
        }
    }

    public async Task<string?> UpdateReceiptAsync(UpdateReceiptForm form, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.Include(x => x.Warehouse).Include(x => x.Room).SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return "Receipt not found.";
        }

        var receiptType = NormalizeReceiptType(form.ReceiptType);
        var validationError = await ValidateReceiptFormAsync(form, receiptType, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        GrowerLot? growerLot = null;
        if (form.GrowerLotId is not null)
        {
            growerLot = await GetReceivingGrowerLotAsync(form.GrowerLotId.Value, cancellationToken);
        }

        var oldReceiptBinCount = receipt.BinCount;
        var before = JsonSerializer.Serialize(new
        {
            receipt.CropYear,
            receipt.ReceivedAt,
            receipt.CompuTechReceiptId,
            receipt.ReceiptType,
            receipt.WarehouseId,
            receipt.RoomId,
            receipt.FruitProfileId,
            receipt.GrowerLotId,
            receipt.GrowerNumber,
            receipt.GrowerName,
            receipt.LotCode,
            receipt.BinCount
        });
        var wasInventory = IsInventoryReceiptType(receipt.ReceiptType);
        var lotNumber = growerLot?.LotNumber ?? form.GrowerNumber.Trim();
        var suppliedGrowerName = growerLot?.Grower ?? form.GrowerName.Trim();
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
        var growerName = growerResolver.DisplayName(suppliedGrowerName, lotNumber);
        var hasInventoryHistory = wasInventory && await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .AnyAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
        var quantityChanged = form.BinCount != receipt.BinCount;
        var inventoryIdentityChanged = receipt.CropYear != form.CropYear
            || receipt.WarehouseId != form.WarehouseId
            || receipt.RoomId != form.RoomId
            || receipt.FruitProfileId != form.FruitProfileId
            || receipt.GrowerLotId != growerLot?.Id
            || !string.Equals(receipt.GrowerNumber ?? receipt.LotCode, lotNumber, StringComparison.OrdinalIgnoreCase)
            || wasInventory != IsInventoryReceiptType(receiptType);
        if (quantityChanged || (hasInventoryHistory && inventoryIdentityChanged))
        {
            var rejectingUser = await GetCurrentUserAsync(cancellationToken);
            await AuditRejectedInventoryDeductionAsync(
                "ReceiptEdit",
                receipt.Id.ToString(),
                rejectingUser,
                $"Rejected requested inventory-affecting receipt edit from {receipt.BinCount} to {form.BinCount} bins.",
                cancellationToken);
            if (quantityChanged)
            {
                return await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Admin, cancellationToken)
                    ? "Changing the bin count of a saved Receipt requires an override. The Receipt was not changed."
                    : "You do not have permission to override the bin count on a saved Receipt. The Receipt was not changed.";
            }

            return "Inventory-affecting receipt corrections require Receipts Admin and an explicit Admin Inventory Override. The receipt was not changed.";
        }

        var addInventoryAdjustment = IsInventoryReceiptType(receiptType)
            && (!wasInventory || !hasInventoryHistory || quantityChanged);
        await using var inventoryTransaction = addInventoryAdjustment
            ? await BeginInventoryTransactionIfSupportedAsync(cancellationToken)
            : null;
        if (addInventoryAdjustment)
        {
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [], [form.RoomId], BusinessTime, cancellationToken);
            if (sealError is not null) return sealError;
        }
        var currentBins = addInventoryAdjustment && wasInventory
            ? await GetCurrentBinsForReceiptAsync(receipt.Id, cancellationToken)
            : 0;

        receipt.CropYear = form.CropYear;
        receipt.ReceivedAt = form.ReceivedAt;
        receipt.CompuTechReceiptId = form.CompuTechReceiptId.Trim();
        receipt.ReceiptType = receiptType;
        receipt.WarehouseId = form.WarehouseId;
        receipt.RoomId = form.RoomId;
        receipt.FruitProfileId = form.FruitProfileId;
        receipt.GrowerLotId = growerLot?.Id;
        receipt.GrowerNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber;
        receipt.PoolStart = null;
        receipt.GrowerName = growerName;
        receipt.LotCode = lotNumber;
        receipt.BinCount = Math.Max(0, form.BinCount);
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        receipt.ConcurrencyVersion++;

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        if (addInventoryAdjustment)
        {
            var changeAmount = wasInventory ? receipt.BinCount - oldReceiptBinCount : receipt.BinCount;
            AddRoomInventoryAdjustment(
                receipt,
                currentUser,
                "ReceiptEdit",
                oldBinCount: wasInventory ? currentBins : null,
                changeAmount: changeAmount,
                newBinCount: currentBins + changeAmount,
                adjustmentAt: DateTimeOffset.UtcNow,
                reason: "Receipt source adjustment",
                notes: $"Receipt {receipt.CompuTechReceiptId} quantity changed from {oldReceiptBinCount} to {receipt.BinCount} bins.",
                roomDepletionId: null);
        }

        await AddAuditAsync("Update", nameof(Receipt), receipt.Id.ToString(), currentUser?.Email ?? "unknown", before, JsonSerializer.Serialize(new
        {
            receipt.CropYear,
            receipt.ReceivedAt,
            receipt.CompuTechReceiptId,
            receipt.ReceiptType,
            receipt.WarehouseId,
            receipt.RoomId,
            receipt.FruitProfileId,
            receipt.GrowerLotId,
            receipt.GrowerNumber,
            receipt.GrowerName,
            receipt.LotCode,
            receipt.BinCount
        }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (addInventoryAdjustment)
        {
            await InventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
            if (inventoryTransaction is not null) await inventoryTransaction.CommitAsync(cancellationToken);
        }
        return null;
    }

    public async Task<string?> SoftDeleteReceiptAsync(DeleteReceiptForm form, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return "Receipt not found.";
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        var before = JsonSerializer.Serialize(new { receipt.Id, receipt.CompuTechReceiptId, receipt.ReceiptType, receipt.BinCount, receipt.IsDeleted });
        receipt.IsDeleted = true;
        receipt.DeletedAt = DateTimeOffset.UtcNow;
        receipt.DeletedByUserId = currentUser?.Id;
        receipt.DeleteReason = string.IsNullOrWhiteSpace(form.Reason) ? "Admin deleted receipt." : form.Reason.Trim();
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("Delete", nameof(Receipt), receipt.Id.ToString(), currentUser?.Email ?? "unknown", before, JsonSerializer.Serialize(new
        {
            receipt.Id,
            receipt.CompuTechReceiptId,
            receipt.IsDeleted,
            receipt.DeletedAt,
            receipt.DeleteReason
        }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(long? SampleId, int? SampleSequenceNumber, string? Warning, string? Error)> CreateSampleAsync(long receiptId, int sampleTypeId, CancellationToken cancellationToken)
    {
        var result = await ReceiptQcSampleCoordinator.OpenOrCreateAsync(
            dbContext,
            receiptId,
            allowCreate: true,
            requestedSampleTypeId: sampleTypeId,
            takenByUserId: await GetCurrentUserIdAsync(cancellationToken),
            qcStationId: null,
            actualSampleSize: 10,
            sampleTakenAt: null,
            notes: null,
            cancellationToken);
        return result.Sample is null
            ? (null, null, result.HistoricalConflict ? result.Error : null, result.Error)
            : (result.Sample.Id, result.Sample.SampleSequenceNumber, null, null);
    }

    private Task<List<RoomTreatmentApplicationHistoryViewModel>> GetReceivingTreatmentHistoryAsync(
        long receiptId,
        CancellationToken cancellationToken) =>
        dbContext.RoomTreatmentApplications.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.ApplicationLevel == TreatmentApplicationLevels.Receiving)
            .OrderByDescending(x => x.AppliedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new RoomTreatmentApplicationHistoryViewModel(
                x.Id,
                x.AppliedAt,
                x.ProductNameSnapshot,
                x.CommonNameSnapshot,
                x.TotalBinsSnapshot,
                x.AppliedByUser.DisplayName ?? x.AppliedByUser.Email,
                x.EstimatedCostSnapshot,
                x.CurrencySnapshot,
                x.Notes,
                x.ReversedAt != null,
                x.ReversedAt,
                x.ReversalReason,
                x.Attachments.Where(a => !a.IsDeleted).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
                    .Select(a => new TreatmentReportAttachmentViewModel(
                        a.Id,
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        a.CreatedAt))
                    .ToList()))
            .ToListAsync(cancellationToken);

    public async Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (sample is null)
            {
                return new SampleDetailViewModel { DataWarning = "QC sample not found." };
            }

            var allowedSampleSizes = await GetAllowedSampleSizesAsync(cancellationToken);
            var targetSampleSize = ResolveTargetSampleSize(sample.ActualSampleSize, allowedSampleSizes);
            var rowModels = await GetFruitReadingRowsAsync(id, targetSampleSize, cancellationToken);

            var availablePhotoTypes = photoRequirementPolicy.GetAvailablePhotoTypes(sample.SampleType.Name, sample.Receipt.FruitProfile.FruitType);
            var samplePhotoTypes = availablePhotoTypes.Where(x => !x.ReceiptLevel).Select(x => x.PhotoType).Append("Other").Distinct().ToList();
            var receiptPhotoTypes = availablePhotoTypes.Where(x => x.ReceiptLevel).Select(x => x.PhotoType).Distinct().ToList();
            var photos = await dbContext.QcPhotos.AsNoTracking()
                .Where(x => (x.QcSampleId == id && samplePhotoTypes.Contains(x.PhotoType))
                    || (x.ReceiptId == sample.ReceiptId && receiptPhotoTypes.Contains(x.PhotoType)))
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.CapturedAt)
                .ToListAsync(cancellationToken);
            var grades = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(cancellationToken);
            var defectTypes = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
            var recipientResolution = await qcEmailRecipientResolver.ResolveForSampleAsync(sample.Id, null, cancellationToken);
            var receiptLot = !string.IsNullOrWhiteSpace(sample.Receipt.GrowerNumber)
                ? sample.Receipt.GrowerNumber
                : sample.Receipt.LotCode;
            var receiptSnapshots = await RoomInventoryLedger.GetSnapshotsAsync(
                sample.Receipt.WarehouseId,
                [sample.Receipt.RoomId],
                sample.Receipt.FruitProfileId,
                cancellationToken);
            var currentPackableBins = receiptSnapshots
                .Where(x => x.CropYear == sample.Receipt.CropYear
                    && x.GrowerLotId == sample.Receipt.GrowerLotId
                    && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase))
                .Select(x => (int?)x.CurrentBins)
                .SingleOrDefault();
            return new SampleDetailViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                SampleTypes = await GetReceiptSampleTypesAsync(cancellationToken),
                FruitRows = rowModels,
                PhotoGroups = GroupPhotos(photos, await CanEditSamplesAsync(cancellationToken), sample.Id),
                Readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken),
                RecipientEmail = recipientResolution.IsConfigured ? recipientResolution.Header : null,
                AllowedSampleSizes = allowedSampleSizes,
                TargetSampleSize = targetSampleSize,
                EnteredFruitCount = rowModels.Count(HasEnteredData),
                AutosaveVersion = sample.FieldSampleAutosaveVersion,
                AvailablePhotoTypes = availablePhotoTypes
                    .Select(x => new QcPhotoRequirementViewModel(x.PhotoType, x.FriendlyName, x.IsRequired, x.ReceiptLevel))
                    .ToList(),
                FruitType = sample.Receipt.FruitProfile.FruitType,
                DefectInspectionStatus = sample.DefectInspectionStatus,
                Grades = grades,
                StarchScaleValues = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken),
                DefectTypes = defectTypes,
                SizeThresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
                    .Where(x => x.IsActive && x.FruitType == sample.Receipt.FruitProfile.FruitType)
                    .OrderByDescending(x => x.MinimumWeightGrams)
                    .Select(x => new FieldSampleSizeThreshold(x.SizeCategory, x.MinimumWeightGrams))
                    .ToListAsync(cancellationToken),
                DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken),
                CurrentPackableBins = currentPackableBins,
                TreatmentApplications = await GetReceivingTreatmentHistoryAsync(sample.ReceiptId!.Value, cancellationToken),
                CanApplyReceivingTreatment = await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, cancellationToken),
                CanReverseReceivingTreatment = await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Admin, cancellationToken),
                FruitReadingForm = new SaveFruitReadingsForm
                {
                    SampleId = sample.Id,
                    TargetSampleSize = targetSampleSize,
                    Rows = rowModels.Select(row => new FruitReadingEditRow
                    {
                        RowNumber = row.RowNumber,
                        Pressure1Lbs = row.Pressure1Lbs,
                        Pressure2Lbs = row.Pressure2Lbs,
                        WeightGrams = row.WeightGrams,
                        StarchScaleValueId = row.StarchScaleValueId,
                        GradeId = row.GradeId,
                        DefectTypeIds = row.DefectTypeIds.ToList(),
                        OtherDefectNotes = row.OtherDefectNotes,
                        DefectsInspected = row.DefectsInspected
                    }).ToList()
                },
                AddPhotoForm = new AddPhotoMetadataForm
                {
                    QcSampleId = sample.Id,
                    PhotoType = "SampleBeforeCutting",
                    PhotoSource = "Upload File",
                    ContentType = "image/jpeg"
                }
            };
        }
        catch (Exception ex)
        {
            return new SampleDetailViewModel { DataWarning = DatabaseWarning(ex, "QC sample detail") };
        }
    }

    public async Task<SampleRefreshViewModel?> GetSampleRefreshAsync(long id, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.AsNoTracking().Include(x => x.QcStation).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return null;
        }

        var allowedSampleSizes = await GetAllowedSampleSizesAsync(cancellationToken);
        var targetSampleSize = ResolveTargetSampleSize(sample.ActualSampleSize, allowedSampleSizes);
        var rows = await GetFruitReadingRowsAsync(id, targetSampleSize, cancellationToken);
        return new SampleRefreshViewModel(
            id,
            targetSampleSize,
            rows.Count(HasEnteredData),
            sample.UpdatedAt,
            rows.Select(row => new SampleRefreshRowViewModel(
                row.RowNumber,
                row.Pressure1Lbs,
                row.Pressure2Lbs,
                row.PressureAverageLbs,
                row.WeightGrams,
                row.GradeId,
                row.Grade,
                row.StarchScaleValueId,
                row.SizeCategory,
                row.SizeStatus,
                row.EntryStatus,
                row.DefectTypeIds,
                row.Defects,
                row.DefectsInspected,
                row.OtherDefectNotes,
                row.FieldVersion)).ToList(),
            BuildQcStationStatus(sample.QcStation));
    }

    public async Task<DeleteSampleConfirmationViewModel> GetDeleteSampleConfirmationAsync(long id, CancellationToken cancellationToken)
    {
        var sample = await QuerySamples(includeDeleted: true).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (sample is null)
        {
            return new DeleteSampleConfirmationViewModel { DataWarning = "QC sample not found." };
        }

        return new DeleteSampleConfirmationViewModel
        {
            SampleId = sample.Id,
            ReceiptId = sample.ReceiptId!.Value,
            CropYear = sample.Receipt.CropYear,
            DisplayReceiptId = sample.GetDisplayReceiptId(),
            Warehouse = sample.Receipt.Warehouse.Code,
            GrowerName = sample.Receipt.GrowerName,
            LotCode = sample.Receipt.LotCode,
            VarietyCode = sample.Receipt.FruitProfile.VarietyCode,
            SampleType = sample.SampleType.Name,
            PhotoCount = sample.Photos.Count(x => !x.IsDeleted),
            EmailStatus = sample.EmailStatus
        };
    }

    public async Task<(long? ReceiptId, string? Error)> SoftDeleteSampleAsync(long id, string? reason, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.DailyQc, PageAccessLevel.Admin, cancellationToken))
        {
            return (null, "Daily QC Admin access is required to delete QC samples.");
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (sample is null)
        {
            return (null, "QC sample not found.");
        }

        if (sample.IsDeleted)
        {
            return (sample.ReceiptId, "QC sample is already deleted.");
        }

        var changedBy = GetCurrentUserEmail() ?? "unknown";
        var before = System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.ReceiptId, sample.Status, sample.EmailStatus, PhotoCount = sample.Photos.Count(x => !x.IsDeleted) });
        sample.IsDeleted = true;
        sample.DeletedAt = DateTimeOffset.UtcNow;
        sample.DeletedByUserId = await GetCurrentUserIdAsync(cancellationToken);
        sample.DeleteReason = string.IsNullOrWhiteSpace(reason) ? "Admin sample delete" : reason.Trim();
        sample.UpdatedAt = DateTimeOffset.UtcNow;

        await AddAuditAsync(
            "soft-delete",
            nameof(QcSample),
            sample.Id.ToString(),
            changedBy,
            before,
            System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.ReceiptId, sample.IsDeleted, sample.DeletedAt, sample.DeleteReason, PhotoCount = sample.Photos.Count(x => !x.IsDeleted) }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (sample.ReceiptId, null);
    }

    public async Task<string?> UpdateSampleTypeAsync(UpdateSampleTypeForm form, CancellationToken cancellationToken)
    {
        if (!await CanEditSamplesAsync(cancellationToken))
        {
            return "Daily QC Edit access is required to change sample type.";
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .Include(x => x.FieldSampleFruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.SampleId && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var sampleType = await dbContext.SampleTypes
            .SingleOrDefaultAsync(x => x.Id == form.SampleTypeId && x.IsActive, cancellationToken);
        if (sampleType is null || !IsReceiptSampleTypeName(sampleType.Name))
        {
            return "Select Receiving Sample, Door Sample, or Lot Sample.";
        }

        var expectedSampleType = ReceiptQcSampleCoordinator.ExpectedSampleTypeName(sample.Receipt?.ReceiptType);
        if (expectedSampleType is not null
            && !string.Equals(sampleType.Name, expectedSampleType, StringComparison.OrdinalIgnoreCase))
        {
            return $"This {sample.Receipt!.ReceiptType} must use {expectedSampleType}.";
        }

        if (sample.SampleTypeId == sampleType.Id)
        {
            return null;
        }

        var isSent = sample.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase);
        var isAdmin = await HasAccessAsync(ApplicationAreas.DailyQc, PageAccessLevel.Admin, cancellationToken);
        if (isSent && !isAdmin)
        {
            return "Daily QC Admin access is required to change sample type after QC Summary email has been sent.";
        }

        var changedBy = GetCurrentUserEmail() ?? "unknown";
        var before = System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.SampleTypeId, SampleType = sample.SampleType.Name, sample.EmailStatus });
        sample.SampleTypeId = sampleType.Id;
        sample.SampleType = sampleType;
        sample.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await MarkSampleNeedsResendIfSentAsync(sample, "sample-type-change", changedBy, before, cancellationToken);
        await RefreshSampleStatusesAsync(sample, cancellationToken);

        if (isSent)
        {
            await AddAuditAsync(
                "sample-type-change-after-send",
                nameof(QcSample),
                sample.Id.ToString(),
                changedBy,
                before,
                System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.SampleTypeId, SampleType = sampleType.Name, sample.EmailStatus }),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SaveFruitReadingsAsync(SaveFruitReadingsForm form, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == form.SampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        if (form.Rows.Count == 0)
        {
            return "At least one grid row must be submitted.";
        }

        var allowedSampleSizes = await GetAllowedSampleSizesAsync(cancellationToken);
        if (!allowedSampleSizes.Contains(form.TargetSampleSize))
        {
            return $"Sample size must be one of: {string.Join(", ", allowedSampleSizes)}.";
        }

        var existingMaxRowNumber = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => (int?)x.RowNumber)
            .MaxAsync(cancellationToken) ?? 0;
        var maxAllowedRowNumber = Math.Max(form.TargetSampleSize, existingMaxRowNumber);
        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key < 1 || x.Key > maxAllowedRowNumber) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return $"Rows must be unique and numbered 1 through {maxAllowedRowNumber}.";
        }

        var validGradeIds = await dbContext.Grades.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
        var validStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var defectTypes = await dbContext.DefectTypes.AsNoTracking().ToListAsync(cancellationToken);
        var validDefectIds = defectTypes.Select(x => x.Id).ToHashSet();
        var otherDefectId = defectTypes.FirstOrDefault(x => x.Name == "Other")?.Id;
        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.FruitType == sample.Receipt.FruitProfile.FruitType && x.IsActive)
            .ToListAsync(cancellationToken);
        var existingRows = await dbContext.QcFruitReadings
            .Include(x => x.Defects)
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var beforeSnapshot = BuildFruitReadingSnapshot(sample, existingRows);
        sample.ActualSampleSize = form.TargetSampleSize;

        foreach (var submittedRow in form.Rows.OrderBy(x => x.RowNumber))
        {
            var selectedDefectIds = submittedRow.DefectTypeIds.Distinct().ToList();
            if (submittedRow.GradeId is not null && !validGradeIds.Contains(submittedRow.GradeId.Value))
            {
                return $"Row {submittedRow.RowNumber} has an invalid grade.";
            }

            if (submittedRow.StarchScaleValueId is not null && !validStarchIds.Contains(submittedRow.StarchScaleValueId.Value))
            {
                return $"Row {submittedRow.RowNumber} has an invalid starch value.";
            }

            if (selectedDefectIds.Any(x => !validDefectIds.Contains(x)))
            {
                return $"Row {submittedRow.RowNumber} has an invalid defect.";
            }

            var entryStatus = GetFruitRowEntryStatus(submittedRow, selectedDefectIds);
            var isCompleted = entryStatus == FruitRowEntryStatus.Complete;

            var reading = existingRows.SingleOrDefault(x => x.RowNumber == submittedRow.RowNumber);
            if (reading is null)
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sample.Id,
                    RowNumber = submittedRow.RowNumber,
                    SizeStatus = "NotCalculated",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.QcFruitReadings.Add(reading);
            }

            var size = CalculateSize(submittedRow.WeightGrams, thresholds);
            reading.Pressure1Lbs = submittedRow.Pressure1Lbs;
            reading.Pressure1Source = submittedRow.Pressure1Lbs is null ? null : "Manual";
            reading.Pressure2Lbs = submittedRow.Pressure2Lbs;
            reading.Pressure2Source = submittedRow.Pressure2Lbs is null ? null : "Manual";
            reading.WeightGrams = submittedRow.WeightGrams;
            reading.StarchScaleValueId = submittedRow.StarchScaleValueId;
            reading.GradeId = submittedRow.GradeId;
            reading.SizeCategory = size.SizeCategory;
            reading.SizeStatus = size.SizeStatus;
            reading.IsCompleted = isCompleted;
            reading.UpdatedAt = DateTimeOffset.UtcNow;

            dbContext.QcFruitDefects.RemoveRange(reading.Defects);
            reading.DefectsInspected = submittedRow.DefectsInspected || selectedDefectIds.Count > 0;
            foreach (var defectTypeId in selectedDefectIds)
            {
                reading.Defects.Add(new QcFruitDefect
                {
                    DefectTypeId = defectTypeId,
                    Notes = defectTypeId == otherDefectId ? submittedRow.OtherDefectNotes?.Trim() : null
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var afterRows = await dbContext.QcFruitReadings
            .AsNoTracking()
            .Include(x => x.Defects)
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var afterSnapshot = BuildFruitReadingSnapshot(sample, afterRows);
        if (!string.Equals(beforeSnapshot, afterSnapshot, StringComparison.Ordinal))
        {
            await MarkSampleNeedsResendIfSentAsync(sample, "fruit-row-change", GetCurrentUserEmail() ?? "unknown", beforeSnapshot, cancellationToken);
        }
        await RefreshSampleStatusesAsync(sample, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<FieldSampleAutosaveResult> AutosaveFruitReadingsAsync(
        long sampleId,
        FieldSampleAutosaveRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChangeId) || request.ChangeId.Length > 100)
        {
            return new FieldSampleAutosaveResult { Error = "A valid autosave change identifier is required." };
        }

        if (request.MetadataChanges.Count > 0)
        {
            return new FieldSampleAutosaveResult
            {
                ValidationErrors = [new("sample", null, "Metadata", "Receipt details must be edited through the receipt workflow.")]
            };
        }

        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId && x.ReceiptId != null && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return new FieldSampleAutosaveResult { Error = "Receipt-backed QC sample not found." };
        }

        var allowedSampleSizes = await GetAllowedSampleSizesAsync(cancellationToken);
        var requestedTarget = request.TargetSampleSize ?? sample.ActualSampleSize ?? allowedSampleSizes.First();
        if (!allowedSampleSizes.Contains(requestedTarget))
        {
            return new FieldSampleAutosaveResult
            {
                ValidationErrors = [new("sample", null, "TargetSampleSize", $"Choose an allowed sample size: {string.Join(", ", allowedSampleSizes)}.")]
            };
        }

        var rows = await dbContext.QcFruitReadings
            .Include(x => x.Defects).ThenInclude(x => x.DefectType)
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var maximumRow = Math.Max(requestedTarget, rows.Select(x => x.RowNumber).DefaultIfEmpty(0).Max());
        var conflicts = new List<FieldSampleAutosaveConflict>();
        var validation = new List<FieldSampleAutosaveValidationError>();

        foreach (var rowChange in request.RowChanges)
        {
            if (rowChange.RowNumber < 1 || rowChange.RowNumber > maximumRow)
            {
                validation.Add(new("row", rowChange.RowNumber, "RowNumber", $"Fruit row must be between 1 and {maximumRow}."));
                continue;
            }

            var row = rows.SingleOrDefault(x => x.RowNumber == rowChange.RowNumber);
            foreach (var change in rowChange.Changes.OrderBy(x => x.Field == "DefectTypeIds" ? 0 : 1))
            {
                if (!IsReceiptAutosaveField(change.Field))
                {
                    validation.Add(new("row", rowChange.RowNumber, change.Field, "This fruit-row field cannot be autosaved."));
                    continue;
                }

                var current = ReceiptAutosaveValue(row, change.Field);
                if (!ReceiptAutosaveValuesEqual(current, change.OriginalValue)
                    && !ReceiptAutosaveValuesEqual(current, change.Value))
                {
                    conflicts.Add(new(
                        "row",
                        rowChange.RowNumber,
                        change.Field,
                        change.Value,
                        current,
                        change.Field is "Pressure1Lbs" or "Pressure2Lbs"
                            ? "QC Station or another user saved a newer pressure value. Choose which value to keep."
                            : "This field changed after the page loaded. Choose which value to keep."));
                }
            }
        }

        if (conflicts.Count > 0 || validation.Count > 0)
        {
            if (conflicts.Count > 0)
            {
                await AddAuditAsync(
                    "autosave-conflict",
                    nameof(QcSample),
                    sample.Id.ToString(),
                    GetCurrentUserEmail() ?? "unknown",
                    null,
                    JsonSerializer.Serialize(new { request.ChangeId, Conflicts = conflicts.Select(x => new { x.RowNumber, x.Field, x.Message }) }),
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return await ReceiptAutosaveResultAsync(sample, conflicts, validation, cancellationToken);
        }

        var activeGradeIds = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var activeStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var activeDefects = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var activeDefectIds = activeDefects.Select(x => x.Id).ToHashSet();
        var otherDefectId = activeDefects.FirstOrDefault(x => x.Name == "Other")?.Id;
        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && x.FruitType == sample.Receipt.FruitProfile.FruitType)
            .ToListAsync(cancellationToken);
        var beforeSnapshot = BuildFruitReadingSnapshot(sample, rows);
        var changedFields = new List<object>();

        foreach (var rowChange in request.RowChanges)
        {
            var reading = rows.SingleOrDefault(x => x.RowNumber == rowChange.RowNumber);
            if (reading is null)
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sample.Id,
                    RowNumber = rowChange.RowNumber,
                    SizeStatus = SizeCalculationService.NotCalculated,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                rows.Add(reading);
                dbContext.QcFruitReadings.Add(reading);
            }

            var rowChanged = false;
            var oldSizeCategory = reading.SizeCategory;
            var oldSizeStatus = reading.SizeStatus;
            foreach (var change in rowChange.Changes.OrderBy(x => x.Field == "DefectTypeIds" ? 0 : 1))
            {
                var oldValue = ReceiptAutosaveValue(reading, change.Field);
                if (ReceiptAutosaveValuesEqual(oldValue, change.Value)) continue;
                if (ApplyReceiptAutosaveChange(reading, change, request.Source, activeGradeIds, activeStarchIds, activeDefectIds, otherDefectId, validation))
                {
                    rowChanged = true;
                    changedFields.Add(new { Row = rowChange.RowNumber, change.Field, OldValue = oldValue, NewValue = change.Value });
                }
            }

            if (rowChanged)
            {
                var size = SizeCalculationService.Calculate(reading.WeightGrams, thresholds);
                reading.SizeCategory = size.SizeCategory;
                reading.SizeStatus = size.SizeStatus;
                if (oldSizeCategory != reading.SizeCategory || !string.Equals(oldSizeStatus, reading.SizeStatus, StringComparison.Ordinal))
                {
                    changedFields.Add(new { Row = rowChange.RowNumber, Field = "CalculatedSize", OldValue = oldSizeCategory?.ToString(CultureInfo.InvariantCulture) ?? oldSizeStatus, NewValue = reading.SizeCategory?.ToString(CultureInfo.InvariantCulture) ?? reading.SizeStatus });
                }
                reading.IsCompleted = HasCompletionFields(reading.Pressure1Lbs, reading.Pressure2Lbs, reading.WeightGrams, reading.GradeId);
                reading.UpdatedAt = DateTimeOffset.UtcNow;
                reading.FieldVersion++;
            }
        }

        if (validation.Count > 0)
        {
            dbContext.ChangeTracker.Clear();
            sample = await dbContext.QcSamples.AsNoTracking().SingleAsync(x => x.Id == sampleId, cancellationToken);
            return await ReceiptAutosaveResultAsync(sample, [], validation, cancellationToken);
        }

        var targetChanged = sample.ActualSampleSize != requestedTarget;
        sample.ActualSampleSize = requestedTarget;
        if (changedFields.Count > 0 || targetChanged)
        {
            sample.FieldSampleAutosaveVersion++;
            sample.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await MarkSampleNeedsResendIfSentAsync(sample, "autosave", GetCurrentUserEmail() ?? "unknown", beforeSnapshot, cancellationToken);
            await RefreshSampleStatusesAsync(sample, cancellationToken);
            await AddAuditAsync(
                "autosave",
                nameof(QcSample),
                sample.Id.ToString(),
                GetCurrentUserEmail() ?? "unknown",
                beforeSnapshot,
                JsonSerializer.Serialize(new { request.ChangeId, Source = NormalizeAutosaveSource(request.Source), TargetSampleSize = requestedTarget, Changes = changedFields }),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await ReceiptAutosaveResultAsync(sample, [], [], cancellationToken);
    }

    public async Task<StarchTestViewModel> GetStarchTestAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (sample is null)
            {
                return new StarchTestViewModel { DataWarning = "QC sample not found." };
            }

            var allowedSampleSizes = await GetAllowedSampleSizesAsync(cancellationToken);
            var targetSampleSize = ResolveTargetSampleSize(sample.ActualSampleSize, allowedSampleSizes);
            var rowModels = await GetFruitReadingRowsAsync(id, targetSampleSize, cancellationToken);
            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
            var photos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == id && !x.IsDeleted && (x.PhotoType == "FruitAfterStarch" || x.PhotoType == "Other")).OrderByDescending(x => x.CapturedAt).ToListAsync(cancellationToken);
            return new StarchTestViewModel
            {
                FruitType = sample.Receipt.FruitProfile.FruitType,
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                FruitRows = rowModels,
                StarchScaleValues = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken),
                Readiness = readiness,
                QcStationStatus = BuildQcStationStatus(sample.QcStation),
                PhotoGroups = GroupPhotos(photos, await CanEditSamplesAsync(cancellationToken), sample.Id),
                DeviceCapture = await GetDeviceCaptureSettingsAsync(cancellationToken),
                AddPhotoForm = new AddPhotoMetadataForm
                {
                    QcSampleId = sample.Id,
                    PhotoType = "FruitAfterStarch",
                    PhotoSource = "Upload File",
                    ContentType = "image/jpeg"
                },
                StarchForm = new SaveStarchTestForm
                {
                    SampleId = sample.Id,
                    Rows = rowModels.Select(row => new StarchTestEditRow
                    {
                        RowNumber = row.RowNumber,
                        StarchScaleValueId = row.StarchScaleValueId
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Starch page model failed for SampleId {SampleId}. ErrorType: {ErrorType}. Message: {Message}", id, ex.GetType().Name, SafeErrorMessage(ex));
            return new StarchTestViewModel { DataWarning = $"Starch sample {id} could not be loaded. The failure was logged with Sample ID {id}; retry the page or contact support." };
        }
    }

    public async Task<string?> SaveStarchTestAsync(SaveStarchTestForm form, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.SingleOrDefaultAsync(x => x.Id == form.SampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var existingMaxRowNumber = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .Select(x => (int?)x.RowNumber)
            .MaxAsync(cancellationToken) ?? 0;
        var targetSampleSize = sample.ActualSampleSize is > 0 ? sample.ActualSampleSize.Value : 10;
        var maxAllowedRowNumber = Math.Max(targetSampleSize, existingMaxRowNumber);
        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key < 1 || x.Key > maxAllowedRowNumber) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return $"Rows must be unique and numbered 1 through {maxAllowedRowNumber}.";
        }

        var validStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Select(x => x.Id).ToHashSetAsync(cancellationToken);
        if (form.Rows.Any(x => x.StarchScaleValueId is not null && !validStarchIds.Contains(x.StarchScaleValueId.Value)))
        {
            return "One or more starch values are invalid.";
        }

        var existingRows = await dbContext.QcFruitReadings
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        var beforeSnapshot = BuildStarchSnapshot(sample, existingRows);
        foreach (var submittedRow in form.Rows.OrderBy(x => x.RowNumber))
        {
            var reading = existingRows.SingleOrDefault(x => x.RowNumber == submittedRow.RowNumber);
            if (reading is null && submittedRow.StarchScaleValueId is null)
            {
                continue;
            }

            if (reading is null)
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sample.Id,
                    RowNumber = submittedRow.RowNumber,
                    SizeStatus = "NotCalculated",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.QcFruitReadings.Add(reading);
            }

            reading.StarchScaleValueId = submittedRow.StarchScaleValueId;
            reading.IsCompleted = HasCompletionFields(reading.Pressure1Lbs, reading.Pressure2Lbs, reading.WeightGrams, reading.GradeId);
            reading.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var afterRows = await dbContext.QcFruitReadings.AsNoTracking().Where(x => x.QcSampleId == sample.Id).ToListAsync(cancellationToken);
        var afterSnapshot = BuildStarchSnapshot(sample, afterRows);
        if (!string.Equals(beforeSnapshot, afterSnapshot, StringComparison.Ordinal))
        {
            await MarkSampleNeedsResendIfSentAsync(sample, "starch-change", GetCurrentUserEmail() ?? "unknown", beforeSnapshot, cancellationToken);
        }
        await RefreshSampleStatusesAsync(sample, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<OverrideSendViewModel> GetOverrideSendAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (sample is null)
            {
                return new OverrideSendViewModel { DataWarning = "QC sample not found." };
            }

            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
            var sender = await GetCurrentUserAsync(cancellationToken);
            var senderEmail = sender?.Email ?? GetCurrentUserEmail();
            var senderDomain = GoogleAuthenticationOptions.GetEmailDomain(senderEmail);
            var credentialDiagnostic = sender is null
                ? new GoogleCredentialDiagnostic(false, false)
                : await googleCredentialStore.GetDiagnosticAsync(sender, cancellationToken);

            var recipientResolution = await qcEmailRecipientResolver.ResolveForSampleAsync(sample.Id, null, cancellationToken);
            return new OverrideSendViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                Readiness = readiness,
                Checklist = readiness.Checklist,
                SenderEmail = senderEmail,
                SenderDomain = senderDomain,
                SenderDomainAllowed = senderDomain is not null && authOptions.AllowedDomains.Contains(senderDomain),
                RecipientEmail = recipientResolution.IsConfigured ? recipientResolution.Header : null,
                GmailReconnectRequired = !string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase)
                    || !credentialDiagnostic.GmailSendPermissionGranted,
                GmailCredentialPresent = credentialDiagnostic.CredentialPresent,
                GmailSendPermissionGranted = credentialDiagnostic.GmailSendPermissionGranted,
                GmailUserProviderEnabled = string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase),
                AllowedGoogleDomains = string.Join(", ", authOptions.AllowedDomains.OrderBy(x => x)),
                Form = new OverrideSendForm { SampleId = sample.Id }
            };
        }
        catch (Exception ex)
        {
            return new OverrideSendViewModel { DataWarning = DatabaseWarning(ex, "QC email override") };
        }
    }

    public async Task<string?> SendQcSummaryAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == sampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
        if (!readiness.IsReady)
        {
            return "QC Summary cannot be sent until required data, starch, and photos are complete. Use Manager/Admin override if needed.";
        }

        return await SendAndLogQcSummaryAsync(sample, readiness, isOverride: false, overrideReason: null, cancellationToken);
    }

    public async Task<string?> LogOverrideSendAsync(OverrideSendForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.OverrideReason))
        {
            return "Override reason is required.";
        }

        if (!form.ConfirmOverride)
        {
            return "Confirm the override before logging it.";
        }

        var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == form.SampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
        return await SendAndLogQcSummaryAsync(sample, readiness, isOverride: true, overrideReason: form.OverrideReason.Trim(), cancellationToken);
    }

    private async Task<string?> SendAndLogQcSummaryAsync(QcSample sample, ReadinessViewModel readiness, bool isOverride, string? overrideReason, CancellationToken cancellationToken)
    {
        var originalSampleTypeId = sample.SampleTypeId;
        var originalSampleTypeName = sample.SampleType.Name;
        var sender = await GetCurrentUserAsync(cancellationToken);
        if (sender is null)
        {
            return "A logged-in user is required to send QC Summary email.";
        }

        var recipientResolution = await qcEmailRecipientResolver.ResolveForSampleAsync(sample.Id, null, cancellationToken);
        if (!recipientResolution.IsConfigured)
        {
            return "No QC email recipients are configured. Admins can set them under Admin -> Configuration -> QC Email Recipients.";
        }

        var emailContent = await emailComposer.ComposeAsync(sample, readiness, sender, isOverride, overrideReason, cancellationToken);
        var recipients = recipientResolution.Header;
        var message = new QcEmailMessage(sender.Email, recipients, sample.TakenByUser?.Email, emailContent.Subject, emailContent.TextBody, emailContent.HtmlBody, emailContent.InlineImages);

        var now = DateTimeOffset.UtcNow;
        var isResend = sample.EmailStatus.Contains("resend", StringComparison.OrdinalIgnoreCase)
            || sample.EmailStatus.Contains("Changed after sent", StringComparison.OrdinalIgnoreCase);
        var sendResult = await emailSender.SendAsync(sender, message, cancellationToken);
        var status = sendResult.Success ? "Sent" : "Failed";

        dbContext.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            ReceiptId = sample.ReceiptId!.Value,
            QcSampleId = sample.Id,
            FromAddress = sender.Email,
            ToAddress = recipients,
            ReplyToAddress = sample.TakenByUser?.Email,
            Subject = emailContent.Subject,
            Status = status,
            MessageId = sendResult.MessageId,
            SentByUserId = sender.Id,
            SentAt = sendResult.Success ? now : null,
            IsResend = isResend,
            IsOverride = isOverride,
            OverrideReason = overrideReason,
            MissingItemsSnapshot = string.Join(Environment.NewLine, readiness.MissingItems),
            EmailBodySnapshot = null,
            ReportSnapshotReference = sendResult.Success
                ? $"Gmail message id: {sendResult.MessageId ?? "(not returned)"}; inline images: {emailContent.InlineImages.Count}"
                : $"Send failed: {sendResult.Error}",
            CreatedAt = now
        });

        if (sendResult.Success)
        {
            var trackedSample = await dbContext.QcSamples.SingleAsync(x => x.Id == sample.Id, cancellationToken);
            trackedSample.SampleTypeId = originalSampleTypeId;
            trackedSample.EmailStatus = "Sent";
            trackedSample.Status = "Sent";
            trackedSample.UpdatedAt = now;
        }

        await AddAuditAsync(
            sendResult.Success ? "send" : "send-failed",
            "qc-summary-email",
            sample.Id.ToString(),
            sender.Email,
            null,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                Sender = sender.Email,
                To = recipients,
                Subject = emailContent.Subject,
                Status = status,
                GmailMessageId = sendResult.MessageId,
                Failure = sendResult.Success ? null : sendResult.Error,
                recipientResolution.ResolvedOrchardId,
                recipientResolution.OrchardCouldNotBeResolved,
                recipientResolution.OrchardHadNoConfiguredManager,
                recipientResolution.ResolvedGrowerNumberId,
                recipientResolution.ResolvedGrowerNumber,
                recipientResolution.GrowerNumberCouldNotBeResolved,
                SkippedInvalidRecipientCount = recipientResolution.SkippedInvalidAddresses.Count,
                SampleType = originalSampleTypeName,
                IsOverride = isOverride
            }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return sendResult.Success
            ? null
            : sendResult.ReconnectRequired
                ? "Gmail permission is required. Please reconnect Google/Gmail."
                : $"QC Summary email failed: {sendResult.Error}";
    }

    public async Task<string?> AddPhotoMetadataAsync(AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Photo metadata request received. ReceiptId: {ReceiptId}. QcSampleId: {QcSampleId}. PhotoType: {PhotoType}. PhotoSource: {PhotoSource}. Uploaded file present: {HasFile}. FileName: {FileName}. ContentType: {ContentType}. Size: {Size}. Selected storage provider: {StorageProvider}.",
            form.ReceiptId,
            form.QcSampleId,
            form.PhotoType,
            form.PhotoSource,
            form.PhotoFile is not null,
            form.PhotoFile?.FileName ?? "(none)",
            form.PhotoFile?.ContentType ?? "(none)",
            form.PhotoFile?.Length ?? 0,
            fileStorageOptions.Provider);

        if ((form.ReceiptId is null && form.QcSampleId is null) || (form.ReceiptId is not null && form.QcSampleId is not null))
        {
            return "Photo metadata must attach to either a receipt or a QC sample.";
        }

        if (string.IsNullOrWhiteSpace(form.PhotoType) || string.IsNullOrWhiteSpace(form.PhotoSource))
        {
            return "Photo type and source are required.";
        }

        var fileValidationError = PhotoUploadValidator.Validate(form);
        if (fileValidationError is not null)
        {
            logger.LogWarning(
                "Photo upload validation failed. ReceiptId: {ReceiptId}. QcSampleId: {QcSampleId}. PhotoType: {PhotoType}. Error: {Error}",
                form.ReceiptId,
                form.QcSampleId,
                form.PhotoType,
                fileValidationError);
            return fileValidationError;
        }

        var receipt = form.ReceiptId is null
            ? null
            : await dbContext.Receipts
                .Include(x => x.Warehouse)
                .SingleOrDefaultAsync(x => x.Id == form.ReceiptId, cancellationToken);
        if (form.ReceiptId is not null && receipt is null)
        {
            return "Receipt not found.";
        }

        var sample = form.QcSampleId is null
            ? null
            : await dbContext.QcSamples
                .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
                .SingleOrDefaultAsync(x => x.Id == form.QcSampleId, cancellationToken);
        if (form.QcSampleId is not null && sample is null)
        {
            return "QC sample not found.";
        }

        var capturedAt = DateTimeOffset.UtcNow;
        receipt ??= sample?.Receipt;
        var storageContext = receipt is not null
            ? new FileStorageTargetContext(receipt.CropYear, receipt.Warehouse.Code, receipt.CompuTechReceiptId, form.PhotoType.Trim(), capturedAt)
            : sample is not null && sample.SampleType.Name.Contains("field", StringComparison.OrdinalIgnoreCase)
                ? new FileStorageTargetContext(sample.SampleTakenAt.Year, "FIELD", $"FieldSample-{sample.Id}", form.PhotoType.Trim(), capturedAt)
                : null;
        if (storageContext is null)
        {
            return "Receipt or Field Sample context is required for photo storage.";
        }

        FileStorageReference reference;
        try
        {
            reference = await SavePhotoFileOrPlaceholderAsync(form, storageContext, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(
                ex,
                "Photo storage save failed. ReceiptId: {ReceiptId}. QcSampleId: {QcSampleId}. PhotoType: {PhotoType}. StorageProvider: {StorageProvider}.",
                form.ReceiptId,
                form.QcSampleId,
                form.PhotoType,
                fileStorageOptions.Provider);
            return FormatStorageError(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected photo storage save failed. ReceiptId: {ReceiptId}. QcSampleId: {QcSampleId}. PhotoType: {PhotoType}. StorageProvider: {StorageProvider}.",
                form.ReceiptId,
                form.QcSampleId,
                form.PhotoType,
                fileStorageOptions.Provider);
            return FormatStorageError(ex);
        }

        var capturedByUserId = await GetCurrentUserIdAsync(cancellationToken);

        logger.LogInformation(
            "QcPhoto metadata insert started. ReceiptId: {ReceiptId}. QcSampleId: {QcSampleId}. StorageProvider: {StorageProvider}. FileId: {FileId}. FolderId: {FolderId}.",
            form.ReceiptId,
            form.QcSampleId,
            reference.StorageProvider,
            reference.FileId ?? reference.StorageKey,
            reference.FolderId ?? reference.TargetPath);

        var photo = new QcPhoto
        {
            ReceiptId = form.ReceiptId,
            QcSampleId = form.QcSampleId,
            PhotoType = QcPhotoRequirementPolicy.NormalizePhotoType(form.PhotoType),
            PhotoSource = form.PhotoSource.Trim(),
            FileName = reference.FileName,
            ContentType = reference.ContentType,
            FileSizeBytes = reference.FileSizeBytes,
            StorageProvider = reference.StorageProvider,
            DriveId = reference.DriveId,
            FileId = reference.FileId,
            FolderId = reference.FolderId,
            SharePointDriveId = reference.DriveId ?? reference.FolderId ?? reference.TargetPath,
            SharePointItemId = reference.FileId ?? reference.StorageKey,
            WebUrl = reference.WebUrl,
            CapturedByUserId = capturedByUserId,
            CapturedAt = capturedAt,
            UploadedAt = form.PhotoFile is null ? null : capturedAt
        };
        dbContext.QcPhotos.Add(photo);

        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(
            "add-photo",
            nameof(QcPhoto),
            photo.Id.ToString(),
            GetCurrentUserEmail() ?? "unknown",
            null,
            JsonSerializer.Serialize(new { photo.Id, photo.ReceiptId, photo.QcSampleId, photo.PhotoType, photo.FileName, photo.StorageProvider, photo.FileId }),
            cancellationToken);
        logger.LogInformation(
            "QcPhoto metadata insert succeeded. QcPhotoId: {QcPhotoId}. StorageProvider: {StorageProvider}. FileId: {FileId}. FolderId: {FolderId}.",
            photo.Id,
            photo.StorageProvider,
            photo.FileId ?? photo.SharePointItemId,
            photo.FolderId ?? photo.SharePointDriveId);
        if (sample is not null)
        {
            if (sample.ReceiptId is null)
            {
                await MarkSampleNeedsResendIfSentAsync(sample, "photo-added", GetCurrentUserEmail() ?? "unknown", null, cancellationToken);
                sample.PhotoStatus = "Optional Photos Attached";
                sample.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                await MarkSampleNeedsResendIfSentAsync(sample, "photo-added", GetCurrentUserEmail() ?? "unknown", null, cancellationToken);
                await RefreshSampleStatusesAsync(sample, cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (form.ReceiptId is not null)
        {
            var receiptSamples = await dbContext.QcSamples.Where(x => x.ReceiptId == form.ReceiptId).ToListAsync(cancellationToken);
            foreach (var receiptSample in receiptSamples)
            {
                await MarkSampleNeedsResendIfSentAsync(receiptSample, "receipt-photo-added", GetCurrentUserEmail() ?? "unknown", null, cancellationToken);
                await RefreshSampleStatusesAsync(receiptSample, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    public async Task<string?> AddSamplePhotoMetadataAsync(long sampleId, AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var canEdit = sample.SampleType.Name.Contains("field", StringComparison.OrdinalIgnoreCase)
            ? await HasAccessAsync(ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken)
            : await CanEditSamplesAsync(cancellationToken);
        if (!canEdit)
        {
            return "You do not have permission to add photos.";
        }

        form.PhotoType = QcPhotoRequirementPolicy.NormalizePhotoType(form.PhotoType);
        var available = photoRequirementPolicy.GetAvailablePhotoTypes(
            sample.SampleType.Name,
            sample.Receipt?.FruitProfile.FruitType ?? sample.FieldSampleFruitProfile?.FruitType);
        var selected = available.SingleOrDefault(x => string.Equals(x.PhotoType, form.PhotoType, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.Equals(form.PhotoType, "Other", StringComparison.OrdinalIgnoreCase))
        {
            return "That photo type is not available for this sample type.";
        }

        form.ReceiptId = selected?.ReceiptLevel == true ? sample.ReceiptId : null;
        form.QcSampleId = selected?.ReceiptLevel == true ? null : sample.Id;
        return await AddPhotoMetadataAsync(form, cancellationToken);
    }

    public async Task<PhotoReclassificationResult> ReclassifySamplePhotoAsync(
        long sampleId,
        long photoId,
        string targetPhotoType,
        CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples
            .Include(x => x.SampleType)
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .Include(x => x.FieldSampleFruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return new(false, "QC sample not found.", photoId);
        }

        if (sample.ReceiptId is long receiptId
            && await dbContext.QcSamples.CountAsync(
                x => x.ReceiptId == receiptId && !x.IsDeleted,
                cancellationToken) > 1)
        {
            return new(false, ReceiptQcSampleCoordinator.HistoricalConflictMessage, photoId);
        }

        var isFieldSample = sample.ReceiptId is null
            || sample.SampleType.Name.Contains("field", StringComparison.OrdinalIgnoreCase);
        var canEdit = isFieldSample
            ? await HasAccessAsync(ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken)
            : await CanEditSamplesAsync(cancellationToken);
        if (!canEdit)
        {
            return new(false, "You do not have permission to reclassify photos for this sample.", photoId);
        }

        var normalizedTarget = QcPhotoRequirementPolicy.NormalizePhotoType(targetPhotoType);
        var fruitType = sample.Receipt?.FruitProfile.FruitType ?? sample.FieldSampleFruitProfile?.FruitType;
        var available = photoRequirementPolicy.GetAvailablePhotoTypes(sample.SampleType.Name, fruitType);
        var selected = available.SingleOrDefault(x =>
            string.Equals(x.PhotoType, normalizedTarget, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.Equals(normalizedTarget, "Other", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "That photo type is not available for this sample type.", photoId);
        }

        var photo = await dbContext.QcPhotos.SingleOrDefaultAsync(
            x => x.Id == photoId
                && !x.IsDeleted
                && (x.QcSampleId == sampleId
                    || (sample.ReceiptId != null && x.ReceiptId == sample.ReceiptId)),
            cancellationToken);
        if (photo is null)
        {
            return new(false, "Photo was not found in this sample workflow.", photoId);
        }

        var receiptLevel = selected?.ReceiptLevel == true && sample.ReceiptId is not null;
        var targetReceiptId = receiptLevel ? sample.ReceiptId : null;
        long? targetSampleId = receiptLevel ? null : sample.Id;
        var oldPhotoType = QcPhotoRequirementPolicy.NormalizePhotoType(photo.PhotoType);
        if (string.Equals(oldPhotoType, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            && photo.ReceiptId == targetReceiptId
            && photo.QcSampleId == targetSampleId)
        {
            return new(true, null, photo.Id, oldPhotoType, normalizedTarget, receiptLevel);
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var changedBy = GetCurrentUserEmail() ?? "unknown";
        var oldReceiptId = photo.ReceiptId;
        var oldSampleId = photo.QcSampleId;
        var before = JsonSerializer.Serialize(new
        {
            PhotoId = photo.Id,
            SampleId = sample.Id,
            ReceiptId = sample.ReceiptId,
            PhotoType = oldPhotoType,
            OwnershipScope = oldReceiptId is not null ? "Receipt" : "Sample",
            photo.FileId,
            photo.StorageProvider
        });

        photo.PhotoType = normalizedTarget;
        photo.ReceiptId = targetReceiptId;
        photo.QcSampleId = targetSampleId;
        await AddAuditAsync(
            "reclassify-photo",
            nameof(QcPhoto),
            photo.Id.ToString(),
            changedBy,
            before,
            JsonSerializer.Serialize(new
            {
                PhotoId = photo.Id,
                SampleId = sample.Id,
                ReceiptId = sample.ReceiptId,
                OldPhotoType = oldPhotoType,
                NewPhotoType = normalizedTarget,
                OldOwnershipScope = oldReceiptId is not null ? "Receipt" : "Sample",
                NewOwnershipScope = receiptLevel ? "Receipt" : "Sample",
                ChangedBy = changedBy,
                ChangedAt = DateTimeOffset.UtcNow,
                photo.FileId,
                photo.StorageProvider
            }),
            cancellationToken);

        if (sample.ReceiptId is not null)
        {
            var affectedSamples = await dbContext.QcSamples
                .Where(x => x.ReceiptId == sample.ReceiptId && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            foreach (var affectedSample in affectedSamples)
            {
                await MarkSampleNeedsResendIfSentAsync(affectedSample, "photo-reclassified", changedBy, before, cancellationToken);
                await RefreshSampleStatusesAsync(affectedSample, cancellationToken);
            }
        }
        else
        {
            await MarkSampleNeedsResendIfSentAsync(sample, "photo-reclassified", changedBy, before, cancellationToken);
            sample.PhotoStatus = "Optional Photos Attached";
            sample.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(true, null, photo.Id, oldPhotoType, normalizedTarget, receiptLevel);
    }

    public async Task<string?> RemoveSamplePhotoAsync(long sampleId, long photoId, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples
            .Include(x => x.Receipt)
            .Include(x => x.SampleType)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var canEdit = sample.SampleType.Name.Contains("field", StringComparison.OrdinalIgnoreCase)
            ? await HasAccessAsync(ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken)
            : await CanEditSamplesAsync(cancellationToken);
        if (!canEdit)
        {
            return "You do not have permission to remove photos.";
        }

        var photo = await dbContext.QcPhotos
            .SingleOrDefaultAsync(x => x.Id == photoId && !x.IsDeleted && (x.QcSampleId == sampleId || x.ReceiptId == sample.ReceiptId), cancellationToken);
        if (photo is null)
        {
            return "Photo was not found.";
        }

        var changedBy = GetCurrentUserEmail() ?? "unknown";
        var before = System.Text.Json.JsonSerializer.Serialize(new
        {
            photo.Id,
            photo.ReceiptId,
            photo.QcSampleId,
            photo.PhotoType,
            photo.FileName,
            photo.FileId,
            photo.WebUrl
        });

        photo.IsDeleted = true;
        photo.DeletedAt = DateTimeOffset.UtcNow;
        photo.DeletedByUserId = await GetCurrentUserIdAsync(cancellationToken);
        photo.DeleteReason = "Removed from sample detail";

        await AddAuditAsync(
            "remove-photo",
            nameof(QcPhoto),
            photo.Id.ToString(),
            changedBy,
            before,
            System.Text.Json.JsonSerializer.Serialize(new { photo.Id, photo.IsDeleted, photo.DeletedAt, photo.PhotoType, photo.FileId }),
            cancellationToken);

        if (photo.ReceiptId is not null && photo.QcSampleId is null)
        {
            var receiptSamples = await dbContext.QcSamples.Where(x => x.ReceiptId == sample.ReceiptId && !x.IsDeleted).ToListAsync(cancellationToken);
            foreach (var receiptSample in receiptSamples)
            {
                await MarkSampleNeedsResendIfSentAsync(receiptSample, "photo-removed", changedBy, before, cancellationToken);
                await RefreshSampleStatusesAsync(receiptSample, cancellationToken);
            }
        }
        else
        {
            if (sample.ReceiptId is null)
            {
                await MarkSampleNeedsResendIfSentAsync(sample, "photo-removed", changedBy, before, cancellationToken);
                sample.PhotoStatus = await dbContext.QcPhotos.AnyAsync(x => x.QcSampleId == sample.Id && !x.IsDeleted && x.Id != photo.Id, cancellationToken)
                    ? "Optional Photos Attached"
                    : "Not Required";
                sample.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                await MarkSampleNeedsResendIfSentAsync(sample, "photo-removed", changedBy, before, cancellationToken);
                await RefreshSampleStatusesAsync(sample, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> RemoveReceiptPhotoAsync(long receiptId, long photoId, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, cancellationToken))
        {
            return "You do not have permission to remove receipt photos.";
        }

        var receiptExists = await dbContext.Receipts
            .AsNoTracking()
            .AnyAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (!receiptExists)
        {
            return "Receipt not found.";
        }

        var photo = await dbContext.QcPhotos
            .SingleOrDefaultAsync(x => x.Id == photoId && x.ReceiptId == receiptId && x.QcSampleId == null && !x.IsDeleted, cancellationToken);
        if (photo is null)
        {
            return "Receipt photo was not found.";
        }

        var changedBy = GetCurrentUserEmail() ?? "unknown";
        var before = JsonSerializer.Serialize(new
        {
            photo.Id,
            photo.ReceiptId,
            photo.QcSampleId,
            photo.PhotoType,
            photo.FileName,
            photo.FileId,
            photo.WebUrl
        });

        photo.IsDeleted = true;
        photo.DeletedAt = DateTimeOffset.UtcNow;
        photo.DeletedByUserId = await GetCurrentUserIdAsync(cancellationToken);
        photo.DeleteReason = "Removed from receipt detail";

        await AddAuditAsync(
            "remove-photo",
            nameof(QcPhoto),
            photo.Id.ToString(),
            changedBy,
            before,
            JsonSerializer.Serialize(new { photo.Id, photo.IsDeleted, photo.DeletedAt, photo.PhotoType, photo.FileId }),
            cancellationToken);

        var receiptSamples = await dbContext.QcSamples
            .Where(x => x.ReceiptId == receiptId && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var receiptSample in receiptSamples)
        {
            await MarkSampleNeedsResendIfSentAsync(receiptSample, "photo-removed", changedBy, before, cancellationToken);
            await RefreshSampleStatusesAsync(receiptSample, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, CancellationToken cancellationToken) =>
        GetDailyQcDashboardAsync(warehouseId, status, "All", cancellationToken);

    public async Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, string? facility, CancellationToken cancellationToken)
    {
        facility = FacilityContext.Normalize(facility);
        try
        {
            var facilityWarehouseIds = await FacilityContext.GetWarehouseIdsAsync(facility, cancellationToken);
            if (warehouseId is int requestedWarehouseId && !facilityWarehouseIds.Contains(requestedWarehouseId))
            {
                warehouseId = null;
            }
            var todayRange = BusinessTime.UtcRangeForPacificDate(BusinessTime.PacificDate(BusinessTime.UtcNow));
            var query = QuerySamples().Where(x => x.SampleTakenAt >= todayRange.Start && x.SampleTakenAt < todayRange.End);
            if (facility != "All")
            {
                query = query.Where(x => facilityWarehouseIds.Contains(x.Receipt.WarehouseId));
            }
            if (warehouseId is not null)
            {
                query = query.Where(x => x.Receipt.WarehouseId == warehouseId);
            }

            var samples = await query.OrderByDescending(x => x.SampleTakenAt).ToListAsync(cancellationToken);
            var enriched = await EnrichSamplesAsync(samples, cancellationToken);
            enriched = FilterDailyQcSamples(enriched, status);
            return new DailyQcDashboardViewModel
            {
                WarehouseId = warehouseId,
                Facility = facility,
                Status = status,
                StatusDescription = BuildDailyQcStatusDescription(status),
                Warehouses = await dbContext.Warehouses.AsNoTracking().Where(x => facilityWarehouseIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Samples = enriched
            };
        }
        catch (Exception ex)
        {
            return new DailyQcDashboardViewModel { WarehouseId = warehouseId, Facility = facility, Status = status, DataWarning = DatabaseWarning(ex, "Daily QC dashboard") };
        }
    }

    private static IReadOnlyList<SampleListItemViewModel> FilterDailyQcSamples(IReadOnlyList<SampleListItemViewModel> samples, string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "readytosend" => samples.Where(IsReadyToEmail).ToList(),
            "missingdata" => samples.Where(x => !x.IsReady).ToList(),
            "needsreview" => samples.Where(x => x.ReviewReasons.Count > 0).ToList(),
            "sent" => samples.Where(x => x.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => samples
        };

    private static string? BuildDailyQcStatusDescription(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "readytosend" => "Showing samples with required data and photos ready for QC summary email.",
            "missingdata" => "Showing saved samples that are missing required fields/photos for completion or email readiness.",
            "needsreview" => "Showing samples with explicit or threshold-based review flags.",
            "sent" => "Showing samples with a recorded QC summary email.",
            _ => null
        };

    private async Task<IReadOnlyList<RoomCountBreakdownRowViewModel>> BuildRoomCountBreakdownRowsAsync(int roomId, CancellationToken cancellationToken)
    {
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.FruitProfile)
            .Where(x => x.RoomId == roomId)
            .OrderByDescending(x => x.ReceivedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var receiptIds = receipts.Select(x => x.Id).ToList();
        var samplesByReceipt = (await QuerySamples()
                .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                .Select(x => new { x.ReceiptId, SampleType = x.SampleType.Name })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(
                x => x.Key,
                x => string.Join(", ", x.Select(y => y.SampleType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y)));
        var depletionByReceipt = await dbContext.RoomDepletions.AsNoTracking()
            .Where(x => receiptIds.Contains(x.ReceiptId) && !x.IsVoided)
            .GroupBy(x => x.ReceiptId)
            .Select(x => new { ReceiptId = x.Key, Bins = x.Sum(y => y.BinCountDepleted) })
            .ToDictionaryAsync(x => x.ReceiptId, x => x.Bins, cancellationToken);
        var latestAdjustmentByReceipt = (await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First());
        var roomCorrectionCutoffs = await BuildCurrentBalanceCorrectionCutoffsAsync(roomId, cancellationToken);
        var includedReceiptIds = receipts
            .Where(x => !x.IsDeleted)
            .Where(x => ReceiptStorageExclusionReason(x, samplesByReceipt.GetValueOrDefault(x.Id, ""), roomCorrectionCutoffs) is null)
            .Where(x => !IsSupersededByRoomCurrentBalanceCorrection(x, roomCorrectionCutoffs))
            .GroupBy(ReceiptDedupeKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.UpdatedAt).ThenByDescending(y => y.Id).First().Id)
            .ToHashSet();

        var rows = receipts.Select(receipt =>
        {
            var bins = CurrentReceiptBins(receipt, depletionByReceipt, latestAdjustmentByReceipt);
            var included = includedReceiptIds.Contains(receipt.Id);
            return new RoomCountBreakdownRowViewModel
            {
                SourceType = "Receipt",
                ReceiptId = receipt.Id,
                DisplayReceiptId = receipt.CompuTechReceiptId,
                SampleType = samplesByReceipt.GetValueOrDefault(receipt.Id) ?? receipt.ReceiptType,
                Grower = receipt.GrowerName,
                Lot = !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode,
                Variety = receipt.FruitProfile.VarietyCode,
                Bins = included ? bins : Math.Max(0, receipt.BinCount),
                Status = receipt.IsDeleted ? "Deleted" : receipt.ReceiptType,
                Date = receipt.ReceivedAt,
                IsIncluded = included,
                DecisionReason = ReceiptBreakdownDecision(receipt, samplesByReceipt.GetValueOrDefault(receipt.Id, ""), roomCorrectionCutoffs, includedReceiptIds)
            };
        }).ToList();

        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Receipt)
            .Where(x => x.RoomId == roomId)
            .OrderByDescending(x => x.AdjustmentAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var adjustmentCorrectionCutoffs = await BuildCurrentBalanceCorrectionCutoffsAsync(roomId, cancellationToken);
        var includedAdjustmentIds = ApplyLatestCurrentBalanceRows(adjustments
                .Where(IsAdjustmentOnlyCurrentStorageSource)
                .Where(x => !IsSupersededByRoomCurrentBalanceCorrection(x, adjustmentCorrectionCutoffs)))
            .Where(x => x.NewBinCount > 0)
            .Select(x => x.Id)
            .ToHashSet();
        rows.AddRange(adjustments.Select(adjustment =>
        {
            var included = includedAdjustmentIds.Contains(adjustment.Id);
            return new RoomCountBreakdownRowViewModel
            {
                SourceType = BreakdownSourceType(adjustment),
                ReceiptId = adjustment.ReceiptId,
                DisplayReceiptId = adjustment.Receipt?.CompuTechReceiptId,
                SampleType = adjustment.Receipt?.ReceiptType ?? adjustment.AdjustmentType,
                Grower = adjustment.GrowerName,
                Lot = adjustment.LotNumber,
                Variety = adjustment.VarietyCode ?? "",
                Bins = Math.Max(0, adjustment.NewBinCount),
                Status = string.IsNullOrWhiteSpace(adjustment.InventoryStatus)
                    ? adjustment.NewBinCount > 0 ? "Current" : "Zero"
                    : adjustment.InventoryStatus!,
                Date = adjustment.AdjustmentAt,
                IsIncluded = included,
                DecisionReason = AdjustmentBreakdownDecision(adjustment, included, adjustmentCorrectionCutoffs)
            };
        }));

        return rows
            .OrderByDescending(x => x.IsIncluded)
            .ThenByDescending(x => x.Date)
            .ThenBy(x => x.SourceType)
            .ToList();
    }

    private async Task<IReadOnlyList<RoomSummaryItemViewModel>> BuildRoomSummariesAsync(CancellationToken cancellationToken, int? roomId = null, RoomSummaryFilterForm? roomSummaryFilter = null)
    {
        var filter = NormalizeRoomSummaryFilter(roomSummaryFilter);
        var roomsQuery = dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).Include(x => x.SealedByUser).OrderBy(x => x.Warehouse.Code).ThenBy(x => x.Code);
        var rooms = await (roomId is null ? roomsQuery : roomsQuery.Where(x => x.Id == roomId)).ToListAsync(cancellationToken);
        if (roomId is null)
        {
            rooms = rooms
                .Where(room => RoomMatchesFacilityFilter(room, filter.Facility))
                .Where(room => RoomMatchesEbsLocationFilter(room, filter.EbsLocation))
                .ToList();
        }

        var lots = roomId is null
            ? await BuildRoomLotSummariesAsync(null, cancellationToken)
            : await BuildRoomLotSummariesAsync(roomId.Value, cancellationToken);
        var startingBinsByRoom = await BuildStartingSeasonBinsByRoomAsync(cancellationToken);
        var latestActivityByRoom = await BuildLatestRoomActivityByRoomAsync(cancellationToken);
        var colorLots = lots.Where(x => x.CurrentBins > 0).Select(ToDashboardInventorySnapshot).ToList();
        var colorMap = await ResolveDashboardVarietyColorsAsync(colorLots, cancellationToken);
        var dashboardQcTargets = colorLots.Select(x => x.QcIdentity).Where(x => x is not null).Select(x => x!).DistinctBy(x => x.LookupKey).ToList();
        var dashboardQcHeaders = roomId is null
            ? []
            : await LoadDashboardQcSampleHeadersAsync(dashboardQcTargets, cancellationToken);
        var dashboardQcHeadersByIdentity = IndexDashboardQcSampleHeaders(dashboardQcHeaders);
        var latestSamplesByLot = BuildDashboardLatestSampleByLot(dashboardQcTargets, dashboardQcHeaders, dashboardQcHeadersByIdentity);
        var roomQcSummaries = roomId is null
            ? new Dictionary<int, RoomQcSummary>()
            : await BuildDashboardRoomQcSummariesAsync(colorLots, dashboardQcTargets, dashboardQcHeaders, dashboardQcHeadersByIdentity, cancellationToken);
        var currentLotsByRoom = lots
            .Where(x => x.CurrentBins > 0)
            .GroupBy(x => x.RoomId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var sealStateAt = BusinessTime.UtcNow;
        var summaries = rooms.Select(room =>
        {
            var roomLots = currentLotsByRoom.GetValueOrDefault(room.Id, []);
            var flags = roomLots.SelectMany(x => x.ReviewFlags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var currentBins = roomLots.Sum(x => x.CurrentBins);
            var roomColorLots = colorLots.Where(x => x.RoomId == room.Id).ToList();
            var colorCurrentBins = roomColorLots.Sum(x => x.CurrentBins);
            var qcSummary = roomQcSummaries.GetValueOrDefault(room.Id) ?? RoomQcSummary.Empty(colorCurrentBins);
            var representedBins = roomColorLots
                .Where(x => latestSamplesByLot.ContainsKey(x.QcIdentityKey))
                .Sum(x => x.CurrentBins);
            var latestSampleDate = roomColorLots
                .Select(x => latestSamplesByLot.GetValueOrDefault(x.QcIdentityKey)?.SampleTakenAt)
                .Where(x => x is not null)
                .DefaultIfEmpty()
                .Max();
            var organicBins = roomColorLots.Where(x => x.IsOrganic == true).Sum(x => x.CurrentBins);
            var conventionalBins = roomColorLots.Where(x => x.IsOrganic == false).Sum(x => x.CurrentBins);
            var unknownOrganicBins = roomColorLots.Where(x => x.IsOrganic is null).Sum(x => x.CurrentBins);
            var status = roomLots.Count == 0 ? "Empty" : flags.Count > 0 ? "Needs Review" : "Active";
            var facility = FacilityCode(room.Warehouse.Code, room.Warehouse.Name);
            var weakestLot = FindWeakestLot(roomLots);
            var sourceRoomCodes = roomLots.Select(x => x.RoomCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var displayRoomCode = room.CropQcRoomName ?? room.DisplayName ?? (sourceRoomCodes.Count == 1 ? sourceRoomCodes[0] : room.Code);
            var latestActivity = latestActivityByRoom.TryGetValue(room.Id, out var activity)
                ? activity
                : roomLots.Select(x => x.LastSampleDate).Where(x => x is not null).DefaultIfEmpty().Max();
            return new RoomSummaryItemViewModel
            {
                RoomId = room.Id,
                Warehouse = room.Warehouse.Code,
                Facility = facility,
                LocationGroup = RoomLocationGroup(room),
                RoomCode = displayRoomCode,
                RoomName = room.DisplayName ?? room.Name,
                CompuTechCode = room.CompuTechRoomCode ?? "",
                RoomCapacityBins = room.CapacityBins,
                Status = status,
                IsSealed = RoomSealState.IsEffectivelySealed(room, sealStateAt),
                IsSealScheduled = RoomSealState.IsScheduled(room, sealStateAt),
                SealedAt = room.SealedAt,
                SealRecordedAt = room.SealRecordedAt,
                SealedBy = room.SealedByUser?.DisplayName,
                CurrentLotsCount = roomColorLots.Count,
                CurrentBinsCount = colorCurrentBins == 0 ? null : colorCurrentBins,
                VarietyColorSegments = BuildRoomVarietyColorSegments(roomColorLots, colorMap),
                OrganicBins = organicBins,
                ConventionalBins = conventionalBins,
                UnknownOrganicStatusBins = unknownOrganicBins,
                OrganicPercent = colorCurrentBins <= 0 ? 0m : decimal.Round(organicBins / (decimal)colorCurrentBins * 100m, 1),
                IsMajorityOrganic = colorCurrentBins > 0 && organicBins / (decimal)colorCurrentBins > 0.51m,
                StartingSeasonBins = startingBinsByRoom.GetValueOrDefault(room.Id),
                NetChangeBins = currentBins - startingBinsByRoom.GetValueOrDefault(room.Id),
                VarietyStatusSummary = BuildDashboardVarietySummary(roomColorLots),
                LastActivityAt = latestActivity,
                LotSummary = roomColorLots.Count == 0 ? "Empty" : string.Join(", ", roomColorLots.Take(4).Select(x => $"{x.Grower} {x.Lot} {x.VarietyName}")),
                QcRepresentedBins = representedBins,
                QcMissingBins = Math.Max(0, colorCurrentBins - representedBins),
                QcCoveragePercent = colorCurrentBins <= 0 ? 0m : decimal.Round(representedBins / (decimal)colorCurrentBins * 100m, 1),
                AveragePressureLbs = qcSummary.ReceivingPressureLbs,
                ReceivingPressureRepresentedBins = qcSummary.ReceivingPressureRepresentedBins,
                ReceivingPressureMissingBins = qcSummary.ReceivingPressureMissingBins,
                LatestPressureLbs = qcSummary.LatestPressureLbs,
                LatestPressureDate = qcSummary.LatestPressureDate,
                LatestPressureRepresentedBins = qcSummary.LatestPressureRepresentedBins,
                LatestPressureMissingBins = qcSummary.LatestPressureMissingBins,
                PressureStdDevLbs = qcSummary.LatestPressureStandardDeviationLbs,
                PressureStandardDeviationRepresentedBins = qcSummary.PressureStandardDeviationRepresentedBins,
                PressureReadingCount = qcSummary.PressureReadingCount,
                MonthOverMonthPressureChangeLbs = qcSummary.PressureChange30DayLbs,
                PressureChangeRepresentedBins = qcSummary.PressureChangeRepresentedBins,
                PressureChangeMissingBins = qcSummary.PressureChangeMissingBins,
                AverageStarch = qcSummary.ReceivingStarch,
                ReceivingStarchRepresentedBins = qcSummary.ReceivingStarchRepresentedBins,
                ReceivingStarchMissingBins = qcSummary.ReceivingStarchMissingBins,
                DefectSummary = SummarizeLotDefects(roomLots),
                LastSampleDate = latestSampleDate,
                SampleCount = roomLots.Sum(x => x.SampleCount),
                EnteredFruitCount = roomLots.Sum(x => x.EnteredFruitCount),
                ReviewFlags = flags,
                WeakestLotLabel = weakestLot?.Label,
                WeakestLotReason = weakestLot?.Reason,
                WeakestLotReceiptId = weakestLot?.ReceiptId
            };
        }).ToList();

        return roomId is not null ? summaries : ApplyRoomStatusFilter(summaries, filter.RoomStatus);
    }

    private async Task<IReadOnlyList<RoomSummaryItemViewModel>> BuildDashboardRoomSummariesAsync(
        IReadOnlyList<DashboardInventorySnapshot> currentLots,
        RoomSummaryFilterForm roomSummaryFilter,
        CancellationToken cancellationToken)
    {
        var occupiedLots = currentLots.Where(x => x.CurrentBins > 0).ToList();
        if (occupiedLots.Count == 0)
        {
            return [];
        }

        var occupiedRoomIds = occupiedLots.Select(x => x.RoomId).Distinct().ToList();
        var rooms = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.SealedByUser)
            .Where(x => x.IsActive && occupiedRoomIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        rooms = rooms
            .Where(room => RoomMatchesFacilityFilter(room, roomSummaryFilter.Facility))
            .Where(room => RoomMatchesEbsLocationFilter(room, roomSummaryFilter.EbsLocation))
            .ToList();

        var includedRoomIds = rooms.Select(x => x.Id).ToHashSet();
        occupiedLots = occupiedLots.Where(x => includedRoomIds.Contains(x.RoomId)).ToList();
        var dashboardQcTargets = occupiedLots.Select(x => x.QcIdentity).Where(x => x is not null).Select(x => x!).DistinctBy(x => x.LookupKey).ToList();
        var dashboardQcHeaders = await LoadDashboardQcSampleHeadersAsync(dashboardQcTargets, cancellationToken);
        var dashboardQcHeadersByIdentity = IndexDashboardQcSampleHeaders(dashboardQcHeaders);
        var latestSamplesByLot = BuildDashboardLatestSampleByLot(dashboardQcTargets, dashboardQcHeaders, dashboardQcHeadersByIdentity);
        var roomQcSummaries = await BuildDashboardRoomQcSummariesAsync(occupiedLots, dashboardQcTargets, dashboardQcHeaders, dashboardQcHeadersByIdentity, cancellationToken);
        var colorMap = await ResolveDashboardVarietyColorsAsync(occupiedLots, cancellationToken);
        var today = BusinessTime.NowPacific;
        var sealStateAt = BusinessTime.UtcNow;

        return rooms.Select(room =>
            {
                var roomLots = occupiedLots.Where(x => x.RoomId == room.Id).ToList();
                var currentBins = roomLots.Sum(x => x.CurrentBins);
                var representedBins = roomLots
                    .Where(x => latestSamplesByLot.ContainsKey(x.QcIdentityKey))
                    .Sum(x => x.CurrentBins);
                var latestSampleDate = roomLots
                    .Select(x => latestSamplesByLot.GetValueOrDefault(x.QcIdentityKey)?.SampleTakenAt)
                    .Where(x => x is not null)
                    .DefaultIfEmpty()
                    .Max();
                var staleLots = roomLots
                    .Where(x => latestSamplesByLot.TryGetValue(x.QcIdentityKey, out var sample)
                        && (today - sample.SampleTakenAt).TotalDays >= 14)
                    .ToList();
                var statusLots = roomLots
                    .Where(x => !string.IsNullOrWhiteSpace(x.InventoryStatus))
                    .ToList();
                var missingBins = Math.Max(0, currentBins - representedBins);
                var coverage = currentBins <= 0 ? 0m : decimal.Round(representedBins / (decimal)currentBins * 100m, 1);
                var qcSummary = roomQcSummaries.GetValueOrDefault(room.Id) ?? RoomQcSummary.Empty(currentBins);
                var attention = BuildDashboardRoomAttention(roomLots, qcSummary, representedBins, missingBins, coverage, staleLots, statusLots, latestSampleDate, today);
                var varietySegments = BuildRoomVarietyColorSegments(roomLots, colorMap);
                var organicBins = roomLots.Where(x => x.IsOrganic == true).Sum(x => x.CurrentBins);
                var conventionalBins = roomLots.Where(x => x.IsOrganic == false).Sum(x => x.CurrentBins);
                var unknownOrganicBins = roomLots.Where(x => x.IsOrganic is null).Sum(x => x.CurrentBins);
                var organicPercent = currentBins <= 0 ? 0m : decimal.Round(organicBins / (decimal)currentBins * 100m, 1);
                var displayRoomCode = room.CropQcRoomName ?? room.DisplayName ?? room.Code;

                return new RoomSummaryItemViewModel
                {
                    RoomId = room.Id,
                    Warehouse = room.Warehouse.Code,
                    Facility = FacilityCode(room.Warehouse.Code, room.Warehouse.Name),
                    LocationGroup = RoomLocationGroup(room),
                    RoomCode = displayRoomCode,
                    RoomName = room.DisplayName ?? room.Name,
                    CompuTechCode = room.CompuTechRoomCode ?? "",
                    RoomCapacityBins = room.CapacityBins,
                    Status = attention.Category,
                    IsSealed = RoomSealState.IsEffectivelySealed(room, sealStateAt),
                    IsSealScheduled = RoomSealState.IsScheduled(room, sealStateAt),
                    SealedAt = room.SealedAt,
                    SealRecordedAt = room.SealRecordedAt,
                    SealedBy = room.SealedByUser?.DisplayName,
                    AttentionCategory = attention.Category,
                    AttentionSort = attention.Sort,
                    RankingReason = attention.Reason,
                    QcRepresentedBins = representedBins,
                    QcMissingBins = missingBins,
                    QcCoveragePercent = coverage,
                    MajorWeakLotIndicator = attention.Indicator,
                    CurrentLotsCount = roomLots.Count,
                    CurrentBinsCount = currentBins,
                    VarietyColorSegments = varietySegments,
                    OrganicBins = organicBins,
                    ConventionalBins = conventionalBins,
                    UnknownOrganicStatusBins = unknownOrganicBins,
                    OrganicPercent = organicPercent,
                    IsMajorityOrganic = currentBins > 0 && organicBins / (decimal)currentBins > 0.51m,
                    LastSampleDate = latestSampleDate,
                    LatestQcSource = latestSampleDate is null ? "" : "Latest lot QC sample",
                    LotSummary = string.Join(", ", roomLots.Take(4).Select(x => $"{x.Grower} {x.Lot} {x.Variety}")),
                    VarietyStatusSummary = BuildDashboardVarietySummary(roomLots),
                    LastActivityAt = latestSampleDate ?? roomLots.Select(x => x.ReceiptDate).Where(x => x is not null).DefaultIfEmpty().Max(),
                    AverageStarch = qcSummary.ReceivingStarch,
                    ReceivingStarchRepresentedBins = qcSummary.ReceivingStarchRepresentedBins,
                    ReceivingStarchMissingBins = qcSummary.ReceivingStarchMissingBins,
                    AveragePressureLbs = qcSummary.ReceivingPressureLbs,
                    ReceivingPressureRepresentedBins = qcSummary.ReceivingPressureRepresentedBins,
                    ReceivingPressureMissingBins = qcSummary.ReceivingPressureMissingBins,
                    LatestPressureLbs = qcSummary.LatestPressureLbs,
                    LatestPressureDate = qcSummary.LatestPressureDate,
                    LatestPressureRepresentedBins = qcSummary.LatestPressureRepresentedBins,
                    LatestPressureMissingBins = qcSummary.LatestPressureMissingBins,
                    MonthOverMonthPressureChangeLbs = qcSummary.PressureChange30DayLbs,
                    PressureChangeRepresentedBins = qcSummary.PressureChangeRepresentedBins,
                    PressureChangeMissingBins = qcSummary.PressureChangeMissingBins,
                    PressureStdDevLbs = qcSummary.LatestPressureStandardDeviationLbs,
                    PressureStandardDeviationRepresentedBins = qcSummary.PressureStandardDeviationRepresentedBins,
                    PressureReadingCount = qcSummary.PressureReadingCount,
                    ReviewFlags = attention.Flag is null ? [] : [attention.Flag],
                    WeakestLotLabel = attention.WeakestLotLabel,
                    WeakestLotReason = attention.Indicator
                };
            })
            .Where(x => (x.CurrentBinsCount ?? 0) > 0)
            .OrderBy(x => x.AttentionSort)
            .ThenByDescending(x => x.QcMissingBins == 0 ? 0m : x.QcMissingBins / (decimal)(x.CurrentBinsCount ?? 1))
            .ThenBy(x => x.LastSampleDate ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.RoomCode)
            .ToList();
    }

    private async Task<IReadOnlyList<DashboardInventorySnapshot>> BuildDashboardCurrentInventorySnapshotsAsync(int? roomId, CancellationToken cancellationToken)
    {
        var ledgerRows = await RoomInventoryLedger.GetSnapshotsAsync(
            null,
            roomId is null ? null : [roomId.Value],
            cancellationToken);
        var conflictingGroups = ledgerRows
            .GroupBy(
                x => LedgerLotKey(x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId),
                StringComparer.OrdinalIgnoreCase)
            .Where(x => !CanReconcileDashboardLedgerSnapshots(x))
            .ToList();
        if (conflictingGroups.Count > 0)
        {
            var sample = conflictingGroups
                .Take(5)
                .Select(x => $"{x.Key} [{DashboardLedgerIdentitySummary(x)}]");
            var suffix = conflictingGroups.Count > 5 ? $"; plus {conflictingGroups.Count - 5} more" : "";
            throw new InvalidOperationException(
                $"Room inventory ledger contains {conflictingGroups.Count} conflicting dashboard key(s): {string.Join("; ", sample)}{suffix}. "
                + "The conflicting quantities were not selected or discarded.");
        }

        return ledgerRows
            .Where(x => x.CurrentBins > 0)
            .Select(x =>
            {
                var variety = VarietyColorService.NormalizeIdentity(x.VarietyName, x.Variety);
                return new DashboardInventorySnapshot(
                    x.RoomId,
                    x.CropYear,
                    x.FruitProfileId,
                    x.Facility,
                    FacilityCode(x.Facility, x.Facility),
                    x.LocationGroup,
                    x.Room,
                    x.Grower,
                    x.GrowerNumber ?? "",
                    x.GrowerLotId,
                    x.Lot,
                    x.Variety,
                    variety.Key,
                    variety.Name,
                    x.ProductionType,
                    x.IsOrganic,
                    x.InventoryStatus,
                    x.CurrentBins,
                    x.LastTransactionAt);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<DashboardQcSampleHeader>> LoadDashboardQcSampleHeadersAsync(
        IReadOnlyList<CanonicalQcFruitIdentity> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        var candidateQuery = CanonicalQcFruitIdentity.FilterReceiptSamples(
                dbContext.QcSamples.AsNoTracking(),
                targets);
        return await CanonicalQcFruitIdentity.OrderCandidates(
                candidateQuery,
                dbContext.Database.ProviderName)
            .Select(x => new DashboardQcSampleHeader(
                x.Id,
                x.Receipt!.GrowerLotId,
                x.Receipt.GrowerNumber ?? x.Receipt.LotCode,
                x.Receipt.LotCode,
                x.Receipt.FruitProfileId,
                x.Receipt.CropYear,
                x.Receipt.FruitProfile.VarietyCode,
                x.Receipt.FruitProfile.ProductionType,
                x.Receipt.FruitProfile.IsOrganic,
                x.SampleTakenAt,
                x.SampleType.Name))
            .Take(CanonicalQcFruitIdentity.CandidateLimit(targets.Count))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, DashboardSampleMarker> BuildDashboardLatestSampleByLot(
        IReadOnlyList<CanonicalQcFruitIdentity> targets,
        IReadOnlyList<DashboardQcSampleHeader> sampleRows,
        IReadOnlyDictionary<string, IReadOnlyList<DashboardQcSampleHeader>> sampleRowsByIdentity)
    {
        var result = new Dictionary<string, DashboardSampleMarker>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var candidates = sampleRowsByIdentity.GetValueOrDefault(target.LookupKey) ?? sampleRows;
            var latest = CanonicalQcFruitIdentity.ResolveLatestUnambiguous(
                target,
                candidates,
                x => x.Identity,
                x => x.SampleTakenAt,
                x => x.Id);
            if (latest is not null)
            {
                result[target.LookupKey] = new DashboardSampleMarker(latest.SampleTakenAt, latest.SampleType);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DashboardQcSampleHeader>> IndexDashboardQcSampleHeaders(
        IReadOnlyList<DashboardQcSampleHeader> headers) =>
        headers
            .Where(x => x.Identity is not null)
            .GroupBy(x => x.Identity!.LookupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<DashboardQcSampleHeader>)x.ToList(), StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<int, RoomQcSummary>> BuildDashboardRoomQcSummariesAsync(
        IReadOnlyList<DashboardInventorySnapshot> currentLots,
        IReadOnlyList<CanonicalQcFruitIdentity> targets,
        IReadOnlyList<DashboardQcSampleHeader> candidateHeaders,
        IReadOnlyDictionary<string, IReadOnlyList<DashboardQcSampleHeader>> candidateHeadersByIdentity,
        CancellationToken cancellationToken)
    {
        var occupiedLots = currentLots.Where(x => x.CurrentBins > 0).ToList();
        if (targets.Count == 0)
        {
            return new Dictionary<int, RoomQcSummary>();
        }

        var sampleTypes = new[] { "Receiving Sample", "Door Sample", "Lot Sample" };
        var sampleHeaders = candidateHeaders.Where(x => sampleTypes.Contains(x.SampleType)).ToList();
        var sampleIds = sampleHeaders.Select(x => x.Id).ToList();
        var aggregates = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(row => sampleIds.Contains(row.QcSampleId))
            .GroupBy(row => row.QcSampleId)
            .Select(group => new DashboardQcSampleAggregate(
                group.Key,
                group.Average(row => row.Pressure1Lbs != null && row.Pressure2Lbs != null
                    ? (row.Pressure1Lbs + row.Pressure2Lbs) / 2m
                    : row.Pressure1Lbs ?? row.Pressure2Lbs),
                group.Average(row => row.StarchScaleValue == null ? (decimal?)null : row.StarchScaleValue.Value)))
            .ToListAsync(cancellationToken);
        var aggregatesBySample = aggregates.ToDictionary(x => x.SampleId);
        var samplesByLot = new Dictionary<string, List<DashboardQcSample>>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var candidates = candidateHeadersByIdentity.GetValueOrDefault(target.LookupKey) ?? sampleHeaders;
            var matchingHeaders = CanonicalQcFruitIdentity.ResolveUnambiguous(target, candidates, x => x.Identity);
            samplesByLot[target.LookupKey] = matchingHeaders
                .Select(header =>
                {
                    aggregatesBySample.TryGetValue(header.Id, out var aggregate);
                    return new DashboardQcSample(
                        header.Id,
                        header.SampleTakenAt,
                        header.SampleType,
                        RoundOrNull(aggregate?.AveragePressureLbs),
                        RoundOrNull(aggregate?.AverageStarch));
                })
                .ToList();
        }

        var latestPressureSampleIds = samplesByLot.Values
            .Select(samples => samples
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.SampleId)
                .FirstOrDefault(x => x.AveragePressureLbs is not null)?.SampleId)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var latestPressureRows = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(row => latestPressureSampleIds.Contains(row.QcSampleId)
                && (row.Pressure1Lbs != null || row.Pressure2Lbs != null))
            .Select(row => new
            {
                row.QcSampleId,
                Pressure = row.Pressure1Lbs != null && row.Pressure2Lbs != null
                    ? (row.Pressure1Lbs + row.Pressure2Lbs) / 2m
                    : row.Pressure1Lbs ?? row.Pressure2Lbs
            })
            .ToListAsync(cancellationToken);
        var latestPressureBySample = latestPressureRows
            .GroupBy(x => x.QcSampleId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<decimal>)x.Where(y => y.Pressure is not null).Select(y => y.Pressure!.Value).ToList());

        return occupiedLots
            .GroupBy(x => x.RoomId)
            .ToDictionary(
                x => x.Key,
                x => BuildRoomQcSummary(x.ToList(), samplesByLot, latestPressureBySample));
    }

    private static RoomQcSummary BuildRoomQcSummary(
        IReadOnlyList<DashboardInventorySnapshot> roomLots,
        IReadOnlyDictionary<string, List<DashboardQcSample>> samplesByLot,
        IReadOnlyDictionary<long, IReadOnlyList<decimal>> latestPressureBySample)
    {
        var totalBins = roomLots.Sum(x => x.CurrentBins);
        var receivingStarch = new List<(decimal Value, decimal Weight)>();
        var receivingPressure = new List<(decimal Value, decimal Weight)>();
        var latestPressure = new List<(decimal Value, decimal Weight)>();
        var pressureChange = new List<(decimal Value, decimal Weight)>();
        var weightedPressureReadings = new List<(decimal Value, decimal Weight)>();
        var receivingStarchBins = 0;
        var receivingPressureBins = 0;
        var latestPressureBins = 0;
        var pressureChangeBins = 0;
        var stdDevBins = 0;
        var pressureReadingCount = 0;
        DateTimeOffset? latestPressureDate = null;

        foreach (var lot in roomLots)
        {
            if (!samplesByLot.TryGetValue(lot.QcIdentityKey, out var samples))
            {
                continue;
            }

            samples = samples
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.SampleId)
                .ToList();

            var receivingSample = samples
                .Where(x => x.SampleType.Equals("Receiving Sample", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (receivingSample?.AverageStarch is decimal starch)
            {
                receivingStarch.Add((starch, lot.CurrentBins));
                receivingStarchBins += lot.CurrentBins;
            }

            if (receivingSample?.AveragePressureLbs is decimal receivingPressureValue)
            {
                receivingPressure.Add((receivingPressureValue, lot.CurrentBins));
                receivingPressureBins += lot.CurrentBins;
            }

            var latestPressureSample = samples.FirstOrDefault(x => x.AveragePressureLbs is not null);
            if (latestPressureSample?.AveragePressureLbs is decimal latestPressureValue)
            {
                latestPressure.Add((latestPressureValue, lot.CurrentBins));
                latestPressureBins += lot.CurrentBins;
                latestPressureDate = latestPressureDate is null || latestPressureSample.SampleTakenAt > latestPressureDate
                    ? latestPressureSample.SampleTakenAt
                    : latestPressureDate;

                var readings = latestPressureBySample.GetValueOrDefault(latestPressureSample.SampleId) ?? [];
                if (readings.Count >= 2)
                {
                    var perReadingWeight = lot.CurrentBins / (decimal)readings.Count;
                    weightedPressureReadings.AddRange(readings.Select(x => (x, perReadingWeight)));
                    pressureReadingCount += readings.Count;
                    stdDevBins += lot.CurrentBins;
                }

                var priorSample = samples
                    .Where(x => x.AveragePressureLbs is not null)
                    .Select(x => new { Sample = x, Days = (latestPressureSample.SampleTakenAt - x.SampleTakenAt).TotalDays })
                    .Where(x => x.Days >= 21 && x.Days <= 45)
                    .OrderBy(x => Math.Abs(x.Days - 30))
                    .ThenByDescending(x => x.Sample.SampleTakenAt)
                    .FirstOrDefault();
                if (priorSample is not null)
                {
                    var normalizedChange = WeightedStatistics.NormalizeChangeToThirtyDays(
                        latestPressureValue,
                        priorSample.Sample.AveragePressureLbs!.Value,
                        priorSample.Days);
                    pressureChange.Add((normalizedChange, lot.CurrentBins));
                    pressureChangeBins += lot.CurrentBins;
                }
            }
        }

        return new RoomQcSummary(
            totalBins,
            RoundOrNull(WeightedStatistics.WeightedMean(receivingStarch)),
            receivingStarchBins,
            Math.Max(0, totalBins - receivingStarchBins),
            RoundOrNull(WeightedStatistics.WeightedMean(receivingPressure)),
            receivingPressureBins,
            Math.Max(0, totalBins - receivingPressureBins),
            RoundOrNull(WeightedStatistics.WeightedMean(latestPressure)),
            latestPressureDate,
            latestPressureBins,
            Math.Max(0, totalBins - latestPressureBins),
            RoundOrNull(WeightedStatistics.WeightedMean(pressureChange)),
            pressureChangeBins,
            Math.Max(0, totalBins - pressureChangeBins),
            RoundOrNull(WeightedStatistics.WeightedSampleStandardDeviation(weightedPressureReadings)),
            stdDevBins,
            pressureReadingCount);
    }

    private DashboardRoomAttention BuildDashboardRoomAttention(
        IReadOnlyList<DashboardInventorySnapshot> roomLots,
        RoomQcSummary qcSummary,
        int representedBins,
        int missingBins,
        decimal coverage,
        IReadOnlyList<DashboardInventorySnapshot> staleLots,
        IReadOnlyList<DashboardInventorySnapshot> statusLots,
        DateTimeOffset? latestSampleDate,
        DateTimeOffset now)
    {
        var currentBins = roomLots.Sum(x => x.CurrentBins);
        if (statusLots.Count > 0)
        {
            var bins = statusLots.Sum(x => x.CurrentBins);
            return new("Needs attention", 1, $"{bins} current bins have inventory status notes", statusLots[0].InventoryStatus, "Inventory status note", $"{statusLots[0].Grower} {statusLots[0].Lot}");
        }

        if (qcSummary.PressureChange30DayLbs is decimal change && change < 0)
        {
            var drop = Math.Abs(change);
            var configuredDropThreshold = ReadDashboardThreshold("DashboardReview:PressureDropLbs");
            if (configuredDropThreshold is not null && drop > configuredDropThreshold.Value)
            {
                return new("Needs attention", 1, $"Pressure declined {drop:0.##} lb over 30 days", "Pressure decline", "Pressure decline", null);
            }

            return new("Watch", 3, $"Pressure declined {drop:0.##} lb over 30 days", "Pressure decline", "Pressure decline", null);
        }

        if (qcSummary.LatestPressureStandardDeviationLbs is decimal stdDev
            && ReadDashboardThreshold("DashboardReview:HighPressureVarianceLbs") is decimal highVariance
            && stdDev > highVariance)
        {
            return new("Watch", 3, $"Latest pressure SD {stdDev:0.##} lb exceeds configured variance threshold {highVariance:0.##} lb", "High pressure variability", "High pressure variability", null);
        }

        if (qcSummary.ReceivingStarch is decimal receivingStarch
            && ReadDashboardThreshold("DashboardReview:HighStarch") is decimal highStarch
            && receivingStarch > highStarch)
        {
            return new("Watch", 3, $"Receiving starch {receivingStarch:0.##} exceeds configured threshold {highStarch:0.##}", "Advanced receiving starch", "Advanced receiving starch", null);
        }

        if (qcSummary.LatestPressureLbs is decimal latestPressure
            && ReadDashboardThreshold("DashboardReview:LowPressureLbs") is decimal lowPressure
            && latestPressure < lowPressure)
        {
            return new("Watch", 3, $"Latest pressure {latestPressure:0.##} lb is below configured threshold {lowPressure:0.##} lb", "Low latest pressure", "Low latest pressure", null);
        }

        if (missingBins > 0)
        {
            var percent = currentBins <= 0 ? 0m : decimal.Round(missingBins / (decimal)currentBins * 100m, 1);
            var category = representedBins == 0 ? "Needs review" : "Watch";
            return new(category, representedBins == 0 ? 2 : 3, $"QC data missing for {missingBins} bins ({percent}% of current bins)", "Incomplete QC coverage", "Missing current QC data", null);
        }

        if (staleLots.Count > 0)
        {
            var oldest = latestSampleDate is null ? 0 : (int)Math.Floor((now - latestSampleDate.Value).TotalDays);
            return new("Watch", 3, $"Latest QC data is {oldest} days old", "Stale QC data", "Stale QC data", null);
        }

        return new("Stable", 4, $"QC data covers {representedBins} of {currentBins} bins ({coverage}%)", "No current concerns identified", null, null);
    }

    private decimal? ReadDashboardThreshold(string key) =>
        decimal.TryParse(configuration[key], out var threshold) ? threshold : null;

    private static string BuildDashboardVarietySummary(IReadOnlyList<DashboardInventorySnapshot> roomLots) =>
        string.Join(", ", roomLots
            .GroupBy(x => string.IsNullOrWhiteSpace(x.VarietyName) ? x.Variety : x.VarietyName)
            .OrderByDescending(x => x.Sum(y => y.CurrentBins))
            .ThenBy(x => x.Key)
            .Take(3)
            .Select(x => $"{x.Key}: {x.Sum(y => y.CurrentBins)} bins"));

    private async Task<IReadOnlyDictionary<string, VarietyColorResolved>> ResolveDashboardVarietyColorsAsync(
        IReadOnlyList<DashboardInventorySnapshot> lots,
        CancellationToken cancellationToken)
    {
        var keys = lots.Select(x => x.VarietyKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<string, VarietyColorResolved>(StringComparer.OrdinalIgnoreCase);
        }

        var resolver = varietyColorService ?? new VarietyColorService(dbContext);
        return await resolver.GetResolvedColorsReadOnlyAsync(keys, cancellationToken);
    }

    private async Task<IReadOnlyList<SampleListItemViewModel>> BuildTodayDashboardSamplesAsync(
        UtcDayRange todayRange,
        CancellationToken cancellationToken)
    {
        var samples = await dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.ReceiptId != null
                && x.SampleTakenAt >= todayRange.Start
                && x.SampleTakenAt < todayRange.End)
            .OrderByDescending(x => x.SampleTakenAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new DashboardSampleSummaryRow(
                x.Id,
                x.ReceiptId!.Value,
                x.Receipt.CropYear,
                x.Receipt.CompuTechReceiptId,
                x.SampleSequenceNumber,
                x.Receipt.Warehouse.Code,
                x.SampleType.Name,
                x.Receipt.FruitProfile.FruitType,
                x.Status,
                x.StarchStatus,
                x.PhotoStatus,
                x.EmailStatus,
                x.TakenByUser == null ? null : x.TakenByUser.DisplayName,
                x.SampleTakenAt,
                x.ActualSampleSize,
                x.IsDeleted))
            .ToListAsync(cancellationToken);

        if (samples.Count == 0)
        {
            return [];
        }

        var sampleIds = samples.Select(x => x.Id).ToList();
        var receiptIds = samples.Select(x => x.ReceiptId).Distinct().ToList();
        var fruitAggregates = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => sampleIds.Contains(x.QcSampleId))
            .GroupBy(x => x.QcSampleId)
            .Select(group => new DashboardSampleFruitAggregate(
                group.Key,
                group.Count(x => x.IsCompleted),
                group.Count(x => x.IsCompleted && (x.Pressure1Lbs == null || x.Pressure2Lbs == null || x.WeightGrams == null || x.GradeId == null)),
                group.Count(x => x.IsCompleted && (x.Pressure1Lbs == null || x.Pressure2Lbs == null)),
                group.Count(x => x.IsCompleted && x.WeightGrams == null),
                group.Count(x => x.IsCompleted && x.GradeId == null),
                group.Count(x => x.IsCompleted && x.StarchScaleValueId == null),
                group.Count(x => x.IsCompleted && x.Defects.Any()),
                group.Average(x => x.Pressure1Lbs != null && x.Pressure2Lbs != null
                    ? (x.Pressure1Lbs + x.Pressure2Lbs) / 2m
                    : x.Pressure1Lbs ?? x.Pressure2Lbs),
                group.Min(x => x.Pressure1Lbs != null && x.Pressure2Lbs != null
                    ? (x.Pressure1Lbs + x.Pressure2Lbs) / 2m
                    : x.Pressure1Lbs ?? x.Pressure2Lbs),
                group.Max(x => x.Pressure1Lbs != null && x.Pressure2Lbs != null
                    ? (x.Pressure1Lbs + x.Pressure2Lbs) / 2m
                    : x.Pressure1Lbs ?? x.Pressure2Lbs),
                group.Average(x => x.StarchScaleValue == null ? (decimal?)null : x.StarchScaleValue.Value)))
            .ToListAsync(cancellationToken);
        var aggregateBySample = fruitAggregates.ToDictionary(x => x.SampleId);

        var photos = await dbContext.QcPhotos.AsNoTracking()
            .Where(x => !x.IsDeleted
                && (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value)
                    || x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value)))
            .Select(x => new { x.ReceiptId, x.QcSampleId, x.PhotoType })
            .ToListAsync(cancellationToken);
        var receiptPhotosByReceipt = photos
            .Where(x => x.ReceiptId is not null)
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(y => y.PhotoType).ToList());
        var samplePhotosBySample = photos
            .Where(x => x.QcSampleId is not null)
            .GroupBy(x => x.QcSampleId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(y => y.PhotoType).ToList());

        var sentLogs = await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .Where(x => x.QcSampleId != null
                && sampleIds.Contains(x.QcSampleId.Value)
                && x.Status == "Sent"
                && x.SentAt != null)
            .Select(x => new
            {
                SampleId = x.QcSampleId!.Value,
                x.SentAt,
                SentBy = x.SentByUser == null ? x.FromAddress : x.SentByUser.DisplayName,
                x.Id
            })
            .ToListAsync(cancellationToken);
        var sentBySample = sentLogs
            .GroupBy(x => x.SampleId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var latest = x.OrderByDescending(y => y.SentAt).ThenByDescending(y => y.Id).First();
                    return new QcSummaryEmailSentInfo(latest.SentAt, latest.SentBy);
                });

        return samples.Select(sample =>
            {
                var aggregate = aggregateBySample.GetValueOrDefault(sample.Id) ?? DashboardSampleFruitAggregate.Empty(sample.Id);
                receiptPhotosByReceipt.TryGetValue(sample.ReceiptId, out var receiptPhotoTypes);
                receiptPhotoTypes ??= [];
                samplePhotosBySample.TryGetValue(sample.Id, out var samplePhotoTypes);
                samplePhotoTypes ??= [];
                sentBySample.TryGetValue(sample.Id, out var sentInfo);
                var readiness = BuildDashboardReadiness(sample.SampleType, sample.FruitType, aggregate, receiptPhotoTypes, samplePhotoTypes);

                return new SampleListItemViewModel
                {
                    Id = sample.Id,
                    ReceiptId = sample.ReceiptId,
                    CropYear = sample.CropYear,
                    ReceiptIdText = sample.ReceiptIdText,
                    DisplayReceiptId = sample.SampleSequenceNumber <= 1 ? sample.ReceiptIdText : $"{sample.ReceiptIdText}({sample.SampleSequenceNumber})",
                    Warehouse = sample.Warehouse,
                    SampleType = sample.SampleType,
                    Status = sample.Status,
                    StarchStatus = sample.StarchStatus,
                    PhotoStatus = sample.PhotoStatus,
                    EmailStatus = sample.EmailStatus,
                    EmailSentAt = sentInfo?.SentAt,
                    EmailSentBy = sentInfo?.SentBy,
                    TakenBy = sample.TakenBy,
                    SampleTakenAt = sample.SampleTakenAt,
                    ActualSampleSize = sample.ActualSampleSize,
                    IsReady = readiness.IsReady,
                    MissingItems = readiness.MissingItems,
                    ReviewReasons = BuildDashboardReviewReasons(sample.Status, aggregate),
                    Checklist = readiness.Checklist,
                    CompletedFruitCount = readiness.CompletedFruitCount,
                    AveragePressureLbs = aggregate.AveragePressureLbs,
                    IsDeleted = sample.IsDeleted
                };
            })
            .ToList();
    }

    private ReadinessViewModel BuildDashboardReadiness(
        string sampleTypeName,
        string? fruitType,
        DashboardSampleFruitAggregate aggregate,
        IReadOnlyList<string> receiptPhotos,
        IReadOnlyList<string> samplePhotos)
    {
        var missing = new List<string>();
        var starchRequired = IsStarchRequiredForEmail(sampleTypeName, fruitType);
        if (aggregate.CompletedRows == 0) missing.Add("At least one completed fruit row is required.");
        if (aggregate.InvalidRows > 0) missing.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        if (starchRequired && aggregate.StarchMissingRows > 0) missing.Add("Starch is required for all completed fruit rows.");
        var requiredPhotoChecklist = photoRequirementPolicy.BuildChecklist(sampleTypeName, receiptPhotos, samplePhotos, fruitType);
        missing.AddRange(photoRequirementPolicy.MissingRequiredPhotos(sampleTypeName, receiptPhotos, samplePhotos, fruitType));

        return new ReadinessViewModel
        {
            IsReady = missing.Count == 0,
            MissingItems = missing,
            Checklist = [],
            CompletedFruitCount = aggregate.CompletedRows,
            StarchMissingCount = aggregate.StarchMissingRows,
            HasBinTruck = receiptPhotos.Contains("BinTruck"),
            HasSampleBeforeCutting = samplePhotos.Contains("SampleBeforeCutting"),
            HasCutFruit = samplePhotos.Contains("CutFruit"),
            HasFruitAfterStarch = samplePhotos.Contains("FruitAfterStarch"),
            RequiredPhotoChecklist = requiredPhotoChecklist
        };
    }

    private IReadOnlyList<string> BuildDashboardReviewReasons(string status, DashboardSampleFruitAggregate aggregate)
    {
        var reasons = new List<string>();
        if (status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Sample is explicitly marked Needs Review.");
        }

        AddThresholdReason(reasons, aggregate.AveragePressureLbs, "DashboardReview:LowPressureLbs", value => aggregate.AveragePressureLbs < value, value => $"Average pressure {aggregate.AveragePressureLbs:0.##} lbs is below configured low threshold {value:0.##} lbs.");
        AddThresholdReason(reasons, aggregate.AveragePressureLbs, "DashboardReview:HighPressureLbs", value => aggregate.AveragePressureLbs > value, value => $"Average pressure {aggregate.AveragePressureLbs:0.##} lbs is above configured high threshold {value:0.##} lbs.");
        AddThresholdReason(reasons, aggregate.AverageStarch, "DashboardReview:HighStarch", value => aggregate.AverageStarch > value, value => $"Average starch {aggregate.AverageStarch:0.##} is above configured threshold {value:0.##}.");

        if (aggregate.CompletedRows > 0)
        {
            var defectPercent = decimal.Round(aggregate.CompletedDefectRows * 100m / aggregate.CompletedRows, 2);
            AddThresholdReason(reasons, defectPercent, "DashboardReview:HighDefectPercent", value => defectPercent > value, value => $"Defects are present on {defectPercent:0.##}% of completed fruit, above configured threshold {value:0.##}%.");
        }

        if (aggregate.MinimumPressureLbs is decimal minimum && aggregate.MaximumPressureLbs is decimal maximum)
        {
            var variance = decimal.Round(maximum - minimum, 2);
            AddThresholdReason(reasons, variance, "DashboardReview:HighPressureVarianceLbs", value => variance > value, value => $"Pressure variance {variance:0.##} lbs is above configured threshold {value:0.##} lbs.");
        }

        return reasons;
    }

    private ReadinessViewModel BuildCompactReadiness(
        string sampleTypeName,
        string? fruitType,
        IReadOnlyList<DashboardSampleFruitRow> rows,
        IReadOnlyList<string> receiptPhotos,
        IReadOnlyList<string> samplePhotos)
    {
        var completedRows = rows.Where(x => x.IsCompleted).ToList();
        var missing = new List<string>();
        var invalidRows = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null || x.WeightGrams is null || x.GradeId is null);
        var pressureMissing = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null);
        var weightMissing = completedRows.Count(x => x.WeightGrams is null);
        var gradeMissing = completedRows.Count(x => x.GradeId is null);
        var starchMissing = completedRows.Count(x => x.StarchScaleValueId is null);
        var starchRequired = IsStarchRequiredForEmail(sampleTypeName, fruitType);

        if (completedRows.Count == 0) missing.Add("At least one completed fruit row is required.");
        if (invalidRows > 0) missing.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        if (starchRequired && starchMissing > 0) missing.Add("Starch is required for all completed fruit rows.");
        var requiredPhotoChecklist = photoRequirementPolicy.BuildChecklist(sampleTypeName, receiptPhotos, samplePhotos, fruitType);
        missing.AddRange(photoRequirementPolicy.MissingRequiredPhotos(sampleTypeName, receiptPhotos, samplePhotos, fruitType));

        var checklist = new List<ReadinessChecklistItem>
        {
            ChecklistItem("Required data", "At least one completed fruit row", completedRows.Count > 0, "Missing"),
            ChecklistItem("Required data", "Pressure 1 and Pressure 2 for every completed fruit row", completedRows.Count == 0 || pressureMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Weight for every completed fruit row", completedRows.Count == 0 || weightMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Grade for every completed fruit row", completedRows.Count == 0 || gradeMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            starchRequired
                ? ChecklistItem("Required data", "Starch for every completed fruit row", completedRows.Count == 0 || starchMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing")
                : new ReadinessChecklistItem("Required data", "Starch for every completed fruit row", "Optional", "pending")
        };
        checklist.AddRange(requiredPhotoChecklist);

        return new ReadinessViewModel
        {
            IsReady = missing.Count == 0,
            MissingItems = missing,
            Checklist = checklist,
            CompletedFruitCount = completedRows.Count,
            StarchMissingCount = starchMissing,
            HasBinTruck = receiptPhotos.Contains("BinTruck"),
            HasSampleBeforeCutting = samplePhotos.Contains("SampleBeforeCutting"),
            HasCutFruit = samplePhotos.Contains("CutFruit"),
            HasFruitAfterStarch = samplePhotos.Contains("FruitAfterStarch"),
            RequiredPhotoChecklist = requiredPhotoChecklist
        };
    }

    private IReadOnlyList<string> BuildCompactReviewReasons(
        string status,
        IReadOnlyList<DashboardSampleFruitRow> rows,
        decimal? averagePressureLbs)
    {
        var reasons = new List<string>();
        if (status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Sample is explicitly marked Needs Review.");
        }

        AddThresholdReason(reasons, averagePressureLbs, "DashboardReview:LowPressureLbs", value => averagePressureLbs < value, value => $"Average pressure {averagePressureLbs:0.##} lbs is below configured low threshold {value:0.##} lbs.");
        AddThresholdReason(reasons, averagePressureLbs, "DashboardReview:HighPressureLbs", value => averagePressureLbs > value, value => $"Average pressure {averagePressureLbs:0.##} lbs is above configured high threshold {value:0.##} lbs.");

        var starchValues = rows
            .Where(x => x.Starch is not null)
            .Select(x => x.Starch!.Value)
            .ToList();
        var averageStarch = starchValues.Count == 0 ? (decimal?)null : decimal.Round(starchValues.Average(), 2);
        AddThresholdReason(reasons, averageStarch, "DashboardReview:HighStarch", value => averageStarch > value, value => $"Average starch {averageStarch:0.##} is above configured threshold {value:0.##}.");

        var completedRows = rows.Where(x => x.IsCompleted).ToList();
        if (completedRows.Count > 0)
        {
            var defectRows = completedRows.Count(x => x.HasDefects);
            var defectPercent = decimal.Round(defectRows * 100m / completedRows.Count, 2);
            AddThresholdReason(reasons, defectPercent, "DashboardReview:HighDefectPercent", value => defectPercent > value, value => $"Defects are present on {defectPercent:0.##}% of completed fruit, above configured threshold {value:0.##}%.");
        }

        var pressureValues = rows
            .Select(x => Average(x.Pressure1Lbs, x.Pressure2Lbs))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();
        if (pressureValues.Count > 1)
        {
            var variance = decimal.Round(pressureValues.Max() - pressureValues.Min(), 2);
            AddThresholdReason(reasons, variance, "DashboardReview:HighPressureVarianceLbs", value => variance > value, value => $"Pressure variance {variance:0.##} lbs is above configured threshold {value:0.##} lbs.");
        }

        return reasons;
    }

    private static IReadOnlyList<RoomVarietyColorSegmentViewModel> BuildRoomVarietyColorSegments(
        IReadOnlyList<DashboardInventorySnapshot> roomLots,
        IReadOnlyDictionary<string, VarietyColorResolved> colorMap)
    {
        var totalBins = roomLots.Sum(x => x.CurrentBins);
        if (totalBins <= 0)
        {
            return [];
        }

        return roomLots
            .GroupBy(
                x => $"{x.VarietyKey}\u001f{x.ProductionType}\u001f{x.IsOrganic?.ToString() ?? "-"}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var identity = group.First();
                var bins = group.Sum(x => x.CurrentBins);
                colorMap.TryGetValue(identity.VarietyKey, out var resolved);
                var name = resolved?.VarietyName ?? group.Select(x => x.VarietyName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? identity.VarietyKey;
                return new RoomVarietyColorSegmentViewModel
                {
                    VarietyKey = identity.VarietyKey,
                    VarietyName = name,
                    ProductionType = identity.ProductionType,
                    IsOrganic = identity.IsOrganic,
                    CurrentBins = bins,
                    Percent = decimal.Round(bins / (decimal)totalBins * 100m, 1),
                    HexColor = resolved?.HexColor ?? VarietyColorService.FallbackColor(identity.VarietyKey),
                    IsConfiguredColor = resolved?.IsConfigured == true
                };
            })
            .OrderByDescending(x => x.CurrentBins)
            .ThenBy(x => x.VarietyName)
            .ThenBy(x => x.ProductionType)
            .ThenBy(x => x.VarietyKey)
            .ToList();
    }

    private static string CurrentDashboardLotKey(int roomId, string lot, string variety) =>
        RoomInventoryImportService.CurrentStorageLotKey(roomId, lot, variety);

    private async Task<IReadOnlyList<RoomLotSummaryViewModel>> BuildRoomLotSummariesAsync(
        int? roomId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? allowedRoomIds = null,
        int? cropYear = null,
        CanonicalVarietyFilter? varietyFilter = null)
    {
        if (allowedRoomIds is { Count: 0 })
        {
            return [];
        }

        var receiptsQuery = dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
                .ThenInclude(x => x.Warehouse)
            .Include(x => x.FruitProfile)
            .Where(x => !x.IsDeleted);
        if (roomId is not null)
        {
            receiptsQuery = receiptsQuery.Where(x => x.RoomId == roomId);
        }
        if (allowedRoomIds is not null)
        {
            receiptsQuery = receiptsQuery.Where(x => allowedRoomIds.Contains(x.RoomId));
        }
        if (cropYear is not null)
        {
            receiptsQuery = receiptsQuery.Where(x => x.CropYear == cropYear);
        }
        if (varietyFilter is not null)
        {
            receiptsQuery = receiptsQuery.Where(x => varietyFilter.FruitProfileIds.Contains(x.FruitProfileId));
        }

        var receipts = await receiptsQuery
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.Room.Code)
            .ThenBy(x => x.GrowerName)
            .ThenBy(x => x.LotCode)
            .Take(MaximumCurrentStorageSourceRows + 1)
            .ToListAsync(cancellationToken);
        if (receipts.Count > MaximumCurrentStorageSourceRows)
        {
            throw new InvalidOperationException(
                $"Current-storage receipt selection exceeds the safe limit of {MaximumCurrentStorageSourceRows}. Filter by facility, room, or crop year.");
        }
        var receiptIds = receipts.Select(x => x.Id).ToList();
        var depletionByReceipt = await dbContext.RoomDepletions.AsNoTracking()
            .Where(x => receiptIds.Contains(x.ReceiptId) && !x.IsVoided)
            .GroupBy(x => x.ReceiptId)
            .Select(x => new { ReceiptId = x.Key, Bins = x.Sum(y => y.BinCountDepleted) })
            .ToDictionaryAsync(x => x.ReceiptId, x => x.Bins, cancellationToken);
        var receiptAdjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
            .ToListAsync(cancellationToken);
        var latestAdjustmentByReceipt = receiptAdjustments
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First());
        var startingInventoryLotSummaries = await BuildAdjustmentOnlyLotSummariesAsync(
            roomId,
            cancellationToken,
            allowedRoomIds,
            cropYear,
            varietyFilter);
        var qcTargets = receipts
            .Select(CanonicalQcFruitIdentity.FromReceipt)
            .Concat(startingInventoryLotSummaries.Select(RoomLotQcIdentity))
            .Where(x => x is not null)
            .Select(x => x!)
            .DistinctBy(x => x.LookupKey)
            .ToList();
        var samples = await LoadRoomConditionSamplesAsync(qcTargets, cancellationToken);
        var samplesByReceipt = samples.GroupBy(x => x.ReceiptId).ToDictionary(x => x.Key, x => x.ToList());
        var conditionDataByLot = BuildRoomConditionDataByLot(qcTargets, samples);

        var roomCorrectionCutoffs = await BuildCurrentBalanceCorrectionCutoffsAsync(roomId, cancellationToken);
        var receiptLotSummaries = receipts
            .Where(receipt => ReceiptStorageExclusionReason(
                receipt,
                string.Join(", ", samplesByReceipt.GetValueOrDefault(receipt.Id, []).Select(x => x.SampleType).Distinct(StringComparer.OrdinalIgnoreCase)),
                roomCorrectionCutoffs) is null)
            .GroupBy(ReceiptDedupeKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.UpdatedAt).ThenByDescending(y => y.Id).First())
            .Select(receipt =>
        {
            var depleted = depletionByReceipt.GetValueOrDefault(receipt.Id);
            var latestAdjustment = latestAdjustmentByReceipt.GetValueOrDefault(receipt.Id);
            var currentBins = latestAdjustment is not null
                ? Math.Max(0, latestAdjustment.NewBinCount)
                : Math.Max(0, receipt.BinCount - depleted);
            var hasSharedConditionData = conditionDataByLot.ContainsKey(QcConditionLotKey(receipt));
            var lotSamples = samplesByReceipt.GetValueOrDefault(receipt.Id, []);
            IReadOnlyList<RoomConditionSample> effectiveLotSamples = hasSharedConditionData ? [] : lotSamples;
            var pressures = effectiveLotSamples.Count == 0 ? [] : PressureValues(effectiveLotSamples).ToList();
            var starch = effectiveLotSamples.Count == 0 ? [] : StarchValues(effectiveLotSamples).ToList();
            return new RoomLotSummaryViewModel
            {
                InventoryKey = $"R:{receipt.Id}",
                ReceiptId = receipt.Id,
                RoomId = receipt.RoomId,
                CropYear = receipt.CropYear,
                FruitProfileId = receipt.FruitProfileId,
                GrowerLotId = receipt.GrowerLotId,
                Warehouse = receipt.Warehouse.Code,
                Facility = FacilityCode(receipt.Warehouse.Code, receipt.Warehouse.Name),
                LocationGroup = RoomLocationGroup(receipt.Room),
                RoomCode = receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
                DisplayReceiptId = receipt.CompuTechReceiptId,
                GrowerNumber = receipt.GrowerNumber ?? "",
                OrchardName = receipt.CanonicalOrchardBlock?.CanonicalOrchard.OrchardName ?? "",
                BlockName = receipt.CanonicalOrchardBlock?.CanonicalBlockName ?? "",
                PoolStart = receipt.PoolStart ?? "",
                GrowerName = receipt.GrowerName,
                LotCode = receipt.LotCode,
                VarietyCode = receipt.FruitProfile.VarietyCode,
                CanonicalVarietyKey = VarietyColorService.IdentityFromProfile(receipt.FruitProfile).Key,
                CanonicalVarietyName = VarietyColorService.IdentityFromProfile(receipt.FruitProfile).Name,
                ProductionType = receipt.FruitProfile.ProductionType,
                IsOrganic = receipt.FruitProfile.IsOrganic,
                InventoryStatus = "",
                FirstReceivedAt = receipt.ReceivedAt,
                OriginalBins = receipt.BinCount,
                DepletedBins = depleted,
                CurrentBins = currentBins,
                AveragePressureLbs = AverageOrNull(pressures),
                PressureStdDevLbs = StandardDeviationOrNull(pressures),
                MonthOverMonthPressureChangeLbs = effectiveLotSamples.Count == 0
                    ? null
                    : MonthPressureChange(receipt.RoomId, currentMonth: true, effectiveLotSamples),
                AverageStarch = AverageOrNull(starch),
                DefectSummary = effectiveLotSamples.Count == 0 ? "None" : SummarizeDefects(effectiveLotSamples),
                LastSampleDate = effectiveLotSamples.Count == 0 ? null : effectiveLotSamples.Max(x => x.SampleTakenAt),
                SampleCount = effectiveLotSamples.Count,
                EnteredFruitCount = effectiveLotSamples.SelectMany(x => x.FruitRows).Count(HasEnteredFruitData),
                DepletionStatus = currentBins > 0 ? "Current" : depleted > 0 ? "Depleted" : "No bins",
                ReviewFlags = effectiveLotSamples.Count == 0
                    ? []
                    : BuildRoomReviewFlags(
                        pressures,
                        starch,
                        effectiveLotSamples.SelectMany(x => x.FruitRows).ToList(),
                        MonthPressureChange(receipt.RoomId, currentMonth: true, effectiveLotSamples)),
                ReceiptEvidence = [new RoomReceiptEvidenceLinkViewModel(receipt.Id, receipt.CompuTechReceiptId)],
                ReceiptEvidenceCount = 1,
                Samples = effectiveLotSamples.Count == 0
                    ? []
                    : effectiveLotSamples
                        .OrderByDescending(x => x.SampleTakenAt)
                        .Select(x => new RoomSampleLinkViewModel(x.Id, x.SampleSequenceNumber <= 1 ? receipt.CompuTechReceiptId : $"{receipt.CompuTechReceiptId}({x.SampleSequenceNumber})", x.SampleType))
                        .ToList(),
                SampleEvidenceCount = effectiveLotSamples.Count
            };
        }).ToList();

        var lotSummaries = receiptLotSummaries.Concat(startingInventoryLotSummaries).ToList();
        var growerResolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(cancellationToken);
        foreach (var lot in lotSummaries)
        {
            lot.GrowerName = growerResolver.DisplayName(lot.GrowerName, lot.GrowerNumber.Length > 0 ? lot.GrowerNumber : lot.LotCode);
        }
        ApplyQcConditionData(lotSummaries, conditionDataByLot);
        var ledgerSnapshots = await RoomInventoryLedger.GetSnapshotsAsync(
            null,
            allowedRoomIds ?? (roomId is null ? null : [roomId.Value]),
            cancellationToken);
        if (varietyFilter is not null)
        {
            ledgerSnapshots = ledgerSnapshots.Where(varietyFilter.Matches).ToList();
        }
        ApplyLedgerBalances(lotSummaries, ledgerSnapshots);

        foreach (var group in lotSummaries.Where(x => x.CurrentBins > 0).GroupBy(x => x.RoomId))
        {
            var weakest = FindWeakestLot(group.ToList());
            if (weakest is not null && group.SingleOrDefault(x => x.ReceiptId == weakest.ReceiptId && x.InventoryAdjustmentId == weakest.InventoryAdjustmentId) is { } match)
            {
                match.WeakestReason = weakest.Reason;
            }
        }

        return lotSummaries;
    }

    private void ApplyLedgerBalances(
        IReadOnlyList<RoomLotSummaryViewModel> lotSummaries,
        IReadOnlyList<RoomInventoryLedgerSnapshot> ledgerSnapshots)
    {
        var balanceGroups = ledgerSnapshots
            .GroupBy(
                x => LedgerLotKey(x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId),
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        var conflictingGroups = balanceGroups
            .Where(x => !CanReconcileDashboardLedgerSnapshots(x))
            .ToList();
        if (conflictingGroups.Count > 0)
        {
            var sample = conflictingGroups
                .Take(5)
                .Select(x => $"{x.Key} [{DashboardLedgerIdentitySummary(x)}]");
            var suffix = conflictingGroups.Count > 5 ? $"; plus {conflictingGroups.Count - 5} more" : "";
            throw new InvalidOperationException(
                $"Room inventory ledger contains {conflictingGroups.Count} conflicting dashboard key(s): {string.Join("; ", sample)}{suffix}. "
                + "The conflicting quantities were not selected or discarded.");
        }

        var compatibleDuplicates = balanceGroups.Where(x => x.Count() > 1).ToList();
        if (compatibleDuplicates.Count > 0)
        {
            logger.LogWarning(
                "Room inventory dashboard reconciled {DuplicateKeyCount} compatible duplicate ledger key(s) by summing authoritative balances before display clamping. Sample keys: {DuplicateKeys}. No ledger rows were changed.",
                compatibleDuplicates.Count,
                string.Join(", ", compatibleDuplicates.Take(5).Select(x => x.Key)));
        }

        var snapshotsByKey = balanceGroups.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RoomInventoryLedgerSnapshot>)x.ToList(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in lotSummaries.GroupBy(
                     x => LedgerLotKey(x.RoomId, x.CropYear, x.LotCode, x.VarietyCode, x.FruitProfileId),
                     StringComparer.OrdinalIgnoreCase))
        {
            var snapshots = snapshotsByKey.GetValueOrDefault(group.Key) ?? [];
            ApplyCanonicalLedgerBalance(group.Key, group.ToList(), snapshots);
        }
    }

    private async Task<CanonicalVarietyFilter?> BuildCanonicalVarietyFilterAsync(
        string? requestedVariety,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedVariety))
        {
            return null;
        }

        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.VarietyCode })
            .ToListAsync(cancellationToken);
        var requested = requestedVariety.Trim();
        var exactProfileKeys = profiles
            .Where(x => string.Equals(x.Name.Trim(), requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.VarietyCode.Trim(), requested, StringComparison.OrdinalIgnoreCase))
            .Select(x => VarietyColorService.NormalizeIdentity(x.Name, x.VarietyCode).Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exactProfileKeys.Count > 1)
        {
            throw new InvalidOperationException(
                $"Current-storage variety filter '{requested}' matches conflicting canonical varieties. Select the canonical variety name.");
        }

        var selectedKey = exactProfileKeys.SingleOrDefault()
            ?? VarietyColorService.NormalizeIdentity(requested, requested).Key;
        var adjustmentCodes = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.VarietyCode != null && x.VarietyCode != "")
            .Select(x => x.VarietyCode!)
            .Distinct()
            .OrderBy(x => x)
            .Take(RoomInventoryLedgerQueryService.MaximumRoomLotRows + 1)
            .ToListAsync(cancellationToken);
        if (adjustmentCodes.Count > RoomInventoryLedgerQueryService.MaximumRoomLotRows)
        {
            throw new InvalidOperationException(
                $"Current-storage variety aliases exceed the safe limit of {RoomInventoryLedgerQueryService.MaximumRoomLotRows}. Narrow the facility or room filter.");
        }

        var matchingProfiles = profiles
            .Where(x => string.Equals(
                VarietyColorService.NormalizeIdentity(x.Name, x.VarietyCode).Key,
                selectedKey,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var matchingProfileCodes = matchingProfiles
            .Select(x => x.VarietyCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rawCodes = adjustmentCodes
            .Where(x => matchingProfileCodes.Contains(x.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                || string.Equals(
                    VarietyColorService.NormalizeIdentity(x, x).Key,
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase))
            .Concat(matchingProfileCodes)
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CanonicalVarietyFilter(
            selectedKey,
            matchingProfiles.Select(x => x.Id).Distinct().ToList(),
            rawCodes);
    }

    private static bool CurrentLotMatchesFilter(
        RoomLotSummaryViewModel lot,
        CurrentGrowerLotsFilterForm filter,
        CanonicalVarietyFilter? varietyFilter)
    {
        if (filter.CropYear is int cropYear && lot.CropYear != cropYear)
        {
            return false;
        }
        if (varietyFilter is not null
            && !string.Equals(lot.CanonicalVarietyKey, varietyFilter.CanonicalKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(filter.Grower)
            && !ContainsIgnoreCase(lot.GrowerNumber, filter.Grower)
            && !ContainsIgnoreCase(lot.GrowerName, filter.Grower))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(filter.Search)
            && !ContainsIgnoreCase(lot.LotCode, filter.Search)
            && !ContainsIgnoreCase(lot.GrowerNumber, filter.Search)
            && !ContainsIgnoreCase(lot.GrowerName, filter.Search)
            && !lot.ReceiptEvidence.Any(x => ContainsIgnoreCase(x.DisplayReceiptId, filter.Search)))
        {
            return false;
        }

        return true;
    }

    private static void ApplyCanonicalLedgerBalance(
        string displayKey,
        IReadOnlyList<RoomLotSummaryViewModel> rows,
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var authoritativeBalance = Math.Max(0, snapshots.Sum(x => x.CurrentBins));
        var canonicalGrowerLotId = snapshots
            .Where(x => x.GrowerLotId is not null)
            .Select(x => x.GrowerLotId!.Value)
            .Distinct()
            .SingleOrDefault();
        var authoritativeGrowerNumber = snapshots
            .Select(x => x.GrowerNumber?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SingleOrDefault();
        var rowGrowerNumbers = rows
            .Select(x => x.GrowerNumber.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (canonicalGrowerLotId == 0 && authoritativeGrowerNumber is null && rowGrowerNumbers.Count > 1)
        {
            throw new InvalidOperationException(
                $"Room inventory ledger display key {displayKey} has conflicting fallback grower identities ({string.Join(",", rowGrowerNumbers.Take(5))}). No quantity was assigned.");
        }

        var canonicalCandidates = canonicalGrowerLotId != 0
            ? rows.Where(x => x.GrowerLotId == canonicalGrowerLotId).ToList()
            : authoritativeGrowerNumber is not null
                ? rows.Where(x => string.Equals(x.GrowerNumber.Trim(), authoritativeGrowerNumber, StringComparison.OrdinalIgnoreCase)).ToList()
                : rows;
        if (canonicalCandidates.Count == 0)
        {
            canonicalCandidates = rows;
        }

        var representative = canonicalCandidates
            .OrderByDescending(x => x.LastSampleDate)
            .ThenBy(x => x.FirstReceivedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(x => StableEvidenceLabel(x), StringComparer.OrdinalIgnoreCase)
            .First();
        var originalBins = rows.Sum(x => Math.Max(0, x.OriginalBins));

        MergeLotEvidence(representative, rows);
        foreach (var row in rows)
        {
            row.CurrentBins = 0;
            row.DepletedBins = Math.Max(0, row.OriginalBins);
            row.DepletionStatus = row.DepletedBins > 0 ? "Depleted" : "No bins";
        }

        if (canonicalGrowerLotId != 0)
        {
            representative.GrowerLotId = canonicalGrowerLotId;
        }
        if (authoritativeGrowerNumber is not null)
        {
            representative.GrowerNumber = authoritativeGrowerNumber;
        }
        representative.OriginalBins = Math.Max(authoritativeBalance, originalBins);
        representative.CurrentBins = authoritativeBalance;
        representative.DepletedBins = Math.Max(0, representative.OriginalBins - authoritativeBalance);
        representative.DepletionStatus = authoritativeBalance > 0
            ? "Current"
            : representative.DepletedBins > 0 ? "Depleted" : "No bins";
    }

    private static void MergeLotEvidence(
        RoomLotSummaryViewModel representative,
        IReadOnlyList<RoomLotSummaryViewModel> rows)
    {
        var receiptEvidence = rows
            .SelectMany(x => x.ReceiptEvidence.Count > 0
                ? x.ReceiptEvidence
                : x.ReceiptId is long receiptId
                    ? [new RoomReceiptEvidenceLinkViewModel(receiptId, x.DisplayReceiptId)]
                    : [])
            .GroupBy(x => x.ReceiptId)
            .Select(x => x.OrderBy(y => y.DisplayReceiptId, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x => x.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReceiptId)
            .ToList();
        var sampleEvidence = rows
            .SelectMany(x => x.Samples)
            .GroupBy(x => x.SampleId)
            .Select(x => x.OrderBy(y => y.DisplayReceiptId, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x => x.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SampleId)
            .ToList();
        var latestQc = rows
            .Where(x => x.LastSampleDate is not null)
            .OrderByDescending(x => x.LastSampleDate)
            .ThenBy(x => StableEvidenceLabel(x), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        representative.ReceiptEvidence = receiptEvidence.Take(MaximumLotEvidenceLinks).ToList();
        representative.ReceiptEvidenceCount = receiptEvidence.Count;
        representative.Samples = sampleEvidence.Take(MaximumLotEvidenceLinks).ToList();
        representative.SampleEvidenceCount = sampleEvidence.Count;
        representative.SampleCount = sampleEvidence.Count;
        representative.FirstReceivedAt = rows.Select(x => x.FirstReceivedAt).Where(x => x is not null).DefaultIfEmpty().Min();
        representative.DisplayReceiptId = receiptEvidence.Count switch
        {
            0 => representative.DisplayReceiptId,
            1 => receiptEvidence[0].DisplayReceiptId,
            _ => $"{receiptEvidence.Count} source receipts"
        };

        if (latestQc is null || ReferenceEquals(latestQc, representative))
        {
            return;
        }

        representative.AveragePressureLbs = latestQc.AveragePressureLbs;
        representative.PressureStdDevLbs = latestQc.PressureStdDevLbs;
        representative.MonthOverMonthPressureChangeLbs = latestQc.MonthOverMonthPressureChangeLbs;
        representative.AverageStarch = latestQc.AverageStarch;
        representative.DefectSummary = latestQc.DefectSummary;
        representative.GradeSummary = latestQc.GradeSummary;
        representative.SizeSummary = latestQc.SizeSummary;
        representative.LastSampleDate = latestQc.LastSampleDate;
        representative.LatestQcSource = latestQc.LatestQcSource;
        representative.EnteredFruitCount = latestQc.EnteredFruitCount;
        representative.ReviewFlags = latestQc.ReviewFlags;
    }

    private static string StableEvidenceLabel(RoomLotSummaryViewModel row) =>
        string.Join('|',
            row.GrowerNumber.Trim().ToUpperInvariant(),
            row.GrowerName.Trim().ToUpperInvariant(),
            row.LotCode.Trim().ToUpperInvariant(),
            row.CanonicalVarietyKey,
            row.ProductionType.Trim().ToUpperInvariant(),
            row.IsOrganic?.ToString() ?? "-");

    private static string LedgerLotKey(int roomId, int? cropYear, string lot, string variety, int? fruitProfileId) =>
        $"{roomId}|{cropYear?.ToString(CultureInfo.InvariantCulture) ?? "-"}|{lot.Trim().ToUpperInvariant()}|{variety.Trim().ToUpperInvariant()}|{fruitProfileId?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    private IReadOnlyDictionary<string, RoomConditionData> BuildRoomConditionDataByLot(
        IReadOnlyList<CanonicalQcFruitIdentity> targets,
        IReadOnlyList<RoomConditionSample> samples)
    {
        var samplesByIdentity = samples
            .Where(x => x.Identity is not null)
            .GroupBy(x => x.Identity!.LookupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<RoomConditionSample>)x.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, RoomConditionData>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var resolved = (samplesByIdentity.TryGetValue(target.LookupKey, out var exact)
                ? exact
                : CanonicalQcFruitIdentity.ResolveUnambiguous(target, samples, x => x.Identity))
                .OrderByDescending(x => x.SampleTakenAt)
                .ThenByDescending(x => x.Id)
                .ToList();
            if (resolved.Count == 0)
            {
                continue;
            }

            var latest = resolved[0];
            var pressures = PressureValues(resolved).ToList();
            var starch = StarchValues(resolved).ToList();
            var fruitRows = resolved.SelectMany(x => x.FruitRows).ToList();
            var monthPressureChange = MonthPressureChange(0, currentMonth: true, resolved);
            var sampleEvidence = resolved
                .Select(x => new RoomSampleLinkViewModel(x.Id, x.SampleSequenceNumber <= 1 ? x.DisplayReceiptId : $"{x.DisplayReceiptId}({x.SampleSequenceNumber})", x.SampleType))
                .ToList();
            var receiptEvidence = resolved
                .GroupBy(x => x.ReceiptId)
                .Select(x => new RoomReceiptEvidenceLinkViewModel(x.Key, x.First().DisplayReceiptId))
                .OrderBy(x => x.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ReceiptId)
                .ToList();
            result[target.LookupKey] = new RoomConditionData(
                AverageOrNull(PressureValues([latest]).ToList()) ?? AverageOrNull(pressures),
                StandardDeviationOrNull(PressureValues([latest]).ToList()) ?? StandardDeviationOrNull(pressures),
                monthPressureChange,
                AverageOrNull(StarchValues([latest]).ToList()) ?? AverageOrNull(starch),
                SummarizeDefects(resolved),
                latest.SampleTakenAt,
                latest.SampleType,
                resolved.Count,
                fruitRows.Count(HasEnteredFruitData),
                BuildRoomReviewFlags(pressures, starch, fruitRows, monthPressureChange),
                sampleEvidence.Take(MaximumLotEvidenceLinks).ToList(),
                sampleEvidence.Count,
                receiptEvidence.Take(MaximumLotEvidenceLinks).ToList(),
                receiptEvidence.Count,
                receiptEvidence.Select(x => x.ReceiptId).ToHashSet());
        }

        return result;
    }

    private static void ApplyQcConditionData(
        IReadOnlyList<RoomLotSummaryViewModel> lotSummaries,
        IReadOnlyDictionary<string, RoomConditionData> conditionDataByLot)
    {
        foreach (var lot in lotSummaries)
        {
            if (!conditionDataByLot.TryGetValue(QcConditionLotKey(lot), out var data))
            {
                continue;
            }

            lot.AveragePressureLbs = data.AveragePressureLbs;
            lot.PressureStdDevLbs = data.PressureStdDevLbs;
            lot.MonthOverMonthPressureChangeLbs = data.MonthOverMonthPressureChangeLbs;
            lot.AverageStarch = data.AverageStarch;
            lot.DefectSummary = data.DefectSummary;
            lot.LastSampleDate = data.LastSampleDate;
            lot.LatestQcSource = data.LatestQcSource;
            lot.SampleCount = data.SampleCount;
            lot.EnteredFruitCount = data.EnteredFruitCount;
            lot.ReviewFlags = data.ReviewFlags;
            lot.Samples = data.SampleEvidence;
            lot.SampleEvidenceCount = data.SampleEvidenceCount;
            if (lot.ReceiptEvidence.Count == 0
                || lot.ReceiptEvidence.All(x => data.ReceiptIds.Contains(x.ReceiptId)))
            {
                lot.ReceiptEvidence = data.ReceiptEvidence;
                lot.ReceiptEvidenceCount = data.ReceiptEvidenceCount;
                continue;
            }

            var receiptEvidence = lot.ReceiptEvidence.Concat(data.ReceiptEvidence)
                .GroupBy(x => x.ReceiptId)
                .Select(x => x.First())
                .OrderBy(x => x.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ReceiptId)
                .ToList();
            lot.ReceiptEvidence = receiptEvidence.Take(MaximumLotEvidenceLinks).ToList();
            lot.ReceiptEvidenceCount = receiptEvidence.Count;
        }
    }

    private static bool CanReconcileDashboardLedgerSnapshots(IEnumerable<RoomInventoryLedgerSnapshot> snapshots)
    {
        var rows = snapshots.ToList();
        return rows.Select(x => x.WarehouseId).Distinct().Count() <= 1
            && rows.Where(x => x.GrowerLotId is not null).Select(x => x.GrowerLotId).Distinct().Count() <= 1
            && DistinctNonEmpty(rows.Select(x => x.GrowerNumber)) <= 1
            && DistinctNonEmpty(rows.Select(x => x.InventoryStatus)) <= 1
            && DistinctNonEmpty(rows.Select(x => x.ProductionType)) <= 1
            && rows.Where(x => x.IsOrganic is not null).Select(x => x.IsOrganic).Distinct().Count() <= 1;
    }

    private static int DistinctNonEmpty(IEnumerable<string?> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string DashboardLedgerIdentitySummary(IEnumerable<RoomInventoryLedgerSnapshot> snapshots)
    {
        var rows = snapshots.ToList();
        var growerLotIds = rows.Where(x => x.GrowerLotId is not null).Select(x => x.GrowerLotId!.Value).Distinct().OrderBy(x => x).ToList();
        return $"snapshots={rows.Count}, warehouses={string.Join(",", rows.Select(x => x.WarehouseId).Distinct().OrderBy(x => x))}, "
            + $"growerLotIds={(growerLotIds.Count == 0 ? "legacy-only" : string.Join(",", growerLotIds))}, "
            + $"growers={string.Join(",", rows.Select(x => x.GrowerNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))}, "
            + $"statuses={string.Join(",", rows.Select(x => x.InventoryStatus).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))}";
    }

    private async Task<IReadOnlyList<RoomConditionSample>> LoadRoomConditionSamplesAsync(
        IReadOnlyList<CanonicalQcFruitIdentity> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        var candidateQuery = CanonicalQcFruitIdentity.FilterReceiptSamples(
                dbContext.QcSamples.AsNoTracking(),
                targets);
        var headers = await CanonicalQcFruitIdentity.OrderCandidates(
                candidateQuery,
                dbContext.Database.ProviderName)
            .Select(x => new RoomConditionSampleHeader(
                x.Id,
                x.ReceiptId!.Value,
                x.Receipt!.RoomId,
                x.Receipt.CropYear,
                x.Receipt.GrowerLotId,
                x.Receipt.FruitProfileId,
                x.Receipt.GrowerNumber ?? x.Receipt.LotCode,
                x.Receipt.LotCode,
                x.Receipt.FruitProfile.VarietyCode,
                x.Receipt.FruitProfile.ProductionType,
                x.Receipt.FruitProfile.IsOrganic,
                x.Receipt.CompuTechReceiptId,
                x.SampleSequenceNumber,
                x.SampleType.Name,
                x.SampleTakenAt))
            .Take(CanonicalQcFruitIdentity.CandidateLimit(targets.Count))
            .ToListAsync(cancellationToken);
        var sampleIds = headers.Select(x => x.Id).ToList();
        if (sampleIds.Count == 0)
        {
            return [];
        }

        var fruitRows = await dbContext.QcFruitReadings.AsNoTracking()
            .Where(x => sampleIds.Contains(x.QcSampleId))
            .Select(x => new RoomConditionFruitRowData(
                x.Id,
                x.QcSampleId,
                x.Pressure1Lbs,
                x.Pressure2Lbs,
                x.WeightGrams,
                x.GradeId,
                x.StarchScaleValueId,
                x.StarchScaleValue == null ? null : x.StarchScaleValue.Value,
                x.SizeCategory,
                x.IsCompleted))
            .ToListAsync(cancellationToken);
        var readingIds = fruitRows.Select(x => x.Id).ToList();
        var defectsByReading = readingIds.Count == 0
            ? new Dictionary<long, List<string>>()
            : (await dbContext.QcFruitDefects.AsNoTracking()
                .Where(x => readingIds.Contains(x.QcFruitReadingId))
                .Select(x => new { x.QcFruitReadingId, x.DefectType.Name })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.QcFruitReadingId)
                .ToDictionary(x => x.Key, x => x.Select(y => y.Name).ToList());
        var fruitRowsBySample = fruitRows
            .GroupBy(x => x.SampleId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(row => new RoomConditionFruitRow(
                    row.Pressure1Lbs,
                    row.Pressure2Lbs,
                    row.WeightGrams,
                    row.GradeId,
                    row.StarchScaleValueId,
                    row.Starch,
                    row.SizeCategory,
                    row.IsCompleted,
                    defectsByReading.GetValueOrDefault(row.Id, [])))
                    .ToList());

        return headers.Select(header => new RoomConditionSample(
                header.Id,
                header.ReceiptId,
                header.RoomId,
                header.CropYear,
                header.GrowerLotId,
                header.FruitProfileId,
                header.GrowerNumber,
                header.LotCode,
                header.VarietyCode,
                header.ProductionType,
                header.IsOrganic,
                header.DisplayReceiptId,
                header.SampleSequenceNumber,
                header.SampleType,
                header.SampleTakenAt,
                fruitRowsBySample.GetValueOrDefault(header.Id, [])))
            .ToList();
    }

    private static DashboardInventorySnapshot ToDashboardInventorySnapshot(RoomLotSummaryViewModel lot)
    {
        var variety = VarietyColorService.NormalizeIdentity(
            string.IsNullOrWhiteSpace(lot.CanonicalVarietyName) ? lot.VarietyCode : lot.CanonicalVarietyName,
            lot.VarietyCode);
        return new DashboardInventorySnapshot(
            lot.RoomId,
            lot.CropYear,
            lot.FruitProfileId,
            lot.Warehouse,
            lot.Facility,
            lot.LocationGroup,
            lot.RoomCode,
            lot.GrowerName,
            lot.GrowerNumber,
            lot.GrowerLotId,
            lot.LotCode,
            lot.VarietyCode,
            variety.Key,
            variety.Name,
            lot.ProductionType,
            lot.IsOrganic,
            lot.InventoryStatus,
            lot.CurrentBins,
            lot.FirstReceivedAt);
    }

    private async Task<IReadOnlyList<RoomLotSummaryViewModel>> BuildAdjustmentOnlyLotSummariesAsync(
        int? roomId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? allowedRoomIds = null,
        int? cropYear = null,
        CanonicalVarietyFilter? varietyFilter = null)
    {
        var query = dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
                .ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt)
            .Include(x => x.FruitProfile)
            .Where(x => x.ReceiptId == null || x.AdjustmentType == "TransferIn");
        if (roomId is not null)
        {
            query = query.Where(x => x.RoomId == roomId);
        }
        if (allowedRoomIds is not null)
        {
            query = query.Where(x => allowedRoomIds.Contains(x.RoomId));
        }
        if (cropYear is not null)
        {
            query = query.Where(x => x.CropYear == null || x.CropYear == cropYear);
        }
        if (varietyFilter is not null)
        {
            query = query.Where(x =>
                (x.FruitProfileId != null && varietyFilter.FruitProfileIds.Contains(x.FruitProfileId.Value))
                || (x.FruitProfileId == null && x.ReceiptId != null && varietyFilter.FruitProfileIds.Contains(x.Receipt!.FruitProfileId))
                || (x.FruitProfileId == null && x.VarietyCode != null && varietyFilter.RawVarietyCodes.Contains(x.VarietyCode.ToUpper())));
        }

        var adjustments = await query
            .OrderBy(x => x.RoomId)
            .ThenBy(x => x.LotNumber)
            .ThenBy(x => x.Id)
            .Take(MaximumCurrentStorageSourceRows + 1)
            .ToListAsync(cancellationToken);
        if (adjustments.Count > MaximumCurrentStorageSourceRows)
        {
            throw new InvalidOperationException(
                $"Current-storage adjustment selection exceeds the safe limit of {MaximumCurrentStorageSourceRows}. Filter by facility, room, or crop year.");
        }
        var correctionCutoffs = await BuildCurrentBalanceCorrectionCutoffsAsync(roomId, cancellationToken);
        return ApplyLatestCurrentBalanceRows(adjustments
                .Where(x => !IsSupersededByRoomCurrentBalanceCorrection(x, correctionCutoffs)))
            .Where(x => x.NewBinCount > 0)
            .Select(x => new RoomLotSummaryViewModel
            {
                InventoryKey = $"A:{x.Id}:{RoomInventoryImportService.CurrentStorageLotKey(x.RoomId, x.LotNumber, x.VarietyCode ?? "")}",
                ReceiptId = null,
                InventoryAdjustmentId = x.Id,
                RoomId = x.RoomId,
                CropYear = x.CropYear,
                FruitProfileId = x.FruitProfileId,
                GrowerLotId = x.GrowerLotId,
                Warehouse = x.Warehouse.Code,
                Facility = FacilityCode(x.Warehouse.Code, x.Warehouse.Name),
                LocationGroup = !string.IsNullOrWhiteSpace(x.SourceSubLocation) ? x.SourceSubLocation! : RoomLocationGroup(x.Room),
                RoomCode = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                DisplayReceiptId = x.Receipt != null ? x.Receipt.CompuTechReceiptId : x.Source ?? x.Reason ?? "Current inventory baseline",
                GrowerNumber = x.LotNumber,
                PoolStart = x.PoolStart ?? "",
                GrowerName = x.GrowerName,
                LotCode = x.LotNumber,
                VarietyCode = x.VarietyCode ?? "",
                CanonicalVarietyKey = VarietyColorService.NormalizeIdentity(x.FruitProfile?.Name ?? x.VarietyCode, x.VarietyCode).Key,
                CanonicalVarietyName = VarietyColorService.NormalizeIdentity(x.FruitProfile?.Name ?? x.VarietyCode, x.VarietyCode).Name,
                ProductionType = x.FruitProfile?.ProductionType ?? "",
                IsOrganic = x.FruitProfile?.IsOrganic,
                InventoryStatus = x.InventoryStatus ?? "",
                FirstReceivedAt = x.Receipt?.ReceivedAt ?? x.AdjustmentAt,
                OriginalBins = x.NewBinCount,
                DepletedBins = 0,
                CurrentBins = x.NewBinCount,
                AveragePressureLbs = null,
                PressureStdDevLbs = null,
                MonthOverMonthPressureChangeLbs = null,
                AverageStarch = null,
                DefectSummary = "None",
                LastSampleDate = null,
                SampleCount = 0,
                EnteredFruitCount = 0,
                DepletionStatus = "Current",
                ReviewFlags = [],
                ReceiptEvidence = x.ReceiptId is long receiptId
                    ? [new RoomReceiptEvidenceLinkViewModel(receiptId, x.Receipt?.CompuTechReceiptId ?? $"Receipt {receiptId}")]
                    : [],
                ReceiptEvidenceCount = x.ReceiptId is null ? 0 : 1,
                Samples = [],
                SampleEvidenceCount = 0
            })
            .ToList();
    }

    private async Task DecorateCurrentRoomLotsAsync(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        IReadOnlyDictionary<string, RoomLotProjectionDistribution> sampleDistributions,
        CancellationToken cancellationToken)
    {
        var keys = lots
            .Select(x => string.IsNullOrWhiteSpace(x.CanonicalVarietyKey)
                ? VarietyColorService.NormalizeIdentity(x.VarietyCode, x.VarietyCode).Key
                : x.CanonicalVarietyKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var colors = varietyColorService is null
            ? keys.ToDictionary(
                x => x,
                x => new VarietyColorResolved(x, VarietyColorService.NormalizeIdentity(x, x).Name, VarietyColorService.FallbackColor(x), false),
                StringComparer.OrdinalIgnoreCase)
            : await varietyColorService.GetResolvedColorsReadOnlyAsync(keys, cancellationToken);

        foreach (var lot in lots)
        {
            var identity = VarietyColorService.NormalizeIdentity(
                string.IsNullOrWhiteSpace(lot.CanonicalVarietyName) ? lot.VarietyCode : lot.CanonicalVarietyName,
                lot.VarietyCode);
            lot.CanonicalVarietyKey = identity.Key;
            lot.CanonicalVarietyName = identity.Name;
            lot.VarietyHexColor = colors.GetValueOrDefault(identity.Key)?.HexColor ?? VarietyColorService.FallbackColor(identity.Key);
            if (sampleDistributions.TryGetValue(QcConditionLotKey(lot), out var distribution))
            {
                lot.GradeSummary = distribution.GradePercentages.Count == 0
                    ? "Unavailable"
                    : FormatProjectionGradeSummary(distribution.GradePercentages);
                lot.SizeSummary = distribution.SizeDistribution.Percentages.Count == 0
                    ? "Unavailable"
                    : string.Join(", ", distribution.SizeDistribution.Percentages
                        .OrderByDescending(x => x.Value)
                        .ThenBy(x => x.Key)
                        .Take(3)
                        .Select(x => $"{x.Key}: {x.Value:0.#}%"));
            }
        }
    }

    private async Task<IReadOnlyList<RoomReceiptEvidenceLinkViewModel>> DecorateReceiptProvenanceAsync(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        CancellationToken cancellationToken)
    {
        var targetGroups = lots
            .Where(x => x.CurrentBins > 0)
            .Select(x => new { Lot = x, Identity = RoomLotQcIdentity(x) })
            .Where(x => x.Identity is not null)
            .GroupBy(
                x => $"{x.Lot.RoomId}|{x.Identity!.LookupKey}",
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targetGroups.Count == 0)
        {
            return [];
        }

        if (targetGroups.Count > RoomReceiptProvenanceResolver.MaximumTargets)
        {
            throw new InvalidOperationException(
                $"Receipt provenance current fruit identities exceed the safe limit of {RoomReceiptProvenanceResolver.MaximumTargets}. Narrow the facility or room filter.");
        }

        var targets = targetGroups
            .Select(x => new ReceiptProvenanceTarget(
                x.Key,
                x.First().Lot.RoomId,
                x.Sum(y => y.Lot.CurrentBins),
                x.First().Identity!))
            .ToList();
        var identities = targets.Select(x => x.Identity).ToList();
        var cropYears = identities.Select(x => x.CropYear).Distinct().ToList();
        var growerLotIds = identities.Where(x => x.GrowerLotId is not null).Select(x => x.GrowerLotId!.Value).Distinct().ToList();
        var growerNumbers = identities.Select(x => x.GrowerNumber).Where(x => x.Length > 0).Distinct().ToList();
        var lotNumbers = identities.Select(x => x.LotNumber).Where(x => x.Length > 0).Distinct().ToList();
        var fruitProfileIds = identities.Where(x => x.FruitProfileId is not null).Select(x => x.FruitProfileId!.Value).Distinct().ToList();
        var varietyCodes = identities.Select(x => x.VarietyCode).Where(x => x.Length > 0).Distinct().ToList();
        var hasGrowerLotIds = growerLotIds.Count > 0;
        var hasLegacyLotIdentity = growerNumbers.Count > 0 && lotNumbers.Count > 0;
        var hasFruitProfileIds = fruitProfileIds.Count > 0;
        var hasLegacyProfileIdentity = varietyCodes.Count > 0;

        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Room)
                .ThenInclude(x => x.Warehouse)
            .Include(x => x.FruitProfile)
            .Where(x => !x.IsDeleted
                && cropYears.Contains(x.CropYear)
                && ((hasGrowerLotIds && x.GrowerLotId != null && growerLotIds.Contains(x.GrowerLotId.Value))
                    || (hasLegacyLotIdentity
                        && growerNumbers.Contains((x.GrowerNumber ?? x.LotCode).Trim().ToUpper())
                        && lotNumbers.Contains(x.LotCode.Trim().ToUpper())))
                && ((hasFruitProfileIds && fruitProfileIds.Contains(x.FruitProfileId))
                    || (hasLegacyProfileIdentity && varietyCodes.Contains(x.FruitProfile.VarietyCode.Trim().ToUpper()))))
            .OrderByDescending(x => x.ReceivedAt)
            .ThenByDescending(x => x.Id)
            .Take(RoomReceiptProvenanceResolver.MaximumReceiptCandidates + 1)
            .ToListAsync(cancellationToken);
        if (receipts.Count > RoomReceiptProvenanceResolver.MaximumReceiptCandidates)
        {
            throw new InvalidOperationException(
                $"Receipt provenance receipt candidates exceed the safe limit of {RoomReceiptProvenanceResolver.MaximumReceiptCandidates}. Narrow the facility or room filter.");
        }

        var transfers = await dbContext.RoomTransfers.AsNoTracking()
            .Include(x => x.FruitProfile)
            .Where(x => x.CropYear != null
                && cropYears.Contains(x.CropYear.Value)
                && ((hasGrowerLotIds && x.GrowerLotId != null && growerLotIds.Contains(x.GrowerLotId.Value))
                    || (hasLegacyLotIdentity && lotNumbers.Contains(x.LotNumber.Trim().ToUpper())))
                && ((hasFruitProfileIds && x.FruitProfileId != null && fruitProfileIds.Contains(x.FruitProfileId.Value))
                    || (hasLegacyProfileIdentity && x.VarietyCode != null && varietyCodes.Contains(x.VarietyCode.Trim().ToUpper()))))
            .OrderBy(x => x.TransferredAt)
            .ThenBy(x => x.Id)
            .Take(RoomReceiptProvenanceResolver.MaximumTransferCandidates + 1)
            .ToListAsync(cancellationToken);
        if (transfers.Count > RoomReceiptProvenanceResolver.MaximumTransferCandidates)
        {
            throw new InvalidOperationException(
                $"Receipt provenance transfer candidates exceed the safe limit of {RoomReceiptProvenanceResolver.MaximumTransferCandidates}. Narrow the facility or room filter.");
        }

        var receiptCandidates = receipts
            .Select(x => new { Receipt = x, Identity = CanonicalQcFruitIdentity.FromReceipt(x) })
            .Where(x => x.Identity is not null)
            .Select(x => new ReceiptProvenanceCandidate(
                x.Receipt.Id,
                x.Receipt.CompuTechReceiptId,
                x.Receipt.RoomId,
                x.Receipt.Room.Warehouse.Code,
                x.Receipt.Room.CropQcRoomName ?? x.Receipt.Room.DisplayName ?? x.Receipt.Room.Code,
                x.Receipt.BinCount,
                x.Receipt.ReceivedAt,
                CanonicalQcFruitIdentity.Normalize(x.Receipt.GrowerNumber ?? x.Receipt.LotCode),
                x.Receipt.GrowerName,
                x.Identity!))
            .ToList();
        var transferCandidates = transfers
            .Select(x => new
            {
                Transfer = x,
                Identity = CanonicalQcFruitIdentity.Create(
                    x.CropYear,
                    x.GrowerLotId,
                    x.LotNumber,
                    x.LotNumber,
                    x.FruitProfileId,
                    x.VarietyCode,
                    x.FruitProfile?.ProductionType,
                    x.FruitProfile?.IsOrganic)
            })
            .Where(x => x.Identity is not null)
            .Select(x => new TransferProvenanceCandidate(
                x.Transfer.Id,
                x.Transfer.SourceRoomId,
                x.Transfer.DestinationRoomId,
                x.Transfer.TransferredAt,
                x.Transfer.IsReversed,
                x.Identity!))
            .ToList();
        var resolved = RoomReceiptProvenanceResolver.Resolve(targets, receiptCandidates, transferCandidates);
        var roomWideLinks = new List<RoomReceiptEvidenceLinkViewModel>();

        foreach (var group in targetGroups)
        {
            var representative = group.First().Lot;
            var links = resolved.GetValueOrDefault(group.Key, [])
                .Select(x => new RoomReceiptEvidenceLinkViewModel(
                    x.Receipt.Id,
                    x.Receipt.DisplayReceiptId,
                    x.EvidenceType,
                    string.IsNullOrWhiteSpace(representative.GrowerNumber) ? x.Receipt.GrowerNumber : representative.GrowerNumber,
                    representative.GrowerName,
                    x.Receipt.ReceivedAt,
                    x.Receipt.OriginalWarehouse,
                    x.Receipt.OriginalRoom,
                    x.Receipt.OriginalBins,
                    x.TransferPathIds))
                .ToList();
            roomWideLinks.AddRange(links);
            foreach (var lot in group.Select(x => x.Lot))
            {
                lot.ReceiptEvidence = links.Take(MaximumLotEvidenceLinks).ToList();
                lot.ReceiptEvidenceCount = links.Count;
            }
        }

        return roomWideLinks
            .GroupBy(x => x.ReceiptId)
            .Select(x => x
                .OrderBy(y => RoomReceiptProvenanceResolver.EvidenceRank(y.EvidenceType))
                .ThenByDescending(y => y.ReceivedAt)
                .First())
            .OrderBy(x => RoomReceiptProvenanceResolver.EvidenceRank(x.EvidenceType))
            .ThenByDescending(x => x.ReceivedAt)
            .ThenBy(x => x.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<RoomGrowerSummaryViewModel> BuildRoomGrowerSummaries(IReadOnlyList<RoomLotSummaryViewModel> lots) =>
        lots
            .GroupBy(
                x => string.IsNullOrWhiteSpace(x.GrowerNumber)
                    ? $"UNAVAILABLE:{x.GrowerName.Trim().ToUpperInvariant()}"
                    : x.GrowerNumber.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.OrderBy(x => x.CanonicalVarietyName).ThenBy(x => x.LotCode).ToList();
                var totalBins = rows.Sum(x => x.CurrentBins);
                var pressureRows = rows.Where(x => x.AveragePressureLbs is not null).ToList();
                var starchRows = rows.Where(x => x.AverageStarch is not null).ToList();
                return new RoomGrowerSummaryViewModel
                {
                    GrowerNumber = rows.Select(x => x.GrowerNumber).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unavailable",
                    GrowerName = rows.Select(x => x.GrowerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Name unavailable",
                    CurrentBins = totalBins,
                    CurrentLotCount = rows.Select(CurrentRoomLotIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    WeightedPressureLbs = RoundOrNull(WeightedStatistics.WeightedMean(pressureRows.Select(x => (x.AveragePressureLbs!.Value, (decimal)x.CurrentBins)))),
                    PressureRepresentedBins = pressureRows.Sum(x => x.CurrentBins),
                    WeightedStarch = RoundOrNull(WeightedStatistics.WeightedMean(starchRows.Select(x => (x.AverageStarch!.Value, (decimal)x.CurrentBins)))),
                    StarchRepresentedBins = starchRows.Sum(x => x.CurrentBins),
                    Varieties = BuildVarietyPresentations(rows, totalBins),
                    Lots = rows
                };
            })
            .OrderBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<VarietyBinPresentationViewModel> BuildVarietyPresentations(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        int totalBins) =>
        lots
            .GroupBy(
                x => $"{x.CanonicalVarietyKey}\u001f{x.ProductionType}\u001f{x.IsOrganic?.ToString() ?? "-"}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var bins = group.Sum(x => x.CurrentBins);
                return new VarietyBinPresentationViewModel
                {
                    CanonicalVarietyKey = first.CanonicalVarietyKey,
                    DisplayName = first.CanonicalVarietyName,
                    ProductionType = first.ProductionType,
                    IsOrganic = first.IsOrganic,
                    HexColor = first.VarietyHexColor,
                    BinCount = bins,
                    Percent = totalBins <= 0 ? 0m : decimal.Round(bins / (decimal)totalBins * 100m, 1)
                };
            })
            .OrderByDescending(x => x.BinCount)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.ProductionType)
            .ThenBy(x => x.CanonicalVarietyKey)
            .ToList();

    private static string CurrentRoomLotIdentity(RoomLotSummaryViewModel lot) =>
        lot.GrowerLotId is int growerLotId
            ? $"G:{growerLotId}:{lot.CanonicalVarietyKey}:{lot.ProductionType}:{lot.IsOrganic}"
            : $"L:{lot.GrowerNumber.Trim().ToUpperInvariant()}:{lot.LotCode.Trim().ToUpperInvariant()}:{lot.CanonicalVarietyKey}:{lot.ProductionType}:{lot.IsOrganic}";

    private static CurrentGrowerLotViewModel ToCurrentGrowerLot(RoomLotSummaryViewModel lot) => new()
    {
        GrowerLotId = lot.GrowerLotId,
        CropYear = lot.CropYear,
        Grower = lot.GrowerName,
        GrowerNumber = lot.GrowerNumber,
        Lot = lot.LotCode,
        Variety = lot.CanonicalVarietyName,
        ProductionType = lot.ProductionType,
        IsOrganic = lot.IsOrganic,
        VarietyHexColor = lot.VarietyHexColor,
        Warehouse = lot.Warehouse,
        Room = lot.RoomCode,
        CurrentBins = lot.CurrentBins,
        FirstReceivedAt = lot.FirstReceivedAt,
        LastQcSampleAt = lot.LastSampleDate,
        LatestQcSource = lot.LatestQcSource,
        LatestAveragePressure = lot.AveragePressureLbs,
        LatestStarch = lot.AverageStarch,
        ReceiptEvidence = lot.ReceiptEvidence,
        ReceiptEvidenceCount = lot.ReceiptEvidenceCount
    };

    private static IReadOnlyList<CurrentStorageGrowerViewModel> BuildCurrentStorageGrowers(IReadOnlyList<RoomLotSummaryViewModel> lots) =>
        lots
            .GroupBy(
                x => string.IsNullOrWhiteSpace(x.GrowerNumber)
                    ? $"UNAVAILABLE:{x.GrowerName.Trim().ToUpperInvariant()}"
                    : x.GrowerNumber.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.OrderBy(x => x.CanonicalVarietyName).ThenBy(x => x.LotCode).ThenBy(x => x.RoomCode).ToList();
                var totalBins = rows.Sum(x => x.CurrentBins);
                var pressureRows = rows.Where(x => x.AveragePressureLbs is not null).ToList();
                var starchRows = rows.Where(x => x.AverageStarch is not null).ToList();
                return new CurrentStorageGrowerViewModel
                {
                    GrowerNumber = rows.Select(x => x.GrowerNumber).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unavailable",
                    GrowerName = rows.Select(x => x.GrowerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Name unavailable",
                    CurrentBins = totalBins,
                    CurrentLotCount = rows.Select(CurrentRoomLotIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CurrentRoomCount = rows.Select(x => x.RoomId).Distinct().Count(),
                    WeightedPressureLbs = RoundOrNull(WeightedStatistics.WeightedMean(pressureRows.Select(x => (x.AveragePressureLbs!.Value, (decimal)x.CurrentBins)))),
                    PressureRepresentedBins = pressureRows.Sum(x => x.CurrentBins),
                    WeightedStarch = RoundOrNull(WeightedStatistics.WeightedMean(starchRows.Select(x => (x.AverageStarch!.Value, (decimal)x.CurrentBins)))),
                    StarchRepresentedBins = starchRows.Sum(x => x.CurrentBins),
                    Varieties = BuildVarietyPresentations(rows, totalBins),
                    Lots = rows.Select(ToCurrentGrowerLot).ToList()
                };
            })
            .OrderBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<IReadOnlyDictionary<string, RoomLotProjectionDistribution>> BuildRoomProjectionSampleDataAsync(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        CancellationToken cancellationToken)
    {
        var targets = lots.Select(RoomLotQcIdentity).Where(x => x is not null).Select(x => x!).DistinctBy(x => x.LookupKey).ToList();
        if (targets.Count == 0)
        {
            return new Dictionary<string, RoomLotProjectionDistribution>(StringComparer.OrdinalIgnoreCase);
        }

        var candidateQuery = CanonicalQcFruitIdentity.FilterReceiptSamples(QuerySamples(), targets);
        var samples = await CanonicalQcFruitIdentity.OrderCandidates(
                candidateQuery,
                dbContext.Database.ProviderName)
            .Take(CanonicalQcFruitIdentity.CandidateLimit(targets.Count))
            .ToListAsync(cancellationToken);
        var sampleIdentities = samples.ToDictionary(
            x => x.Id,
            x => CanonicalQcFruitIdentity.FromReceipt(x.Receipt));

        var result = new Dictionary<string, RoomLotProjectionDistribution>(StringComparer.OrdinalIgnoreCase);
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
                result[target.LookupKey] = BuildRoomLotProjectionDistribution(latest);
            }
        }

        return result;
    }

    private static RoomLotProjectionDistribution BuildRoomLotProjectionDistribution(QcSample sample)
    {
        var gradeCounts = sample.FruitReadings
            .Where(x => x.Grade is not null)
            .GroupBy(x => x.Grade!.Code)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        return new RoomLotProjectionDistribution(ProjectionDistributionMath.BuildSizePercentages(sample.FruitReadings), Percentages(gradeCounts), sample.SampleTakenAt);
    }

    private static BinsRunProjectionViewModel BuildRoomProjection(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        IReadOnlyDictionary<string, RoomLotProjectionDistribution> sampleDistributions,
        bool isSelection)
    {
        var availableBins = lots.Sum(x => x.CurrentBins);
        var sizeRepresentedBins = lots
            .Where(x => sampleDistributions.TryGetValue(RoomProjectionLotKey(x), out var data) && data.SizeDistribution.Percentages.Count > 0)
            .Sum(x => x.CurrentBins);
        var gradeRepresentedBins = lots
            .Where(x => sampleDistributions.TryGetValue(RoomProjectionLotKey(x), out var data) && data.GradePercentages.Count > 0)
            .Sum(x => x.CurrentBins);

        return new BinsRunProjectionViewModel
        {
            IsSelection = isSelection,
            Label = isSelection
                ? $"Projected mix for {lots.Count} selected lot{(lots.Count == 1 ? "" : "s")}"
                : "Room baseline",
            LotCount = lots.Count,
            AvailableBins = availableBins,
            SizeDistribution = BuildRoomWeightedSizeDistribution(lots, sampleDistributions),
            GradeSummary = BuildRoomWeightedGradeSummary(lots, sampleDistributions),
            SizeDataLotCount = lots.Count(x => sampleDistributions.TryGetValue(RoomProjectionLotKey(x), out var data) && data.SizeDistribution.Percentages.Count > 0),
            GradeDataLotCount = lots.Count(x => sampleDistributions.TryGetValue(RoomProjectionLotKey(x), out var data) && data.GradePercentages.Count > 0),
            SizeRepresentedBins = sizeRepresentedBins,
            SizeMissingBins = Math.Max(0, availableBins - sizeRepresentedBins),
            SizeCoveragePercent = availableBins <= 0 ? 0m : decimal.Round(sizeRepresentedBins / (decimal)availableBins * 100m, 1),
            SizeUnclassifiedPercent = ProjectionDistributionMath.CombineWeightedUnclassifiedPercent(
                lots,
                sampleDistributions.ToDictionary(x => x.Key, x => x.Value.SizeDistribution, StringComparer.OrdinalIgnoreCase),
                RoomProjectionLotKey,
                lot => lot.CurrentBins),
            GradeRepresentedBins = gradeRepresentedBins,
            GradeMissingBins = Math.Max(0, availableBins - gradeRepresentedBins)
        };
    }

    private static IReadOnlyList<BinsRunSizeDistributionPoint> BuildRoomWeightedSizeDistribution(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        IReadOnlyDictionary<string, RoomLotProjectionDistribution> sampleDistributions)
    {
        var sizeData = sampleDistributions.ToDictionary(x => x.Key, x => x.Value.SizeDistribution, StringComparer.OrdinalIgnoreCase);
        return ProjectionDistributionMath.CombineWeightedSizePercentages(
            lots,
            sizeData,
            RoomProjectionLotKey,
            lot => lot.CurrentBins);
    }

    private static IReadOnlyList<BinsRunGradeSummaryPoint> BuildRoomWeightedGradeSummary(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        IReadOnlyDictionary<string, RoomLotProjectionDistribution> sampleDistributions)
    {
        var estimatedBinsByGrade = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var lot in lots)
        {
            if (!sampleDistributions.TryGetValue(RoomProjectionLotKey(lot), out var data))
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

    private static IReadOnlyList<RoomProjectionLotViewModel> BuildRoomProjectionLots(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        IReadOnlyDictionary<string, RoomLotProjectionDistribution> sampleDistributions,
        DateTimeOffset now)
    {
        return lots
            .OrderBy(x => x.GrowerName)
            .ThenBy(x => x.LotCode)
            .Select(lot =>
            {
                sampleDistributions.TryGetValue(RoomProjectionLotKey(lot), out var distribution);
                var indicators = new List<string>();
                if (distribution is null || distribution.SizeDistribution.Percentages.Count == 0) indicators.Add("Missing sizing");
                if (distribution is null || distribution.GradePercentages.Count == 0) indicators.Add("Missing grade");
                if (lot.SampleCount <= 0) indicators.Add("No QC samples");
                if (lot.LastSampleDate is DateTimeOffset sampleDate && (now - sampleDate).TotalDays >= 14) indicators.Add($"Sample is {(int)(now - sampleDate).TotalDays} days old");
                indicators.AddRange(lot.ReviewFlags.Take(2));
                if (!string.IsNullOrWhiteSpace(lot.WeakestReason)) indicators.Add(lot.WeakestReason!);

                return new RoomProjectionLotViewModel
                {
                    InventoryKey = RoomProjectionInventoryKey(lot),
                    Grower = lot.GrowerName,
                    Lot = !string.IsNullOrWhiteSpace(lot.GrowerNumber) ? lot.GrowerNumber : lot.LotCode,
                    Variety = lot.VarietyCode,
                    CurrentBins = lot.CurrentBins,
                    GradeSummary = distribution is null || distribution.GradePercentages.Count == 0 ? "No grade data" : FormatProjectionGradeSummary(distribution.GradePercentages),
                    LastSampleDate = distribution?.SampleTakenAt ?? lot.LastSampleDate,
                    Indicators = indicators.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<RoomSampleTimelineItemViewModel>> BuildRoomSampleTimelineAsync(int roomId, CancellationToken cancellationToken)
    {
        var samples = await QuerySamples()
            .Where(x => x.Receipt.RoomId == roomId)
            .OrderByDescending(x => x.SampleTakenAt)
            .Take(24)
            .ToListAsync(cancellationToken);

        return samples.Select(sample => new RoomSampleTimelineItemViewModel
        {
            SampleDate = sample.SampleTakenAt,
            Lot = ReceiptLotNumber(sample.Receipt),
            Variety = sample.Receipt.FruitProfile.VarietyCode,
            SampleType = sample.SampleType.Name,
            EnteredFruitCount = sample.FruitReadings.Count(HasEnteredFruitData),
            AveragePressureLbs = AverageOrNull(PressureValues([sample]).ToList()),
            AverageStarch = AverageOrNull(StarchValues([sample]).ToList()),
            SizeSummary = FormatSizeSummary(sample.FruitReadings.Where(x => x.SizeCategory is not null).Select(x => x.SizeCategory!.Value).ToList()),
            GradeSummary = FormatGradeSummary(sample.FruitReadings.Where(x => x.Grade is not null).Select(x => x.Grade!.Code).ToList())
        }).ToList();
    }

    private async Task<IReadOnlyList<RoomDepletionListItemViewModel>> BuildRoomDepletionHistoryAsync(int roomId, CancellationToken cancellationToken) =>
        await dbContext.RoomDepletions.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.FruitProfile)
            .Include(x => x.CreatedByUser)
            .Where(x => x.RoomId == roomId)
            .OrderByDescending(x => x.DepletedAt)
            .Select(x => new RoomDepletionListItemViewModel
            {
                Id = x.Id,
                ReceiptId = x.ReceiptId,
                DisplayReceiptId = x.Receipt.CompuTechReceiptId,
                Lot = $"{x.GrowerName} {x.LotCode}",
                Variety = x.FruitProfile.VarietyCode,
                BinCount = x.BinCountDepleted,
                Destination = x.Destination,
                Notes = x.Notes,
                DepletedAt = x.DepletedAt,
                CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName,
                IsVoided = x.IsVoided,
                VoidReason = x.VoidReason
            })
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<RoomInventoryAdjustmentListItemViewModel>> BuildRoomInventoryAdjustmentHistoryAsync(int roomId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Receipt)
            .Include(x => x.Room)
            .Include(x => x.CreatedByUser)
            .Include(x => x.RoomTransfer).ThenInclude(x => x!.SourceWarehouse)
            .Include(x => x.RoomTransfer).ThenInclude(x => x!.SourceRoom)
            .Include(x => x.RoomTransfer).ThenInclude(x => x!.DestinationWarehouse)
            .Include(x => x.RoomTransfer).ThenInclude(x => x!.DestinationRoom)
            .Where(x => x.RoomId == roomId)
            .OrderByDescending(x => x.AdjustmentAt)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new RoomInventoryAdjustmentListItemViewModel
        {
            Id = x.Id,
            ReceiptId = x.ReceiptId,
            Lot = $"{x.GrowerName} {x.LotNumber}",
            Room = x.Room.Code,
            OldBinCount = x.OldBinCount,
            ChangeAmount = x.ChangeAmount,
            NewBinCount = x.NewBinCount,
            AdjustmentType = x.AdjustmentType,
            Source = x.Source,
            Reason = x.Reason,
            Notes = x.Notes,
            TransferFrom = x.RoomTransfer is null ? null : TransferRoomLabel(x.RoomTransfer.SourceWarehouse, x.RoomTransfer.SourceRoom),
            TransferTo = x.RoomTransfer is null ? null : TransferRoomLabel(x.RoomTransfer.DestinationWarehouse, x.RoomTransfer.DestinationRoom),
            AdjustmentAt = x.AdjustmentAt,
            CreatedBy = x.CreatedByUser == null ? "" : x.CreatedByUser.DisplayName
        })
            .ToList();
    }

    private string TransferRoomLabel(Warehouse warehouse, Room room) =>
        $"{FacilityCode(warehouse.Code, warehouse.Name)} / {room.CropQcRoomName ?? room.DisplayName ?? room.Code}";

    private async Task<IReadOnlyList<ReceiptListItemViewModel>> BuildRoomLinkedReceiptsAsync(int roomId, CancellationToken cancellationToken)
    {
        var adjustmentReceiptIds = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.RoomId == roomId && x.ReceiptId != null)
            .Select(x => x.ReceiptId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Where(x => !x.IsDeleted && (x.RoomId == roomId || adjustmentReceiptIds.Contains(x.Id)))
            .OrderByDescending(x => x.ReceivedAt)
            .ThenBy(x => x.CompuTechReceiptId)
            .Take(100)
            .ToListAsync(cancellationToken);
        return receipts.Select(x => ReceiptListItem(x)).ToList();
    }

    private async Task<RoomTransferDestinationData> BuildRoomTransferDestinationsAsync(int currentRoomId, CancellationToken cancellationToken)
    {
        var warehouses = await dbContext.Warehouses.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);
        var knownFacilities = warehouses
            .Select(x => new RoomTransferFacilityViewModel(x.Id, FacilityCode(x.Code, x.Name), x.Code, x.Name))
            .Where(x => TransferFacilitySort(x.Label) < int.MaxValue)
            .OrderBy(x => TransferFacilitySort(x.Label))
            .ThenBy(x => x.WarehouseId)
            .ToList();

        var roomRows = await dbContext.Rooms.AsNoTracking()
            .Where(x => x.IsActive && x.Warehouse.IsActive)
            .Select(x => new
            {
                x.Id,
                x.WarehouseId,
                x.SortOrder,
                x.Code,
                x.CropQcRoomName,
                x.DisplayName
                ,
                x.IsSealed,
                x.SealedAt
            })
            .ToListAsync(cancellationToken);
        var sourceWarehouseId = roomRows.SingleOrDefault(x => x.Id == currentRoomId)?.WarehouseId ?? 0;
        var rooms = roomRows
            .Where(x => x.Id != currentRoomId && knownFacilities.Any(facility => facility.WarehouseId == x.WarehouseId))
            .OrderBy(x => TransferFacilitySort(knownFacilities.Single(facility => facility.WarehouseId == x.WarehouseId).Label))
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.CropQcRoomName ?? x.DisplayName ?? x.Code)
            .Select(x => new RoomTransferDestinationViewModel(
                x.Id,
                x.WarehouseId,
                x.CropQcRoomName ?? x.DisplayName ?? x.Code,
                x.SortOrder,
                x.IsSealed && (x.SealedAt == null || x.SealedAt <= BusinessTime.UtcNow)))
            .ToList();
        return new RoomTransferDestinationData(sourceWarehouseId, knownFacilities, rooms);
    }

    private static int TransferFacilitySort(string facility) => facility switch
    {
        "WP" => 0,
        "MCD" => 1,
        "DH" => 2,
        "EBS" => 3,
        _ => int.MaxValue
    };

    private sealed record RoomTransferDestinationData(
        int SourceWarehouseId,
        IReadOnlyList<RoomTransferFacilityViewModel> Facilities,
        IReadOnlyList<RoomTransferDestinationViewModel> Rooms);

    private async Task<Dictionary<int, int>> BuildStartingSeasonBinsByRoomAsync(CancellationToken cancellationToken)
    {
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType)
            .ToListAsync(cancellationToken);
        return ApplyLatestCurrentBalanceRows(adjustments)
            .GroupBy(x => x.RoomId)
            .ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.NewBinCount)));
    }

    private async Task<Dictionary<int, DateTimeOffset>> BuildLatestRoomActivityByRoomAsync(CancellationToken cancellationToken)
    {
        var adjustmentActivity = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .GroupBy(x => x.RoomId)
            .Select(x => new { RoomId = x.Key, LastAt = x.Max(y => y.AdjustmentAt) })
            .ToListAsync(cancellationToken);
        var receiptActivity = await dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.RoomId)
            .Select(x => new { RoomId = x.Key, LastAt = x.Max(y => y.UpdatedAt) })
            .ToListAsync(cancellationToken);
        return adjustmentActivity
            .Concat(receiptActivity)
            .GroupBy(x => x.RoomId)
            .ToDictionary(x => x.Key, x => x.Max(y => y.LastAt));
    }

    private static string BuildVarietyStatusSummary(IReadOnlyList<RoomLotSummaryViewModel> lots)
    {
        if (lots.Count == 0)
        {
            return "Empty";
        }

        return string.Join(", ", lots
            .GroupBy(x => string.IsNullOrWhiteSpace(x.InventoryStatus) ? x.VarietyCode : $"{x.VarietyCode} {x.InventoryStatus}")
            .OrderByDescending(x => x.Sum(y => y.CurrentBins))
            .ThenBy(x => x.Key)
            .Take(4)
            .Select(x => $"{x.Key}: {x.Sum(y => y.CurrentBins)} bins"));
    }

    private async Task<RoomTransferInventoryProjection> BuildTreatmentTransferProjectionAsync(
        IReadOnlyList<RoomLotSummaryViewModel> lots,
        int roomId,
        CancellationToken cancellationToken)
    {
        var snapshots = (await RoomInventoryLedger.GetSnapshotsAsync(null, [roomId], cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .ToList();
        var currentRoomBins = snapshots.Sum(x => x.CurrentBins);
        var snapshotsByDisplayKey = snapshots
            .GroupBy(x => LedgerLotKey(x.RoomId, x.CropYear, x.Lot, x.Variety, x.FruitProfileId), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (snapshotsByDisplayKey.Any(x => x.Count() != 1))
        {
            return RoomTransferInventoryProjection.Failed(currentRoomBins, 0);
        }

        var lotsByDisplayKey = lots
            .Where(x => x.CurrentBins > 0)
            .GroupBy(x => LedgerLotKey(x.RoomId, x.CropYear, x.LotCode, x.VarietyCode, x.FruitProfileId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var treatmentSelections = RoomTreatments is null
            ? null
            : await RoomTreatments.GetSelectionsAsync(snapshots, cancellationToken);
        var treatmentReceiptIds = treatmentSelections?.Values.SelectMany(x => x)
            .Where(x => x.ReceiptId is not null)
            .Select(x => x.ReceiptId!.Value)
            .Distinct()
            .ToList() ?? [];
        var treatmentReceiptLabels = treatmentReceiptIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Receipts.AsNoTracking()
                .Where(x => treatmentReceiptIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CompuTechReceiptId ?? $"Receipt #{x.Id}", cancellationToken);
        var entries = new List<RoomTransferInventoryEntry>();
        foreach (var snapshot in snapshots)
        {
            var displayKey = LedgerLotKey(snapshot.RoomId, snapshot.CropYear, snapshot.Lot, snapshot.Variety, snapshot.FruitProfileId);
            if (!lotsByDisplayKey.TryGetValue(displayKey, out var matchingLots) || matchingLots.Count != 1)
            {
                return RoomTransferInventoryProjection.Failed(currentRoomBins, entries.Sum(x => x.Option.CurrentBins));
            }

            var lot = matchingLots[0];
            IReadOnlyList<TreatmentSegmentSelection> segments;
            if (treatmentSelections is null)
            {
                segments = [new TreatmentSegmentSelection("", "", TreatmentLineageStates.Untreated, snapshot.CurrentBins, "Untreated")];
            }
            else if (!treatmentSelections.TryGetValue(RoomTreatmentService.SelectionLookupKey(snapshot), out segments!))
            {
                return RoomTransferInventoryProjection.Failed(currentRoomBins, entries.Sum(x => x.Option.CurrentBins));
            }
            if (segments.Any(x => x.CurrentBins <= 0) || segments.Sum(x => x.CurrentBins) != snapshot.CurrentBins)
            {
                return RoomTransferInventoryProjection.Failed(
                    currentRoomBins,
                    entries.Sum(x => x.Option.CurrentBins) + segments.Where(x => x.CurrentBins > 0).Sum(x => x.CurrentBins));
            }

            foreach (var segment in segments)
            {
                var sourceLabel = segment.ReceiptId is long receiptId
                    ? treatmentReceiptLabels.GetValueOrDefault(receiptId, $"Receipt #{receiptId}")
                    : "Shared inventory";
                var option = new RoomInventoryLotOptionViewModel(
                    RoomLotKey(lot),
                    $"{sourceLabel} - {lot.GrowerName} {lot.LotCode} {lot.VarietyCode} - {segment.Label} ({segment.CurrentBins} bins)",
                    segment.CurrentBins,
                    segment.TreatmentSignature,
                    segment.Label,
                    $"{lot.GrowerName} / {lot.LotCode}",
                    lot.VarietyCode,
                    segment.SegmentId,
                    segment.ReceiptId);
                entries.Add(new RoomTransferInventoryEntry(option, snapshot));
            }
        }

        var availableBins = entries.Sum(x => x.Option.CurrentBins);
        if (availableBins != currentRoomBins || lots.Sum(x => x.CurrentBins) != currentRoomBins)
        {
            return RoomTransferInventoryProjection.Failed(currentRoomBins, availableBins);
        }

        return new RoomTransferInventoryProjection(currentRoomBins, availableBins, true, null, entries);
    }

    private sealed record RoomTransferInventoryEntry(
        RoomInventoryLotOptionViewModel Option,
        RoomInventoryLedgerSnapshot Snapshot);

    private sealed record RoomTransferInventoryProjection(
        int CurrentRoomBins,
        int AvailableBins,
        bool Reconciles,
        string? Error,
        IReadOnlyList<RoomTransferInventoryEntry> Entries)
    {
        public IReadOnlyList<RoomInventoryLotOptionViewModel> Options => Reconciles
            ? Entries.Select(x => x.Option).ToList()
            : [];

        public static RoomTransferInventoryProjection Failed(int currentRoomBins, int availableBins) =>
            new(currentRoomBins, availableBins, false, TransferInventoryReconciliationError, []);
    }

    private static string RoomLotKey(RoomLotSummaryViewModel lot) =>
        lot.ReceiptId is not null
            ? $"R:{lot.ReceiptId.Value}"
            : $"A:{lot.InventoryAdjustmentId ?? 0}:{RoomInventoryImportService.CurrentStorageLotKey(lot.RoomId, lot.LotCode, lot.VarietyCode)}";

    private async Task<Dictionary<int, DateTimeOffset>> BuildCurrentBalanceCorrectionCutoffsAsync(int? roomId, CancellationToken cancellationToken)
    {
        var query = dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == null && x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType);
        if (roomId is not null)
        {
            query = query.Where(x => x.RoomId == roomId);
        }

        return await query
            .GroupBy(x => x.RoomId)
            .Select(x => new { RoomId = x.Key, Cutoff = x.Max(y => y.AdjustmentAt) })
            .ToDictionaryAsync(x => x.RoomId, x => x.Cutoff, cancellationToken);
    }

    private static bool IsCurrentBalanceCorrection(RoomInventoryAdjustment adjustment) =>
        adjustment.ReceiptId == null
        && adjustment.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType;

    private static string BreakdownSourceType(RoomInventoryAdjustment adjustment) =>
        IsCurrentBalanceCorrection(adjustment) ? "Current Inventory Baseline" : adjustment.AdjustmentType;

    private static bool IsAdjustmentOnlyCurrentStorageSource(RoomInventoryAdjustment adjustment) =>
        adjustment.ReceiptId == null
        || adjustment.AdjustmentType == "TransferIn";

    private static IEnumerable<RoomInventoryAdjustment> ApplyLatestCurrentBalanceRows(IEnumerable<RoomInventoryAdjustment> adjustments) =>
        adjustments
            .Where(x => !string.IsNullOrWhiteSpace(x.LotNumber))
            .GroupBy(x => RoomInventoryImportService.CurrentStorageLotKey(x.RoomId, x.LotNumber, x.VarietyCode ?? ""), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var latestEffectiveDate = group.Max(x => x.AdjustmentAt);
                var latestRows = group.Where(x => x.AdjustmentAt == latestEffectiveDate).ToList();
                var latestCreatedAt = latestRows.Max(x => x.CreatedAt);
                return latestRows.Where(x => x.CreatedAt == latestCreatedAt);
            });

    private static bool IsSupersededByRoomCurrentBalanceCorrection(Receipt receipt, IReadOnlyDictionary<int, DateTimeOffset> correctionCutoffs) =>
        correctionCutoffs.TryGetValue(receipt.RoomId, out var cutoff)
        && receipt.ReceivedAt <= cutoff;

    private static bool IsSupersededByRoomCurrentBalanceCorrection(RoomInventoryAdjustment adjustment, IReadOnlyDictionary<int, DateTimeOffset> correctionCutoffs) =>
        correctionCutoffs.TryGetValue(adjustment.RoomId, out var cutoff)
        && adjustment.AdjustmentAt < cutoff;

    private static string ReceiptDedupeKey(Receipt receipt) =>
        !string.IsNullOrWhiteSpace(receipt.CompuTechReceiptId)
            ? $"Receipt:{receipt.CompuTechReceiptId.Trim()}"
            : $"Lot:{receipt.RoomId}:{(receipt.GrowerNumber ?? receipt.LotCode).Trim()}:{receipt.FruitProfileId}:{receipt.BinCount}:{receipt.ReceivedAt:O}";

    private static int CurrentReceiptBins(
        Receipt receipt,
        IReadOnlyDictionary<long, int> depletionByReceipt,
        IReadOnlyDictionary<long, RoomInventoryAdjustment> latestAdjustmentByReceipt)
    {
        if (latestAdjustmentByReceipt.TryGetValue(receipt.Id, out var latestAdjustment))
        {
            return Math.Max(0, latestAdjustment.NewBinCount);
        }

        return Math.Max(0, receipt.BinCount - depletionByReceipt.GetValueOrDefault(receipt.Id));
    }

    private static string? ReceiptStorageExclusionReason(Receipt receipt, string sampleTypes, IReadOnlyDictionary<int, DateTimeOffset> correctionCutoffs)
    {
        if (receipt.IsDeleted)
        {
            return "Excluded: receipt is soft-deleted.";
        }

        if (HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, "LS"))
        {
            return "Excluded: LS prefix.";
        }

        if (HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, "DS"))
        {
            return "Excluded: DS prefix.";
        }

        if (!string.Equals(receipt.ReceiptType, "Truck receipt", StringComparison.OrdinalIgnoreCase))
        {
            return IsLotSampleLabel(receipt.ReceiptType)
                ? "Excluded: Lot Sample."
                : IsDoorSampleLabel(receipt.ReceiptType)
                    ? "Excluded: Door Sample."
                    : "Excluded: only Truck Receipt records add storage bins.";
        }

        if (ContainsLotSampleLabel(sampleTypes))
        {
            return "Excluded: Lot Sample.";
        }

        if (ContainsDoorSampleLabel(sampleTypes))
        {
            return "Excluded: Door Sample.";
        }

        if (IsSupersededByRoomCurrentBalanceCorrection(receipt, correctionCutoffs))
        {
            return "Excluded: superseded by the latest room current inventory baseline.";
        }

        return null;
    }

    private static string ReceiptBreakdownDecision(Receipt receipt, string sampleTypes, IReadOnlyDictionary<int, DateTimeOffset> correctionCutoffs, IReadOnlySet<long> includedReceiptIds)
    {
        var exclusionReason = ReceiptStorageExclusionReason(receipt, sampleTypes, correctionCutoffs);
        if (exclusionReason is not null)
        {
            return exclusionReason;
        }

        return includedReceiptIds.Contains(receipt.Id)
            ? "Included: Truck Receipt."
            : "Excluded: duplicate.";
    }

    private static bool HasStorageExcludedIdentifierPrefix(string? identifier, string prefix) =>
        !string.IsNullOrWhiteSpace(identifier)
        && identifier.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDoorSampleLabel(string sampleTypes) =>
        sampleTypes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(IsDoorSampleLabel);

    private static bool ContainsLotSampleLabel(string sampleTypes) =>
        sampleTypes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(IsLotSampleLabel);

    private static bool IsDoorSampleLabel(string value) =>
        value.Contains("Door Sample", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Door sample", StringComparison.OrdinalIgnoreCase);

    private static bool IsLotSampleLabel(string value) =>
        value.Contains("Lot Sample", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Lot sample", StringComparison.OrdinalIgnoreCase);

    private static string AdjustmentBreakdownDecision(RoomInventoryAdjustment adjustment, bool included, IReadOnlyDictionary<int, DateTimeOffset> correctionCutoffs)
    {
        if (!included && IsSupersededByRoomCurrentBalanceCorrection(adjustment, correctionCutoffs))
        {
            return "Excluded: superseded by the latest room current inventory baseline.";
        }

        if (included && IsCurrentBalanceCorrection(adjustment))
        {
            return "Included: current inventory baseline is authoritative for this room as of its effective date.";
        }

        if (included)
        {
            return "Included: latest adjustment/transfer row for this room lot.";
        }

        return IsAdjustmentOnlyCurrentStorageSource(adjustment)
            ? "Excluded: older adjustment for the same room lot; latest row is counted."
            : "Excluded: receipt-linked adjustment is applied through its receipt row.";
    }

    private async Task<int> GetCurrentBinsForReceiptAsync(long receiptId, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.AsNoTracking()
            .Where(x => x.Id == receiptId)
            .Select(x => new { x.ReceiptType, x.CompuTechReceiptId, x.IsDeleted, x.BinCount })
            .SingleAsync(cancellationToken);
        if (receipt.IsDeleted
            || HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, "LS")
            || HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, "DS")
            || !string.Equals(receipt.ReceiptType, "Truck receipt", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var latestAdjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId)
            .OrderByDescending(x => x.AdjustmentAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestAdjustment is not null)
        {
            return Math.Max(0, latestAdjustment.NewBinCount);
        }

        var depleted = await dbContext.RoomDepletions.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && !x.IsVoided)
            .SumAsync(x => (int?)x.BinCountDepleted, cancellationToken) ?? 0;
        return Math.Max(0, receipt.BinCount - depleted);
    }

    private RoomInventoryAdjustment AddRoomInventoryAdjustment(
        Receipt receipt,
        User? currentUser,
        string adjustmentType,
        int? oldBinCount,
        int changeAmount,
        int newBinCount,
        DateTimeOffset adjustmentAt,
        string? reason,
        string? notes,
        long? roomDepletionId,
        int? warehouseIdOverride = null,
        int? roomIdOverride = null)
    {
        var adjustment = new RoomInventoryAdjustment
        {
            CropYear = receipt.CropYear,
            ReceiptId = receipt.Id == 0 ? null : receipt.Id,
            RoomDepletionId = roomDepletionId,
            WarehouseId = warehouseIdOverride ?? receipt.WarehouseId,
            RoomId = roomIdOverride ?? receipt.RoomId,
            GrowerLotId = receipt.GrowerLotId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode,
            PoolStart = receipt.PoolStart,
            VarietyCode = receipt.FruitProfile?.VarietyCode,
            OldBinCount = oldBinCount,
            ChangeAmount = changeAmount,
            NewBinCount = Math.Max(0, newBinCount),
            AdjustmentType = adjustmentType,
            Source = reason,
            InventoryStatus = null,
            Reason = reason,
            Notes = notes,
            AdjustmentAt = adjustmentAt,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        return adjustment;
    }

    private RoomInventoryAdjustment AddRoomInventoryAdjustmentRaw(
        long? receiptId,
        int warehouseId,
        int roomId,
        int? growerLotId,
        int? fruitProfileId,
        string growerName,
        string lotNumber,
        string varietyCode,
        int? oldBinCount,
        int changeAmount,
        int newBinCount,
        string adjustmentType,
        DateTimeOffset adjustmentAt,
        User? currentUser,
        string? reason,
        string? notes)
    {
        var adjustment = new RoomInventoryAdjustment
        {
            CropYear = cropYearService.GetCurrentCropYear(adjustmentAt),
            ReceiptId = receiptId,
            RoomDepletionId = null,
            WarehouseId = warehouseId,
            RoomId = roomId,
            GrowerLotId = growerLotId,
            FruitProfileId = fruitProfileId,
            GrowerName = growerName,
            LotNumber = lotNumber,
            PoolStart = null,
            VarietyCode = varietyCode,
            OldBinCount = oldBinCount,
            ChangeAmount = changeAmount,
            NewBinCount = Math.Max(0, newBinCount),
            AdjustmentType = adjustmentType,
            Source = reason,
            InventoryStatus = null,
            Reason = reason,
            Notes = notes,
            AdjustmentAt = adjustmentAt,
            CreatedByUserId = currentUser?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        return adjustment;
    }

    private async Task<IDbContextTransaction?> BeginInventoryTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        return provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private async Task AuditRejectedInventoryDeductionAsync(
        string action,
        string entityKey,
        User? user,
        string detail,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = $"Rejected{action}",
            EntityName = nameof(RoomInventoryAdjustment),
            EntityKey = entityKey,
            UserId = user?.Id,
            AfterValuesJson = JsonSerializer.Serialize(new { Reason = detail }),
            SourceApplication = "Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WeakestLotResult? FindWeakestLot(IReadOnlyList<RoomLotSummaryViewModel> lots)
    {
        var candidates = lots
            .Where(x => x.CurrentBins > 0)
            .Select(BuildWeakestLotScore)
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Label)
            .ToList();
        return candidates.FirstOrDefault();
    }

    private static WeakestLotResult BuildWeakestLotScore(RoomLotSummaryViewModel lot)
    {
        var reasons = new List<string>();
        decimal score = 0;
        if (lot.AveragePressureLbs is decimal pressure)
        {
            score += Math.Max(0m, 30m - pressure);
            reasons.Add($"lowest pressure candidate {pressure:0.##} lbs");
        }

        if (lot.MonthOverMonthPressureChangeLbs is decimal change && change < 0)
        {
            score += Math.Abs(change) * 2m;
            reasons.Add($"pressure {change:0.##} lbs MoM");
        }

        if (lot.DefectSummary != "None")
        {
            score += 2m;
            reasons.Add($"defects: {lot.DefectSummary}");
        }

        if (lot.AverageStarch is decimal starch)
        {
            score += starch / 4m;
            reasons.Add($"starch {starch:0.##}");
        }

        if (lot.PressureStdDevLbs is decimal stdDev && stdDev > 0)
        {
            score += stdDev / 2m;
        }

        if (lot.EnteredFruitCount == 0)
        {
            return new(lot.ReceiptId, lot.InventoryAdjustmentId, $"{lot.GrowerName} {lot.LotCode}", 0, "No QC trend data yet");
        }

        return new(lot.ReceiptId, lot.InventoryAdjustmentId, $"{lot.GrowerName} {lot.LotCode} {lot.VarietyCode}", score, reasons.Count == 0 ? "No QC trend data yet" : string.Join("; ", reasons.Take(3)));
    }

    private sealed record WeakestLotResult(long? ReceiptId, long? InventoryAdjustmentId, string Label, decimal Score, string Reason);

    private RoomSummaryFilterForm NormalizeRoomSummaryFilter(RoomSummaryFilterForm? filter)
    {
        var facility = FacilityContext.Normalize(filter?.Facility);

        var ebsLocation = string.IsNullOrWhiteSpace(filter?.EbsLocation) ? "All EBS" : filter.EbsLocation.Trim();
        if (!new[] { "All EBS", "Evans", "Lamb", "BM" }.Contains(ebsLocation, StringComparer.OrdinalIgnoreCase))
        {
            ebsLocation = "All EBS";
        }

        var roomStatus = string.IsNullOrWhiteSpace(filter?.RoomStatus) ? "WithFruit" : filter.RoomStatus.Trim();
        if (!new[] { "WithFruit", "Empty", "All" }.Contains(roomStatus, StringComparer.OrdinalIgnoreCase))
        {
            roomStatus = "WithFruit";
        }

        return new RoomSummaryFilterForm
        {
            Facility = facility,
            EbsLocation = ebsLocation.Equals("All EBS", StringComparison.OrdinalIgnoreCase) ? "All EBS" : ebsLocation,
            RoomStatus = roomStatus.Equals("Empty", StringComparison.OrdinalIgnoreCase) ? "Empty" : roomStatus.Equals("All", StringComparison.OrdinalIgnoreCase) ? "All" : "WithFruit"
        };
    }

    private static IReadOnlyList<RoomSummaryItemViewModel> ApplyRoomStatusFilter(IReadOnlyList<RoomSummaryItemViewModel> summaries, string roomStatus) =>
        roomStatus switch
        {
            "Empty" => summaries.Where(x => x.CurrentLotsCount == 0 && (x.CurrentBinsCount ?? 0) == 0).ToList(),
            "All" => summaries,
            _ => summaries.Where(x => x.CurrentLotsCount > 0 || (x.CurrentBinsCount ?? 0) > 0).ToList()
        };

    private bool RoomMatchesFacilityFilter(Room room, string facility) =>
        FacilityContext.Matches(room.Warehouse.Code, room.Warehouse.Name, facility);

    private bool RoomMatchesEbsLocationFilter(Room room, string ebsLocation) =>
        !FacilityContext.GetFacilityCode(room.Warehouse.Code, room.Warehouse.Name).Equals("EBS", StringComparison.OrdinalIgnoreCase)
        || ebsLocation.Equals("All EBS", StringComparison.OrdinalIgnoreCase)
        || RoomLocationGroup(room).Equals(ebsLocation, StringComparison.OrdinalIgnoreCase);

    private string FacilityCode(string warehouseCode, string warehouseName) =>
        FacilityContext.GetFacilityCode(warehouseCode, warehouseName);

    private string RoomLocationGroup(Room room)
    {
        if (!string.IsNullOrWhiteSpace(room.SubLocation))
        {
            return room.SubLocation;
        }

        var combined = $"{room.Code} {room.Name} {room.CropQcRoomName} {room.CompuTechRoomCode}";
        if (combined.Contains("Evans", StringComparison.OrdinalIgnoreCase)) return "Evans";
        if (combined.Contains("Lamb", StringComparison.OrdinalIgnoreCase)) return "Lamb";
        if (combined.Contains("BM", StringComparison.OrdinalIgnoreCase) || combined.Contains("B M", StringComparison.OrdinalIgnoreCase)) return "BM";
        return FacilityCode(room.Warehouse.Code, room.Warehouse.Name).Equals("EBS", StringComparison.OrdinalIgnoreCase) ? "Other EBS" : "";
    }

    private decimal? MonthPressureChange(int roomId, bool currentMonth, IReadOnlyList<QcSample> alreadyLoadedSamples)
    {
        var now = DateTimeOffset.UtcNow;
        var currentStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var previousStart = currentStart.AddMonths(-1);
        var currentValues = PressureValues(alreadyLoadedSamples.Where(x => x.SampleTakenAt >= currentStart)).ToList();
        var previousValues = PressureValues(alreadyLoadedSamples.Where(x => x.SampleTakenAt >= previousStart && x.SampleTakenAt < currentStart)).ToList();
        if (currentValues.Count == 0 || previousValues.Count == 0)
        {
            return null;
        }

        return decimal.Round(currentValues.Average() - previousValues.Average(), 2);
    }

    private decimal? MonthPressureChange(int roomId, bool currentMonth, IReadOnlyList<RoomConditionSample> alreadyLoadedSamples)
    {
        var now = DateTimeOffset.UtcNow;
        var currentStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var previousStart = currentStart.AddMonths(-1);
        var currentValues = PressureValues(alreadyLoadedSamples.Where(x => x.SampleTakenAt >= currentStart)).ToList();
        var previousValues = PressureValues(alreadyLoadedSamples.Where(x => x.SampleTakenAt >= previousStart && x.SampleTakenAt < currentStart)).ToList();
        if (currentValues.Count == 0 || previousValues.Count == 0)
        {
            return null;
        }

        return decimal.Round(currentValues.Average() - previousValues.Average(), 2);
    }

    private IReadOnlyList<string> BuildRoomReviewFlags(IReadOnlyList<decimal> pressures, IReadOnlyList<decimal> starchValues, IReadOnlyList<QcFruitReading> rows, decimal? monthPressureChange)
    {
        var flags = new List<string>();
        var averagePressure = AverageOrNull(pressures);
        AddThresholdReason(flags, averagePressure, "DashboardReview:LowPressureLbs", threshold => averagePressure < threshold, threshold => $"Average pressure {averagePressure:0.##} lbs is below configured low threshold {threshold:0.##} lbs.");
        AddThresholdReason(flags, averagePressure, "DashboardReview:HighPressureLbs", threshold => averagePressure > threshold, threshold => $"Average pressure {averagePressure:0.##} lbs is above configured high threshold {threshold:0.##} lbs.");
        var averageStarch = AverageOrNull(starchValues);
        AddThresholdReason(flags, averageStarch, "DashboardReview:HighStarch", threshold => averageStarch > threshold, threshold => $"Average starch {averageStarch:0.##} is above configured threshold {threshold:0.##}.");
        var variance = StandardDeviationOrNull(pressures);
        AddThresholdReason(flags, variance, "DashboardReview:HighPressureVarianceLbs", threshold => variance > threshold, threshold => $"Pressure standard deviation {variance:0.##} lbs is above configured threshold {threshold:0.##} lbs.");
        var completedRows = rows.Where(x => x.IsCompleted).ToList();
        if (completedRows.Count > 0)
        {
            var defectPercent = decimal.Round(completedRows.Count(x => x.Defects.Count > 0) * 100m / completedRows.Count, 2);
            AddThresholdReason(flags, defectPercent, "DashboardReview:HighDefectPercent", threshold => defectPercent > threshold, threshold => $"Defects are present on {defectPercent:0.##}% of completed fruit, above configured threshold {threshold:0.##}%.");
        }

        if (monthPressureChange < 0)
        {
            AddThresholdReason(flags, Math.Abs(monthPressureChange.Value), "DashboardReview:PressureDropLbs", threshold => Math.Abs(monthPressureChange.Value) > threshold, threshold => $"Month-over-month pressure dropped {Math.Abs(monthPressureChange.Value):0.##} lbs, above configured drop threshold {threshold:0.##} lbs.");
        }

        return flags;
    }

    private IReadOnlyList<string> BuildRoomReviewFlags(IReadOnlyList<decimal> pressures, IReadOnlyList<decimal> starchValues, IReadOnlyList<RoomConditionFruitRow> rows, decimal? monthPressureChange)
    {
        var flags = new List<string>();
        var averagePressure = AverageOrNull(pressures);
        AddThresholdReason(flags, averagePressure, "DashboardReview:LowPressureLbs", threshold => averagePressure < threshold, threshold => $"Average pressure {averagePressure:0.##} lbs is below configured low threshold {threshold:0.##} lbs.");
        AddThresholdReason(flags, averagePressure, "DashboardReview:HighPressureLbs", threshold => averagePressure > threshold, threshold => $"Average pressure {averagePressure:0.##} lbs is above configured high threshold {threshold:0.##} lbs.");
        var averageStarch = AverageOrNull(starchValues);
        AddThresholdReason(flags, averageStarch, "DashboardReview:HighStarch", threshold => averageStarch > threshold, threshold => $"Average starch {averageStarch:0.##} is above configured threshold {threshold:0.##}.");
        var variance = StandardDeviationOrNull(pressures);
        AddThresholdReason(flags, variance, "DashboardReview:HighPressureVarianceLbs", threshold => variance > threshold, threshold => $"Pressure standard deviation {variance:0.##} lbs is above configured threshold {threshold:0.##} lbs.");
        var completedRows = rows.Where(x => x.IsCompleted).ToList();
        if (completedRows.Count > 0)
        {
            var defectPercent = decimal.Round(completedRows.Count(x => x.DefectNames.Count > 0) * 100m / completedRows.Count, 2);
            AddThresholdReason(flags, defectPercent, "DashboardReview:HighDefectPercent", threshold => defectPercent > threshold, threshold => $"Defects are present on {defectPercent:0.##}% of completed fruit, above configured threshold {threshold:0.##}%.");
        }

        if (monthPressureChange < 0)
        {
            AddThresholdReason(flags, Math.Abs(monthPressureChange.Value), "DashboardReview:PressureDropLbs", threshold => Math.Abs(monthPressureChange.Value) > threshold, threshold => $"Month-over-month pressure dropped {Math.Abs(monthPressureChange.Value):0.##} lbs, above configured drop threshold {threshold:0.##} lbs.");
        }

        return flags;
    }

    private static IEnumerable<decimal> PressureValues(IEnumerable<QcSample> samples) =>
        PressureCalculationService.ValidSideReadings(
            samples.SelectMany(x => x.FruitReadings).Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));

    private static IEnumerable<decimal> PressureValues(IEnumerable<RoomConditionSample> samples) =>
        PressureCalculationService.ValidSideReadings(
            samples.SelectMany(x => x.FruitRows).Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));

    private static IEnumerable<decimal> StarchValues(IEnumerable<QcSample> samples) =>
        samples.SelectMany(x => x.FruitReadings)
            .Where(x => x.StarchScaleValue is not null)
            .Select(x => x.StarchScaleValue!.Value);

    private static IEnumerable<decimal> StarchValues(IEnumerable<RoomConditionSample> samples) =>
        samples.SelectMany(x => x.FruitRows)
            .Where(x => x.Starch is not null)
            .Select(x => x.Starch!.Value);

    private static string SummarizeDefects(IEnumerable<QcSample> samples)
    {
        var groups = samples.SelectMany(x => x.FruitReadings)
            .SelectMany(x => x.Defects)
            .Select(x => x.DefectType.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x)
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();
        return groups.Count == 0 ? "None" : string.Join(", ", groups);
    }

    private static string SummarizeDefects(IEnumerable<RoomConditionSample> samples)
    {
        var groups = samples.SelectMany(x => x.FruitRows)
            .SelectMany(x => x.DefectNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x)
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();
        return groups.Count == 0 ? "None" : string.Join(", ", groups);
    }

    private static string SummarizeLotDefects(IEnumerable<RoomLotSummaryViewModel> lots)
    {
        var summaries = lots
            .Select(x => x.DefectSummary)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("None", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        return summaries.Count == 0 ? "None" : string.Join(", ", summaries);
    }

    private static decimal? AverageOrNull(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? null : decimal.Round(values.Average(), 2);

    private static decimal? RoundOrNull(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, 2);

    private static decimal? StandardDeviationOrNull(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var average = (double)values.Average();
        var sum = values.Sum(x => Math.Pow((double)x - average, 2));
        return decimal.Round((decimal)Math.Sqrt(sum / (values.Count - 1)), 2);
    }

    private static bool HasEnteredFruitData(QcFruitReading row) =>
        row.Pressure1Lbs is not null ||
        row.Pressure2Lbs is not null ||
        row.WeightGrams is not null ||
        row.GradeId is not null ||
        row.StarchScaleValueId is not null ||
        row.SizeCategory is not null ||
        row.Defects.Count > 0;

    private static bool HasEnteredFruitData(RoomConditionFruitRow row) =>
        row.Pressure1Lbs is not null ||
        row.Pressure2Lbs is not null ||
        row.WeightGrams is not null ||
        row.GradeId is not null ||
        row.StarchScaleValueId is not null ||
        row.SizeCategory is not null ||
        row.DefectNames.Count > 0;

    private Task<bool> CanEditSamplesAsync(CancellationToken cancellationToken) =>
        HasAccessAsync(ApplicationAreas.DailyQc, PageAccessLevel.Edit, cancellationToken);

    private Task<bool> HasAccessAsync(string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return Task.FromResult(false);
        }

        if (userAccessService is null)
        {
            return Task.FromResult(UserAccessService.IsOwner(user.FindFirstValue(ClaimTypes.Email)));
        }

        return userAccessService.HasAccessAsync(user, areaKey, minimumLevel, cancellationToken);
    }

    private async Task<IReadOnlyList<SampleType>> GetReceiptSampleTypesAsync(CancellationToken cancellationToken)
    {
        var sampleTypes = await dbContext.SampleTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return sampleTypes
            .Where(x => IsReceiptSampleTypeName(x.Name))
            .OrderBy(x => ReceiptSampleTypeSort(x.Name))
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static bool IsReceiptSampleTypeName(string name)
    {
        var normalized = NormalizeSampleTypeName(name);
        return normalized is "receiving sample" or "truck sample" or "door sample" or "lot sample";
    }

    private static int ReceiptSampleTypeSort(string name) => NormalizeSampleTypeName(name) switch
    {
        "receiving sample" or "truck sample" => 0,
        "door sample" => 1,
        "lot sample" => 2,
        _ => 99
    };

    private static string NormalizeSampleTypeName(string value)
    {
        var normalized = value.Trim().Replace("/", " ", StringComparison.OrdinalIgnoreCase);
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (normalized.Equals("Door Room Sample", StringComparison.OrdinalIgnoreCase))
        {
            return "door sample";
        }

        return normalized.ToLowerInvariant();
    }

    private IQueryable<QcSample> QuerySamples(bool includeDeleted = false)
    {
        var query = dbContext.QcSamples.AsNoTracking().Where(x => x.ReceiptId != null);
        if (!includeDeleted)
        {
            query = query.Where(x => !x.IsDeleted);
        }

        return query
            .Include(x => x.SampleType)
            .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt).ThenInclude(x => x.Room)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .Include(x => x.Receipt).ThenInclude(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
            .Include(x => x.Receipt).ThenInclude(x => x.Photos)
            .Include(x => x.TakenByUser)
            .Include(x => x.QcStation)
            .Include(x => x.Photos)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType);
    }

    private static FieldSampleQcStationStatusViewModel BuildQcStationStatus(QcStation? station)
    {
        if (station is null)
        {
            return new FieldSampleQcStationStatusViewModel
            {
                Message = "No QC Station has synchronized this sample. The Starch form and browser camera remain available."
            };
        }

        var lastContact = station.LastSyncAt ?? station.LastSeenAt;
        var connected = station.IsActive && lastContact is not null && lastContact >= DateTimeOffset.UtcNow.AddMinutes(-5);
        return new FieldSampleQcStationStatusViewModel
        {
            State = !station.IsActive ? "Error" : connected ? "Connected" : "Disconnected",
            Message = !station.IsActive
                ? $"{station.StationName} is inactive. The Starch form remains available; ask an administrator to reactivate the station."
                : connected
                    ? $"{station.StationName} recently synchronized this sample."
                    : $"{station.StationName} is not currently reporting. The Starch form remains available; reopen QC Station to retry.",
            StationCode = station.StationCode,
            StationName = station.StationName,
            LastSeenAt = station.LastSeenAt,
            LastSyncAt = station.LastSyncAt
        };
    }

    private async Task<IReadOnlyList<SampleListItemViewModel>> EnrichSamplesAsync(IReadOnlyList<QcSample> samples, CancellationToken cancellationToken)
    {
        var result = new List<SampleListItemViewModel>();
        var sampleIds = samples.Select(x => x.Id).ToList();
        var sentLogs = sampleIds.Count == 0
            ? new Dictionary<long, QcSummaryEmailSentInfo>()
            : (await dbContext.QcSummaryEmailLogs
                .AsNoTracking()
                .Include(x => x.SentByUser)
                .Where(x => x.QcSampleId != null
                    && sampleIds.Contains(x.QcSampleId.Value)
                    && x.Status == "Sent"
                    && x.SentAt != null)
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.QcSampleId!.Value)
                .ToDictionary(
                    x => x.Key,
                    x =>
                    {
                        var latest = x.OrderByDescending(y => y.SentAt).ThenByDescending(y => y.Id).First();
                        return new QcSummaryEmailSentInfo(latest.SentAt, latest.SentByUser?.DisplayName ?? latest.FromAddress);
                    });
        foreach (var sample in samples)
        {
            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
            var averagePressure = AveragePressure(sample.FruitReadings);
            sentLogs.TryGetValue(sample.Id, out var sentInfo);
            result.Add(new SampleListItemViewModel
            {
                Id = sample.Id,
                ReceiptId = sample.ReceiptId!.Value,
                CropYear = sample.Receipt.CropYear,
                ReceiptIdText = sample.Receipt.CompuTechReceiptId,
                DisplayReceiptId = sample.SampleSequenceNumber <= 1 ? sample.Receipt.CompuTechReceiptId : $"{sample.Receipt.CompuTechReceiptId}({sample.SampleSequenceNumber})",
                Warehouse = sample.Receipt.Warehouse.Code,
                SampleType = sample.SampleType.Name,
                Status = sample.Status,
                StarchStatus = sample.StarchStatus,
                PhotoStatus = sample.PhotoStatus,
                EmailStatus = sample.EmailStatus,
                EmailSentAt = sentInfo?.SentAt,
                EmailSentBy = sentInfo?.SentBy,
                TakenBy = sample.TakenByUser?.DisplayName,
                SampleTakenAt = sample.SampleTakenAt,
                ActualSampleSize = sample.ActualSampleSize,
                IsReady = readiness.IsReady,
                MissingItems = readiness.MissingItems,
                ReviewReasons = BuildReviewReasons(sample, averagePressure),
                Checklist = readiness.Checklist,
                CompletedFruitCount = readiness.CompletedFruitCount,
                AveragePressureLbs = averagePressure,
                IsDeleted = sample.IsDeleted
            });
        }

        return result;
    }

    private static bool IsReadyToEmail(SampleListItemViewModel sample) =>
        sample.IsReady
        && !sample.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase)
        && !sample.EmailStatus.Contains("resend", StringComparison.OrdinalIgnoreCase)
        && !sample.EmailStatus.Contains("Changed after sent", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> BuildReviewReasons(QcSample sample, decimal? averagePressureLbs)
    {
        var reasons = new List<string>();
        if (sample.Status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Sample is explicitly marked Needs Review.");
        }

        AddThresholdReason(reasons, averagePressureLbs, "DashboardReview:LowPressureLbs", value => averagePressureLbs < value, value => $"Average pressure {averagePressureLbs:0.##} lbs is below configured low threshold {value:0.##} lbs.");
        AddThresholdReason(reasons, averagePressureLbs, "DashboardReview:HighPressureLbs", value => averagePressureLbs > value, value => $"Average pressure {averagePressureLbs:0.##} lbs is above configured high threshold {value:0.##} lbs.");

        var starchValues = sample.FruitReadings
            .Where(x => x.StarchScaleValue is not null)
            .Select(x => x.StarchScaleValue!.Value)
            .ToList();
        var averageStarch = starchValues.Count == 0 ? (decimal?)null : decimal.Round(starchValues.Average(), 2);
        AddThresholdReason(reasons, averageStarch, "DashboardReview:HighStarch", value => averageStarch > value, value => $"Average starch {averageStarch:0.##} is above configured threshold {value:0.##}.");

        var completedRows = sample.FruitReadings.Where(x => x.IsCompleted).ToList();
        if (completedRows.Count > 0)
        {
            var defectRows = completedRows.Count(x => x.Defects.Count > 0);
            var defectPercent = decimal.Round(defectRows * 100m / completedRows.Count, 2);
            AddThresholdReason(reasons, defectPercent, "DashboardReview:HighDefectPercent", value => defectPercent > value, value => $"Defects are present on {defectPercent:0.##}% of completed fruit, above configured threshold {value:0.##}%.");
        }

        var pressureValues = PressureCalculationService.ValidSideReadings(
            sample.FruitReadings.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
        if (pressureValues.Count > 1)
        {
            var variance = decimal.Round(pressureValues.Max() - pressureValues.Min(), 2);
            AddThresholdReason(reasons, variance, "DashboardReview:HighPressureVarianceLbs", value => variance > value, value => $"Pressure variance {variance:0.##} lbs is above configured threshold {value:0.##} lbs.");
        }

        return reasons;
    }

    private void AddThresholdReason(List<string> reasons, decimal? actualValue, string key, Func<decimal, bool> isTriggered, Func<decimal, string> buildReason)
    {
        if (actualValue is null)
        {
            return;
        }

        var rawThreshold = configuration[key];
        if (decimal.TryParse(rawThreshold, out var threshold) && isTriggered(threshold))
        {
            reasons.Add(buildReason(threshold));
        }
    }

    private async Task<IReadOnlyList<FruitReadingRowViewModel>> GetFruitReadingRowsAsync(long sampleId, int targetSampleSize, CancellationToken cancellationToken)
    {
        var rows = await dbContext.QcFruitReadings.AsNoTracking()
            .Include(x => x.Grade)
            .Include(x => x.StarchScaleValue)
            .Include(x => x.Defects).ThenInclude(x => x.DefectType)
            .Where(x => x.QcSampleId == sampleId)
            .ToListAsync(cancellationToken);

        var rowCount = Math.Max(targetSampleSize, rows.Count == 0 ? 0 : rows.Max(x => x.RowNumber));
        return Enumerable.Range(1, rowCount)
            .Select(rowNumber =>
            {
                var row = rows.SingleOrDefault(x => x.RowNumber == rowNumber);
                return row is null
                    ? new FruitReadingRowViewModel { RowNumber = rowNumber, EntryStatus = FruitRowEntryStatus.Empty.ToDisplayName() }
                    : new FruitReadingRowViewModel
                    {
                        RowNumber = row.RowNumber,
                        Pressure1Lbs = row.Pressure1Lbs,
                        Pressure2Lbs = row.Pressure2Lbs,
                        PressureAverageLbs = Average(row.Pressure1Lbs, row.Pressure2Lbs),
                        WeightGrams = row.WeightGrams,
                        GradeId = row.GradeId,
                        Grade = row.Grade?.Code,
                        StarchScaleValueId = row.StarchScaleValueId,
                        Starch = row.StarchScaleValue?.Value.ToString("0.0"),
                        SizeCategory = row.SizeCategory,
                        SizeStatus = row.SizeStatus,
                        IsCompleted = row.IsCompleted,
                        EntryStatus = GetFruitRowEntryStatus(row).ToDisplayName(),
                        DefectTypeIds = row.Defects.Select(x => x.DefectTypeId).ToList(),
                        Defects = row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x).ToList(),
                        OtherDefectNotes = row.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes,
                        DefectsInspected = row.DefectsInspected,
                        FieldVersion = row.FieldVersion
                    };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<int>> GetAllowedSampleSizesAsync(CancellationToken cancellationToken)
    {
        var configured = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key == "AllowedSampleSizes")
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);
        var values = (configured ?? "10,25,50")
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var value) ? value : 0)
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        return values.Count == 0 ? [10, 25, 50] : values;
    }

    private static int ResolveTargetSampleSize(int? savedSampleSize, IReadOnlyList<int> allowedSampleSizes)
    {
        if (savedSampleSize is > 0)
        {
            return savedSampleSize.Value;
        }

        return allowedSampleSizes.Contains(10) ? 10 : allowedSampleSizes.FirstOrDefault(10);
    }

    private static bool HasEnteredData(FruitReadingRowViewModel row) =>
        row.Pressure1Lbs is not null ||
        row.Pressure2Lbs is not null ||
        row.WeightGrams is not null ||
        row.GradeId is not null ||
        row.StarchScaleValueId is not null ||
        row.SizeCategory is not null ||
        row.DefectsInspected ||
        row.DefectTypeIds.Count > 0 ||
        !string.IsNullOrWhiteSpace(row.OtherDefectNotes);

    public static FruitRowEntryStatus GetFruitRowEntryStatus(FruitReadingEditRow row, IReadOnlyCollection<int>? selectedDefectIds = null)
    {
        var hasAnyValue = row.Pressure1Lbs is not null
            || row.Pressure2Lbs is not null
            || row.WeightGrams is not null
            || row.GradeId is not null
            || row.StarchScaleValueId is not null
            || row.DefectsInspected
            || (selectedDefectIds ?? row.DefectTypeIds).Count > 0
            || !string.IsNullOrWhiteSpace(row.OtherDefectNotes);
        if (!hasAnyValue)
        {
            return FruitRowEntryStatus.Empty;
        }

        return HasCompletionFields(row.Pressure1Lbs, row.Pressure2Lbs, row.WeightGrams, row.GradeId)
            ? FruitRowEntryStatus.Complete
            : FruitRowEntryStatus.InProgress;
    }

    private static FruitRowEntryStatus GetFruitRowEntryStatus(QcFruitReading row)
    {
        var hasAnyValue = row.Pressure1Lbs is not null
            || row.Pressure2Lbs is not null
            || row.WeightGrams is not null
            || row.GradeId is not null
            || row.StarchScaleValueId is not null
            || row.DefectsInspected
            || row.Defects.Count > 0;
        if (!hasAnyValue)
        {
            return FruitRowEntryStatus.Empty;
        }

        return HasCompletionFields(row.Pressure1Lbs, row.Pressure2Lbs, row.WeightGrams, row.GradeId)
            ? FruitRowEntryStatus.Complete
            : FruitRowEntryStatus.InProgress;
    }

    private static bool HasCompletionFields(decimal? pressure1Lbs, decimal? pressure2Lbs, decimal? weightGrams, int? gradeId) =>
        pressure1Lbs is not null
        && pressure2Lbs is not null
        && weightGrams is not null
        && gradeId is not null;

    private async Task<FieldSampleAutosaveResult> ReceiptAutosaveResultAsync(
        QcSample sample,
        IReadOnlyList<FieldSampleAutosaveConflict> conflicts,
        IReadOnlyList<FieldSampleAutosaveValidationError> validation,
        CancellationToken cancellationToken)
    {
        var target = sample.ActualSampleSize ?? 10;
        var rows = await GetFruitReadingRowsAsync(sample.Id, target, cancellationToken);
        return new FieldSampleAutosaveResult
        {
            Saved = conflicts.Count == 0 && validation.Count == 0,
            SavedAt = conflicts.Count == 0 && validation.Count == 0 ? sample.UpdatedAt ?? DateTimeOffset.UtcNow : null,
            AutosaveVersion = sample.FieldSampleAutosaveVersion,
            Rows = rows.Select(row => new FieldSampleRefreshRowViewModel(
                row.RowNumber,
                row.Pressure1Lbs,
                row.Pressure2Lbs,
                row.PressureAverageLbs,
                row.WeightGrams,
                row.SizeCategory,
                row.StarchScaleValueId,
                row.GradeId,
                row.DefectsInspected,
                row.DefectTypeIds,
                row.OtherDefectNotes,
                row.FieldVersion)).ToList(),
            Conflicts = conflicts,
            ValidationErrors = validation
        };
    }

    public async Task<ReceiptReportPreviewViewModel> GetQcReportPreviewAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == sampleId, cancellationToken);
        if (sample is null)
        {
            return new ReceiptReportPreviewViewModel();
        }

        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
        var sender = await GetCurrentUserAsync(cancellationToken);
        var recipients = await qcEmailRecipientResolver.ResolveForSampleAsync(sample.Id, null, cancellationToken);
        var content = await emailComposer.ComposeAsync(sample, readiness, sender, false, null, cancellationToken);
        var history = await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .Where(x => x.QcSampleId == sample.Id)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new ReceiptReportSendHistoryItem(
                x.Status,
                x.SentAt,
                x.FromAddress,
                x.ToAddress,
                x.Subject,
                x.IsResend,
                x.IsOverride))
            .ToListAsync(cancellationToken);
        var isResend = sample.EmailStatus.Contains("resend", StringComparison.OrdinalIgnoreCase)
            || sample.EmailStatus.Contains("Changed after sent", StringComparison.OrdinalIgnoreCase);

        return new ReceiptReportPreviewViewModel
        {
            SampleId = sample.Id,
            ReceiptId = sample.ReceiptId.Value,
            DisplayReceiptId = sample.GetDisplayReceiptId(),
            Recipients = recipients.Header,
            Subject = content.Subject,
            HtmlBody = QcSummaryEmailComposer.BuildBrowserPreviewHtml(content),
            CanSend = readiness.IsReady && recipients.IsConfigured
                && (!sample.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase) || isResend),
            IsResend = isResend,
            MissingItems = readiness.MissingItems,
            SendHistory = history
        };
    }

    private static bool IsReceiptAutosaveField(string field) => field is
        "Pressure1Lbs" or "Pressure2Lbs" or "WeightGrams" or "StarchScaleValueId" or "GradeId"
        or "DefectsInspected" or "DefectTypeIds" or "OtherDefectNotes";

    private static string? ReceiptAutosaveValue(QcFruitReading? row, string field) => field switch
    {
        "Pressure1Lbs" => AutosaveText(row?.Pressure1Lbs),
        "Pressure2Lbs" => AutosaveText(row?.Pressure2Lbs),
        "WeightGrams" => AutosaveText(row?.WeightGrams),
        "StarchScaleValueId" => AutosaveText(row?.StarchScaleValueId),
        "GradeId" => AutosaveText(row?.GradeId),
        "DefectsInspected" => (row?.DefectsInspected ?? false).ToString().ToLowerInvariant(),
        "DefectTypeIds" => row is null ? "" : string.Join(",", row.Defects.Select(x => x.DefectTypeId).OrderBy(x => x)),
        "OtherDefectNotes" => row?.Defects.FirstOrDefault(x => x.DefectType?.Name == "Other")?.Notes,
        _ => null
    };

    private bool ApplyReceiptAutosaveChange(
        QcFruitReading reading,
        FieldSampleAutosaveFieldChange change,
        string source,
        IReadOnlySet<int> activeGradeIds,
        IReadOnlySet<int> activeStarchIds,
        IReadOnlySet<int> activeDefectIds,
        int? otherDefectId,
        ICollection<FieldSampleAutosaveValidationError> validation)
    {
        switch (change.Field)
        {
            case "Pressure1Lbs":
                if (!TryAutosaveDecimal(change.Value, out var pressure1) || pressure1 < 0) return Invalid("Enter a valid nonnegative pressure.");
                reading.Pressure1Lbs = pressure1;
                reading.Pressure1Source = pressure1 is null ? null : NormalizeAutosaveSource(source);
                return true;
            case "Pressure2Lbs":
                if (!TryAutosaveDecimal(change.Value, out var pressure2) || pressure2 < 0) return Invalid("Enter a valid nonnegative pressure.");
                reading.Pressure2Lbs = pressure2;
                reading.Pressure2Source = pressure2 is null ? null : NormalizeAutosaveSource(source);
                return true;
            case "WeightGrams":
                if (!TryAutosaveDecimal(change.Value, out var weight) || weight < 0) return Invalid("Enter a valid nonnegative weight in grams.");
                reading.WeightGrams = weight;
                return true;
            case "GradeId":
                if (!TryAutosaveInt(change.Value, out var gradeId) || gradeId is not null && !activeGradeIds.Contains(gradeId.Value)) return Invalid("Select an active grade.");
                reading.GradeId = gradeId;
                return true;
            case "StarchScaleValueId":
                if (!TryAutosaveInt(change.Value, out var starchId) || starchId is not null && !activeStarchIds.Contains(starchId.Value)) return Invalid("Select an active starch value.");
                reading.StarchScaleValueId = starchId;
                return true;
            case "DefectsInspected":
                if (!bool.TryParse(change.Value, out var inspected)) return Invalid("Defect inspection status is invalid.");
                if (!inspected && reading.Defects.Count > 0) return Invalid("Remove selected defects before marking the fruit not inspected.");
                reading.DefectsInspected = inspected;
                return true;
            case "DefectTypeIds":
                var ids = ParseAutosaveIds(change.Value);
                if (ids is null || ids.Any(id => !activeDefectIds.Contains(id))) return Invalid("Select only active defect types.");
                var existingNotes = reading.Defects.ToDictionary(x => x.DefectTypeId, x => x.Notes);
                dbContext.QcFruitDefects.RemoveRange(reading.Defects);
                reading.Defects.Clear();
                foreach (var id in ids)
                {
                    reading.Defects.Add(new QcFruitDefect { DefectTypeId = id, Notes = existingNotes.GetValueOrDefault(id) });
                }
                if (ids.Count > 0) reading.DefectsInspected = true;
                return true;
            case "OtherDefectNotes":
                if (change.Value?.Length > 500) return Invalid("Other defect notes must be 500 characters or fewer.");
                var other = otherDefectId is null ? null : reading.Defects.FirstOrDefault(x => x.DefectTypeId == otherDefectId.Value);
                if (other is null && !string.IsNullOrWhiteSpace(change.Value)) return Invalid("Select Other before entering other defect notes.");
                if (other is not null) other.Notes = string.IsNullOrWhiteSpace(change.Value) ? null : change.Value.Trim();
                return true;
            default:
                return Invalid("This fruit-row field cannot be autosaved.");
        }

        bool Invalid(string message)
        {
            validation.Add(new("row", reading.RowNumber, change.Field, message));
            return false;
        }
    }

    private static List<int>? ParseAutosaveIds(string? value)
    {
        var ids = new List<int>();
        foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
            ids.Add(id);
        }
        return ids.Distinct().OrderBy(x => x).ToList();
    }

    private static bool TryAutosaveDecimal(string? value, out decimal? parsed)
    {
        if (string.IsNullOrWhiteSpace(value)) { parsed = null; return true; }
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) { parsed = number; return true; }
        parsed = null;
        return false;
    }

    private static bool TryAutosaveInt(string? value, out int? parsed)
    {
        if (string.IsNullOrWhiteSpace(value)) { parsed = null; return true; }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) { parsed = number; return true; }
        parsed = null;
        return false;
    }

    private static bool ReceiptAutosaveValuesEqual(string? first, string? second)
    {
        var left = string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        var right = string.IsNullOrWhiteSpace(second) ? null : second.Trim();
        if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftDecimal)
            && decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? AutosaveText<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture);

    private static string NormalizeAutosaveSource(string? source) => source switch
    {
        "Scale" => "Scale",
        "Manual Save Now" => "Manual Save Now",
        "Conflict Resolution" => "Conflict Resolution",
        _ => "Browser"
    };

    private async Task<ReadinessViewModel> GetReadinessAsync(long sampleId, long receiptId, CancellationToken cancellationToken)
    {
        var sampleInfo = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.Id == sampleId)
            .Select(x => new { SampleTypeName = x.SampleType.Name, FruitType = x.Receipt!.FruitProfile.FruitType })
            .SingleOrDefaultAsync(cancellationToken);
        var sampleTypeName = sampleInfo?.SampleTypeName;
        var fruitType = sampleInfo?.FruitType;
        var completedRows = await dbContext.QcFruitReadings.AsNoTracking().Where(x => x.QcSampleId == sampleId && x.IsCompleted).ToListAsync(cancellationToken);
        var receiptPhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.ReceiptId == receiptId && !x.IsDeleted).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var samplePhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == sampleId && !x.IsDeleted).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var missing = new List<string>();
        var invalidRows = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null || x.WeightGrams is null || x.GradeId is null);
        var pressureMissing = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null);
        var weightMissing = completedRows.Count(x => x.WeightGrams is null);
        var gradeMissing = completedRows.Count(x => x.GradeId is null);
        var starchMissing = completedRows.Count(x => x.StarchScaleValueId is null);
        var starchRequired = IsStarchRequiredForEmail(sampleTypeName, fruitType);

        if (completedRows.Count == 0) missing.Add("At least one completed fruit row is required.");
        if (invalidRows > 0) missing.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        if (starchRequired && starchMissing > 0) missing.Add("Starch is required for all completed fruit rows.");
        var hasBinTruck = receiptPhotos.Contains("BinTruck");
        var hasSampleBeforeCutting = samplePhotos.Contains("SampleBeforeCutting");
        var hasCutFruit = samplePhotos.Contains("CutFruit");
        var hasFruitAfterStarch = samplePhotos.Contains("FruitAfterStarch");
        var requiredPhotoChecklist = photoRequirementPolicy.BuildChecklist(sampleTypeName, receiptPhotos, samplePhotos, fruitType);
        missing.AddRange(photoRequirementPolicy.MissingRequiredPhotos(sampleTypeName, receiptPhotos, samplePhotos, fruitType));

        var checklist = new List<ReadinessChecklistItem>
        {
            ChecklistItem("Required data", "At least one completed fruit row", completedRows.Count > 0, "Missing"),
            ChecklistItem("Required data", "Pressure 1 and Pressure 2 for every completed fruit row", completedRows.Count == 0 || pressureMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Weight for every completed fruit row", completedRows.Count == 0 || weightMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Grade for every completed fruit row", completedRows.Count == 0 || gradeMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            starchRequired
                ? ChecklistItem("Required data", "Starch for every completed fruit row", completedRows.Count == 0 || starchMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing")
                : new ReadinessChecklistItem("Required data", "Starch for every completed fruit row", "Optional", "pending")
        };
        checklist.AddRange(requiredPhotoChecklist);

        return new ReadinessViewModel
        {
            IsReady = missing.Count == 0,
            MissingItems = missing,
            Checklist = checklist,
            CompletedFruitCount = completedRows.Count,
            StarchMissingCount = starchMissing,
            HasBinTruck = hasBinTruck,
            HasSampleBeforeCutting = hasSampleBeforeCutting,
            HasCutFruit = hasCutFruit,
            HasFruitAfterStarch = hasFruitAfterStarch,
            RequiredPhotoChecklist = requiredPhotoChecklist
        };
    }

    private static bool IsStarchRequiredForEmail(string? sampleTypeName, string? fruitType)
    {
        if (string.Equals(fruitType, "Pear", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var normalized = sampleTypeName ?? string.Empty;
        return normalized.Contains("receiving", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("truck", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshSampleStatusesAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId!.Value, cancellationToken);
        sample.StarchStatus = readiness.CompletedFruitCount > 0 && readiness.StarchMissingCount == 0
            ? "Starch Complete"
            : "Starch Pending";
        sample.PhotoStatus = readiness.RequiredPhotoChecklist.All(x => x.Status == "Complete")
            ? "Photos Complete"
            : "Photo Pending";
        if (sample.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase))
        {
            sample.Status = "Sent";
        }
        else if (sample.EmailStatus.Contains("resend", StringComparison.OrdinalIgnoreCase)
            || sample.EmailStatus.Contains("Changed after sent", StringComparison.OrdinalIgnoreCase))
        {
            sample.Status = "Needs Resend";
        }
        else if (!sample.Status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase))
        {
            sample.Status = readiness.IsReady
                ? "Ready to Send"
                : readiness.StarchMissingCount > 0 ? "Starch Pending"
                : sample.PhotoStatus == "Photo Pending" ? "Photo Pending"
                : "Data Entry In Progress";
        }

        sample.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task MarkSampleNeedsResendIfSentAsync(QcSample sample, string reason, string changedBy, string? beforeValuesJson, CancellationToken cancellationToken)
    {
        if (!sample.EmailStatus.Equals("Sent", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        sample.EmailStatus = "Needs Resend";
        sample.Status = "Needs Resend";
        sample.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync(
            "changed-after-send",
            nameof(QcSample),
            sample.Id.ToString(),
            changedBy,
            beforeValuesJson,
            JsonSerializer.Serialize(new { sample.Id, sample.EmailStatus, Reason = reason }),
            cancellationToken);
    }

    private static string BuildFruitReadingSnapshot(QcSample sample, IEnumerable<QcFruitReading> rows) =>
        JsonSerializer.Serialize(new
        {
            sample.ActualSampleSize,
            Rows = rows
                .OrderBy(x => x.RowNumber)
                .Select(x => new
                {
                    x.RowNumber,
                    x.Pressure1Lbs,
                    x.Pressure2Lbs,
                    x.WeightGrams,
                    x.SizeCategory,
                    x.GradeId,
                    x.StarchScaleValueId,
                    x.DefectsInspected,
                    Defects = x.Defects
                        .OrderBy(y => y.DefectTypeId)
                        .Select(y => new { y.DefectTypeId, y.Notes })
                        .ToList()
                })
                .ToList()
        });

    private static string BuildStarchSnapshot(QcSample sample, IEnumerable<QcFruitReading> rows) =>
        JsonSerializer.Serialize(new
        {
            sample.ActualSampleSize,
            Rows = rows
                .OrderBy(x => x.RowNumber)
                .Select(x => new { x.RowNumber, x.StarchScaleValueId })
                .ToList()
        });

    private static (int? SizeCategory, string SizeStatus) CalculateSize(decimal? weightGrams, IEnumerable<FruitSizeConversionThreshold> thresholds)
    {
        if (weightGrams is null)
        {
            return (null, "NotCalculated");
        }

        var match = thresholds
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.MinimumWeightGrams)
            .FirstOrDefault(x => weightGrams.Value >= x.MinimumWeightGrams);
        return match is null ? (null, "Undersized") : (match.SizeCategory, "Sized");
    }

    private static ReceiptListItemViewModel ReceiptListItem(
        Receipt receipt,
        ReceiptSampleSummary? sampleSummary = null,
        IReadOnlyDictionary<string, VarietyColorResolved>? colors = null,
        CanonicalGrowerResolutionSet? growerResolver = null)
    {
        var identity = VarietyColorService.IdentityFromProfile(receipt.FruitProfile);
        var resolved = colors?.GetValueOrDefault(identity.Key);
        var presentation = new VarietyBinPresentationViewModel
        {
            CanonicalVarietyKey = identity.Key,
            DisplayName = resolved?.VarietyName ?? identity.Name,
            ProductionType = receipt.FruitProfile.ProductionType,
            IsOrganic = receipt.FruitProfile.IsOrganic,
            HexColor = resolved?.HexColor ?? VarietyColorService.FallbackColor(identity.Key),
            IsConfiguredColor = resolved?.IsConfigured == true,
            BinCount = receipt.BinCount,
            Percent = 100m
        };
        return new(
        receipt.Id,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.CompuTechReceiptId,
        NormalizeReceiptType(receipt.ReceiptType),
        receipt.Warehouse.Code,
        receipt.RoomId,
        receipt.Room.Code,
        receipt.GrowerNumber ?? "",
        receipt.PoolStart ?? "",
        growerResolver?.DisplayName(receipt.GrowerName, receipt.GrowerNumber ?? receipt.LotCode) ?? receipt.GrowerName,
        receipt.LotCode,
        receipt.FruitProfile.VarietyCode,
        receipt.BinCount,
        sampleSummary?.SampleCount ?? 0,
        BuildReceiptQcStatus(sampleSummary),
        sampleSummary?.LastUpdatedAt ?? receipt.UpdatedAt,
        receipt.FruitProfile.ProductionType,
        receipt.FruitProfile.IsOrganic,
        [presentation]);
    }

    private static IReadOnlyList<ReceiptTypeCountViewModel> BuildReceiptTypeCounts(ReceiptSearchForm search, IReadOnlyList<Receipt> receipts)
    {
        static string Url(ReceiptSearchForm form, string? receiptType)
        {
            var query = new List<string>();
            void Add(string name, object? value)
            {
                if (value is null)
                {
                    return;
                }

                var text = value.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(text)}");
            }

            Add(nameof(ReceiptSearchForm.Facility), form.Facility);
            Add(nameof(ReceiptSearchForm.CropYear), form.CropYear);
            if (form.AllCropYears)
            {
                Add(nameof(ReceiptSearchForm.AllCropYears), true);
            }
            Add(nameof(ReceiptSearchForm.DateFilter), form.DateFilter);
            Add(nameof(ReceiptSearchForm.SampleType), form.SampleType);
            Add(nameof(ReceiptSearchForm.ReceiptId), form.ReceiptId);
            Add(nameof(ReceiptSearchForm.Grower), form.Grower);
            Add(nameof(ReceiptSearchForm.Lot), form.Lot);
            Add(nameof(ReceiptSearchForm.WarehouseId), form.WarehouseId);
            Add(nameof(ReceiptSearchForm.RoomId), form.RoomId);
            Add(nameof(ReceiptSearchForm.FruitProfileId), form.FruitProfileId);
            Add(nameof(ReceiptSearchForm.ReceiptType), receiptType);
            return query.Count == 0 ? "/Receipts" : $"/Receipts?{string.Join("&", query)}";
        }

        int Count(string receiptType) => receipts.Count(x => string.Equals(x.ReceiptType, receiptType, StringComparison.OrdinalIgnoreCase));
        return
        [
            new("All", "All", receipts.Count, Url(search, null)),
            new("Truck receipt", "Truck Receipts", Count("Truck receipt"), Url(search, "Truck receipt")),
            new("Door sample", "Door Samples", Count("Door sample"), Url(search, "Door sample")),
            new("Lot sample", "Lot Samples", Count("Lot sample"), Url(search, "Lot sample"))
        ];
    }

    private static string BuildReceiptQcStatus(ReceiptSampleSummary? sampleSummary)
    {
        if (sampleSummary is null || sampleSummary.SampleCount == 0)
        {
            return "No samples";
        }

        if (sampleSummary.HasReview)
        {
            return "Needs Review";
        }

        if (sampleSummary.HasReady)
        {
            return "Ready to Send";
        }

        return sampleSummary.HasSent ? "Sent" : "In Progress";
    }

    private static decimal? AveragePressure(IEnumerable<QcFruitReading> rows)
    {
        return PressureCalculationService.CalculateOverallAverage(
            rows.Where(x => x.IsCompleted).Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
    }

    private sealed record ReceiptSampleSummary(long ReceiptId, int SampleCount, DateTimeOffset LastUpdatedAt, bool HasReady, bool HasReview, bool HasSent);
    private sealed record LatestSampleSummary(DateTimeOffset SampleDate, decimal? AveragePressure, decimal? AverageStarch);
    private sealed record QcSummaryEmailSentInfo(DateTimeOffset? SentAt, string? SentBy);

    private async Task<IReadOnlyDictionary<long, LatestSampleSummary>> BuildLatestSampleSummariesAsync(IReadOnlyList<long> receiptIds, CancellationToken cancellationToken)
    {
        if (receiptIds.Count == 0)
        {
            return new Dictionary<long, LatestSampleSummary>();
        }

        var samples = await QuerySamples()
            .Where(x => x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
            .ToListAsync(cancellationToken);
        return samples
            .GroupBy(x => x.ReceiptId!.Value)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var latest = x.OrderByDescending(y => y.SampleTakenAt).ThenByDescending(y => y.Id).First();
                    return new LatestSampleSummary(
                        latest.SampleTakenAt,
                        AverageOrNull(PressureValues([latest]).ToList()),
                        AverageOrNull(StarchValues([latest]).ToList()));
                });
    }

    private static string CurrentLotKey(RoomLotSummaryViewModel lot) =>
        $"{lot.GrowerName}|{lot.GrowerNumber}|{lot.VarietyCode}";

    private static string RoomProjectionInventoryKey(RoomLotSummaryViewModel lot) =>
        lot.ReceiptId is not null
            ? $"R:{lot.ReceiptId.Value}"
            : $"A:{lot.InventoryAdjustmentId}:{RoomProjectionLotKey(lot)}";

    private static string RoomProjectionLotKey(RoomLotSummaryViewModel lot) =>
        QcConditionLotKey(lot);

    private static string ReceiptLotNumber(Receipt receipt) =>
        !string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.GrowerNumber! : receipt.LotCode;

    private static string QcConditionLotKey(Receipt receipt) =>
        CanonicalQcFruitIdentity.FromReceipt(receipt)?.LookupKey ?? $"UNRESOLVED:RECEIPT:{receipt.Id}";

    private static string QcConditionLotKey(RoomLotSummaryViewModel lot) =>
        RoomLotQcIdentity(lot)?.LookupKey ?? $"UNRESOLVED:LOT:{lot.InventoryKey}";

    private static CanonicalQcFruitIdentity? RoomLotQcIdentity(RoomLotSummaryViewModel lot) =>
        CanonicalQcFruitIdentity.Create(
            lot.CropYear,
            lot.GrowerLotId,
            string.IsNullOrWhiteSpace(lot.GrowerNumber) ? lot.LotCode : lot.GrowerNumber,
            lot.LotCode,
            lot.FruitProfileId,
            lot.VarietyCode,
            lot.ProductionType,
            lot.IsOrganic);

    private static IReadOnlyDictionary<string, decimal> Percentages(IReadOnlyDictionary<string, int> counts)
    {
        var total = counts.Values.Sum();
        return total == 0
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : counts.ToDictionary(x => x.Key, x => x.Value / (decimal)total, StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatProjectionGradeSummary(IReadOnlyDictionary<string, decimal> gradePercentages) =>
        string.Join(", ", gradePercentages
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(3)
            .Select(x => $"{x.Key} {x.Value:P0}"));

    private static string FormatSizeSummary(IReadOnlyList<int> sizes)
    {
        if (sizes.Count == 0) return "No sizing data";
        return string.Join(", ", sizes
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => Array.IndexOf(ProjectionDistributionMath.SizeDisplayOrder, x.Key))
            .Take(3)
            .Select(x => $"{x.Key} ({x.Count()})"));
    }

    private static string FormatGradeSummary(IReadOnlyList<string> grades)
    {
        if (grades.Count == 0) return "No grade data";
        return string.Join(", ", grades
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Take(3)
            .Select(x => $"{x.Key} ({x.Count()})"));
    }

    private sealed record RoomLotProjectionDistribution(
        SizeSampleDistribution SizeDistribution,
        IReadOnlyDictionary<string, decimal> GradePercentages,
        DateTimeOffset SampleTakenAt);

    private async Task<DeviceCaptureSettingsViewModel> GetDeviceCaptureSettingsAsync(CancellationToken cancellationToken)
    {
        var values = await dbContext.DashboardConfigurations.AsNoTracking()
            .Where(x => x.Key.StartsWith("DeviceCapture__"))
            .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

        bool Read(string key)
        {
            if (values.TryGetValue($"DeviceCapture__{key}", out var configured))
            {
                return bool.TryParse(configured, out var parsed) && parsed;
            }

            return configuration.GetValue<bool>($"DeviceCapture:{key}");
        }

        return new DeviceCaptureSettingsViewModel(
            Read("Enabled"),
            Read("BrioEnabled"),
            Read("ObsbotEnabled"),
            Read("ScaleEnabled"));
    }

    private static string NormalizeReceiptType(string? receiptType)
    {
        var normalized = string.IsNullOrWhiteSpace(receiptType) ? "Truck receipt" : receiptType.Trim();
        return ReceiptTypeOptions.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? "Truck receipt";
    }

    private static bool IsInventoryReceiptType(string receiptType) =>
        NormalizeReceiptType(receiptType).Equals("Truck receipt", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<PhotoGroupViewModel> GroupPhotos(
        IReadOnlyList<QcPhoto> photos,
        bool canDelete,
        long? deleteFromSampleId = null,
        long? deleteFromReceiptId = null,
        bool canDeleteSamplePhotos = false) =>
        photos.Where(x => !x.IsDeleted)
            .GroupBy(x => QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType))
            .OrderBy(x => x.Key)
            .Select(group => new PhotoGroupViewModel(group.Key, group.Select(photo =>
            {
                var isSamplePhoto = photo.QcSampleId is not null;
                var deleteAction = deleteFromReceiptId is long receiptId
                    ? isSamplePhoto
                        ? canDeleteSamplePhotos ? $"/Samples/{photo.QcSampleId}/photos/{photo.Id}/remove" : null
                        : canDelete ? $"/Receipts/{receiptId}/photos/{photo.Id}/remove" : null
                    : deleteFromSampleId is long sampleId && canDelete
                        ? $"/Samples/{sampleId}/photos/{photo.Id}/remove"
                        : null;
                var isImage = photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var reviewContentUrl = deleteFromReceiptId is long contentReceiptId && isImage
                    ? $"/Receipts/{contentReceiptId}/photos/{photo.Id}/content"
                    : photo.WebUrl;
                return new PhotoMetadataViewModel(
                    photo.Id,
                    photo.QcSampleId,
                    deleteFromSampleId,
                    QcPhotoRequirementPolicy.NormalizePhotoType(photo.PhotoType),
                    photo.PhotoSource,
                    photo.FileName,
                    photo.ContentType,
                    photo.FileSizeBytes,
                    reviewContentUrl,
                    photo.CapturedAt,
                    deleteAction is not null,
                    deleteAction,
                    isImage && reviewContentUrl is not null,
                    isImage ? reviewContentUrl : null);
            }).ToList()))
            .ToList();

    private async Task<FileStorageReference> SavePhotoFileOrPlaceholderAsync(AddPhotoMetadataForm form, FileStorageTargetContext context, CancellationToken cancellationToken)
    {
        var targetPath = fileStorageService.GenerateTargetPath(context);
        logger.LogInformation(
            "Storage save started. Provider: {StorageProvider}. TargetPath: {TargetPath}. ReceiptId: {ReceiptId}. PhotoType: {PhotoType}. Uploaded file present: {HasFile}.",
            fileStorageOptions.Provider,
            targetPath,
            context.ReceiptId,
            form.PhotoType,
            form.PhotoFile is not null);

        if (form.PhotoFile is null)
        {
            if (string.Equals(form.PhotoSource, "Upload File", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("No photo file was selected.");
            }

            var placeholderName = GeneratePhotoFileName(context.ReceiptId, form.PhotoType, context.CapturedAt, ".jpg");
            var placeholderReference = new FileStorageReference(
                FileStorageProviders.Placeholder,
                $"{targetPath}/{placeholderName}",
                targetPath,
                placeholderName,
                string.IsNullOrWhiteSpace(form.ContentType) ? "image/jpeg" : form.ContentType.Trim(),
                form.FileSizeBytes ?? 0,
                FolderId: targetPath,
                WebUrl: $"{targetPath}/{placeholderName}");
            logger.LogInformation(
                "Storage save succeeded with placeholder reference. Provider: {StorageProvider}. FileId: {FileId}. FolderId: {FolderId}.",
                placeholderReference.StorageProvider,
                placeholderReference.FileId ?? placeholderReference.StorageKey,
                placeholderReference.FolderId ?? placeholderReference.TargetPath);
            return placeholderReference;
        }

        var extension = Path.GetExtension(form.PhotoFile.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = GeneratePhotoFileName(context.ReceiptId, form.PhotoType, context.CapturedAt, extension);
        await using var stream = form.PhotoFile.OpenReadStream();
        try
        {
            var reference = await fileStorageService.SaveAsync(new FileStorageSaveRequest(
                stream,
                targetPath,
                fileName,
                string.IsNullOrWhiteSpace(form.PhotoFile.ContentType) ? "application/octet-stream" : form.PhotoFile.ContentType,
                form.PhotoFile.Length), cancellationToken);
            logger.LogInformation(
                "Storage save succeeded. Provider: {StorageProvider}. FileId: {FileId}. FolderId: {FolderId}. WebUrlPresent: {WebUrlPresent}.",
                reference.StorageProvider,
                reference.FileId ?? reference.StorageKey,
                reference.FolderId ?? reference.TargetPath,
                !string.IsNullOrWhiteSpace(reference.WebUrl));
            return reference;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Storage save failed with exception message: {Message}. Provider: {StorageProvider}. TargetPath: {TargetPath}. FileName: {FileName}.",
                SafeErrorMessage(ex),
                fileStorageOptions.Provider,
                targetPath,
                fileName);
            throw;
        }
    }

    private async Task<int?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var email = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await dbContext.Users.AsNoTracking()
            .Where(x => x.Email == email)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private string? GetCurrentUserEmail() =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var email = GetCurrentUserEmail();
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task AddAuditAsync(string action, string entityName, string entityKey, string changedByEmail, string? before, string? after, CancellationToken ct)
    {
        var userId = await dbContext.Users.Where(x => x.Email == changedByEmail).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = userId,
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static string GeneratePhotoFileName(string receiptId, string photoType, DateTimeOffset capturedAt, string extension) =>
        $"{SanitizeFileName(receiptId)}_{SanitizeFileName(photoType)}_{capturedAt:yyyy-MM-dd_HHmmss}{extension}";

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch));
    }

    private static string? JoinWarnings(params string?[] warnings)
    {
        var messages = warnings.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    private string DatabaseWarning(Exception exception, string operation, string? safeMessage = null)
    {
        var diagnostic = DatabaseFailureDiagnostics.Classify(exception);
        var reference = Guid.NewGuid().ToString("N")[..8];
        logger.LogError(
            exception,
            "Database operation failed. Operation {Operation}; category {Category}; provider {Provider}; provider code {ProviderCode}; reference {Reference}.",
            operation,
            diagnostic.Category,
            dbContext.Database.ProviderName ?? "Unknown",
            diagnostic.ProviderCode ?? "None",
            reference);
        return $"{safeMessage ?? diagnostic.SafeMessage} Reference {reference}.";
    }

    private static string SafeErrorMessage(Exception exception)
    {
        var message = exception.Message;
        return string.IsNullOrWhiteSpace(message) ? "Unknown error." : message;
    }

    private string FormatStorageError(Exception exception)
    {
        var message = SafeErrorMessage(exception);
        if (string.Equals(message, "No photo file was selected.", StringComparison.Ordinal))
        {
            return message;
        }

        if (message.Contains("Service Accounts do not have storage quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not being treated as a Shared Drive upload target", StringComparison.OrdinalIgnoreCase))
        {
            return SharedDriveQuotaGuidance;
        }

        return string.Equals(fileStorageOptions.Provider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase)
            ? $"Google Drive upload failed: {message}"
            : $"Photo upload failed: {message}";
    }

    private static decimal? Average(decimal? first, decimal? second) =>
        first is null || second is null ? null : decimal.Round((first.Value + second.Value) / 2m, 2);

    private static decimal? AverageFlexible(decimal? first, decimal? second) => (first, second) switch
    {
        (decimal a, decimal b) => decimal.Round((a + b) / 2m, 2),
        (decimal a, null) => a,
        (null, decimal b) => b,
        _ => null
    };

    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static IReadOnlyList<string> Row(params string[] values) => values;
    private static ReadinessChecklistItem ChecklistItem(string category, string label, bool complete, string incompleteStatus) =>
        complete
            ? new(category, label, "Complete", "ready")
            : new(category, label, incompleteStatus, incompleteStatus == "Not applicable" ? "" : "missing");
    private static IReadOnlyList<(string Label, string Href)> MasterDataLinks() =>
    [
        ("Warehouses", "/MasterData/warehouses"),
        ("Rooms", "/MasterData/rooms"),
        ("Fruit profiles / variety codes", "/MasterData/fruit-profiles"),
        ("Grades", "/MasterData/grades"),
        ("Defects", "/MasterData/defects"),
        ("Sample types", "/MasterData/sample-types"),
        ("Grower Lots", "/MasterData/grower-lots"),
        ("Starch scale values", "/MasterData/starch-scale-values"),
        ("Size thresholds", "/MasterData/size-thresholds")
    ];

    private sealed record CanonicalVarietyFilter(
        string CanonicalKey,
        List<int> FruitProfileIds,
        List<string> RawVarietyCodes)
    {
        public bool Matches(RoomInventoryLedgerSnapshot snapshot) =>
            snapshot.FruitProfileId is int fruitProfileId && FruitProfileIds.Contains(fruitProfileId)
            || string.Equals(
                VarietyColorService.NormalizeIdentity(snapshot.VarietyName, snapshot.Variety).Key,
                CanonicalKey,
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DashboardInventorySnapshot(
        int RoomId,
        int? CropYear,
        int? FruitProfileId,
        string Warehouse,
        string Facility,
        string LocationGroup,
        string Room,
        string Grower,
        string GrowerNumber,
        int? GrowerLotId,
        string Lot,
        string Variety,
        string VarietyKey,
        string VarietyName,
        string ProductionType,
        bool? IsOrganic,
        string InventoryStatus,
        int CurrentBins,
        DateTimeOffset? ReceiptDate)
    {
        public CanonicalQcFruitIdentity? QcIdentity { get; } = CanonicalQcFruitIdentity.Create(
            CropYear,
            GrowerLotId,
            GrowerNumber.Length == 0 ? Lot : GrowerNumber,
            Lot,
            FruitProfileId,
            Variety,
            ProductionType,
            IsOrganic);

        public string QcIdentityKey => QcIdentity?.LookupKey
            ?? $"UNRESOLVED:{RoomId}:{GrowerNumber}:{Lot}:{Variety}";
    }


    private sealed record DashboardQcSampleAggregate(
        long SampleId,
        decimal? AveragePressureLbs,
        decimal? AverageStarch);

    private sealed record DashboardQcSampleHeader(
        long Id,
        int? GrowerLotId,
        string GrowerNumber,
        string LotNumber,
        int FruitProfileId,
        int CropYear,
        string VarietyCode,
        string ProductionType,
        bool IsOrganic,
        DateTimeOffset SampleTakenAt,
        string SampleType)
    {
        public CanonicalQcFruitIdentity? Identity { get; } = CanonicalQcFruitIdentity.Create(
            CropYear,
            GrowerLotId,
            GrowerNumber,
            LotNumber,
            FruitProfileId,
            VarietyCode,
            ProductionType,
            IsOrganic);
    }

    private sealed record DashboardSampleSummaryRow(
        long Id,
        long ReceiptId,
        int CropYear,
        string ReceiptIdText,
        int SampleSequenceNumber,
        string Warehouse,
        string SampleType,
        string FruitType,
        string Status,
        string StarchStatus,
        string PhotoStatus,
        string EmailStatus,
        string? TakenBy,
        DateTimeOffset SampleTakenAt,
        int? ActualSampleSize,
        bool IsDeleted);

    private sealed record DashboardSampleFruitRow(
        long SampleId,
        decimal? Pressure1Lbs,
        decimal? Pressure2Lbs,
        decimal? WeightGrams,
        int? GradeId,
        int? StarchScaleValueId,
        decimal? Starch,
        bool IsCompleted,
        bool HasDefects);

    private sealed record DashboardSampleFruitAggregate(
        long SampleId,
        int CompletedRows,
        int InvalidRows,
        int PressureMissingRows,
        int WeightMissingRows,
        int GradeMissingRows,
        int StarchMissingRows,
        int CompletedDefectRows,
        decimal? AveragePressureLbs,
        decimal? MinimumPressureLbs,
        decimal? MaximumPressureLbs,
        decimal? AverageStarch)
    {
        public static DashboardSampleFruitAggregate Empty(long sampleId) =>
            new(sampleId, 0, 0, 0, 0, 0, 0, 0, null, null, null, null);
    }

    private sealed record DashboardQcSample(
        long SampleId,
        DateTimeOffset SampleTakenAt,
        string SampleType,
        decimal? AveragePressureLbs,
        decimal? AverageStarch);

    private sealed record RoomQcSummary(
        int TotalBins,
        decimal? ReceivingStarch,
        int ReceivingStarchRepresentedBins,
        int ReceivingStarchMissingBins,
        decimal? ReceivingPressureLbs,
        int ReceivingPressureRepresentedBins,
        int ReceivingPressureMissingBins,
        decimal? LatestPressureLbs,
        DateTimeOffset? LatestPressureDate,
        int LatestPressureRepresentedBins,
        int LatestPressureMissingBins,
        decimal? PressureChange30DayLbs,
        int PressureChangeRepresentedBins,
        int PressureChangeMissingBins,
        decimal? LatestPressureStandardDeviationLbs,
        int PressureStandardDeviationRepresentedBins,
        int PressureReadingCount)
    {
        public static RoomQcSummary Empty(int totalBins) =>
            new(totalBins, null, 0, totalBins, null, 0, totalBins, null, null, 0, totalBins, null, 0, totalBins, null, 0, 0);
    }

    private sealed record DashboardSampleMarker(DateTimeOffset SampleTakenAt, string SampleType);

    private sealed record RoomConditionSampleHeader(
        long Id,
        long ReceiptId,
        int RoomId,
        int CropYear,
        int? GrowerLotId,
        int FruitProfileId,
        string GrowerNumber,
        string LotCode,
        string VarietyCode,
        string ProductionType,
        bool IsOrganic,
        string DisplayReceiptId,
        int SampleSequenceNumber,
        string SampleType,
        DateTimeOffset SampleTakenAt)
    {
        public CanonicalQcFruitIdentity? Identity { get; } = CanonicalQcFruitIdentity.Create(
            CropYear,
            GrowerLotId,
            GrowerNumber,
            LotCode,
            FruitProfileId,
            VarietyCode,
            ProductionType,
            IsOrganic);
    }

    private sealed record RoomConditionFruitRowData(
        long Id,
        long SampleId,
        decimal? Pressure1Lbs,
        decimal? Pressure2Lbs,
        decimal? WeightGrams,
        int? GradeId,
        int? StarchScaleValueId,
        decimal? Starch,
        int? SizeCategory,
        bool IsCompleted);

    private sealed record RoomConditionFruitRow(
        decimal? Pressure1Lbs,
        decimal? Pressure2Lbs,
        decimal? WeightGrams,
        int? GradeId,
        int? StarchScaleValueId,
        decimal? Starch,
        int? SizeCategory,
        bool IsCompleted,
        IReadOnlyList<string> DefectNames);

    private sealed record RoomConditionSample(
        long Id,
        long ReceiptId,
        int RoomId,
        int CropYear,
        int? GrowerLotId,
        int FruitProfileId,
        string GrowerNumber,
        string LotCode,
        string VarietyCode,
        string ProductionType,
        bool IsOrganic,
        string DisplayReceiptId,
        int SampleSequenceNumber,
        string SampleType,
        DateTimeOffset SampleTakenAt,
        IReadOnlyList<RoomConditionFruitRow> FruitRows)
    {
        public CanonicalQcFruitIdentity? Identity { get; } = CanonicalQcFruitIdentity.Create(
            CropYear,
            GrowerLotId,
            GrowerNumber,
            LotCode,
            FruitProfileId,
            VarietyCode,
            ProductionType,
            IsOrganic);
    }

    private sealed record RoomConditionData(
        decimal? AveragePressureLbs,
        decimal? PressureStdDevLbs,
        decimal? MonthOverMonthPressureChangeLbs,
        decimal? AverageStarch,
        string DefectSummary,
        DateTimeOffset LastSampleDate,
        string LatestQcSource,
        int SampleCount,
        int EnteredFruitCount,
        IReadOnlyList<string> ReviewFlags,
        IReadOnlyList<RoomSampleLinkViewModel> SampleEvidence,
        int SampleEvidenceCount,
        IReadOnlyList<RoomReceiptEvidenceLinkViewModel> ReceiptEvidence,
        int ReceiptEvidenceCount,
        IReadOnlySet<long> ReceiptIds);

    private sealed record DashboardRoomAttention(
        string Category,
        int Sort,
        string Reason,
        string Indicator,
        string? Flag,
        string? WeakestLotLabel);
}
