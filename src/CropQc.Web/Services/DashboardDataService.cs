using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CropQc.Web.Services;

public interface IDashboardDataService
{
    Task<HomeDashboardViewModel> GetHomeDashboardAsync(CancellationToken cancellationToken);
    Task<MasterDataPageViewModel> GetMasterDataPageAsync(string type, CancellationToken cancellationToken);
    Task<ReceiptListViewModel> SearchReceiptsAsync(ReceiptSearchForm search, CancellationToken cancellationToken);
    Task<string?> CreateReceiptAsync(CreateReceiptForm form, CancellationToken cancellationToken);
    Task<ReceiptDetailViewModel> GetReceiptDetailAsync(long id, CancellationToken cancellationToken);
    Task<(long? SampleId, int? SampleSequenceNumber, string? Warning, string? Error)> CreateReceivingSampleAsync(long receiptId, CancellationToken cancellationToken);
    Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken);
    Task<DeleteSampleConfirmationViewModel> GetDeleteSampleConfirmationAsync(long id, CancellationToken cancellationToken);
    Task<(long? ReceiptId, string? Error)> SoftDeleteSampleAsync(long id, string? reason, CancellationToken cancellationToken);
    Task<string?> SaveFruitReadingsAsync(SaveFruitReadingsForm form, CancellationToken cancellationToken);
    Task<StarchTestViewModel> GetStarchTestAsync(long id, CancellationToken cancellationToken);
    Task<string?> SaveStarchTestAsync(SaveStarchTestForm form, CancellationToken cancellationToken);
    Task<OverrideSendViewModel> GetOverrideSendAsync(long id, CancellationToken cancellationToken);
    Task<string?> SendQcSummaryAsync(long sampleId, CancellationToken cancellationToken);
    Task<string?> LogOverrideSendAsync(OverrideSendForm form, CancellationToken cancellationToken);
    Task<string?> AddPhotoMetadataAsync(AddPhotoMetadataForm form, CancellationToken cancellationToken);
    Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, CancellationToken cancellationToken);
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
    GoogleAuthenticationOptions authOptions,
    IGoogleCredentialStore googleCredentialStore,
    IQcEmailSender emailSender,
    IQcPhotoRequirementPolicy photoRequirementPolicy,
    IQcSummaryEmailComposer emailComposer,
    ICropYearService cropYearService,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<DashboardDataService> logger) : IDashboardDataService
{
    private const string DataWarning = "Database is not available yet. The dashboard shell is running with empty data.";
    private const string SharedDriveQuotaGuidance = "The configured Google Drive folder is not being treated as a Shared Drive upload target. Confirm GoogleDrive__UseSharedDrive=true, GoogleDrive__RootFolderId is a folder inside the Shared Drive, GoogleDrive__SharedDriveId is set, and the service account has Content Manager access.";

    public async Task<HomeDashboardViewModel> GetHomeDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            var todaySamples = await QuerySamples().Where(x => x.SampleTakenAt.Date == DateTimeOffset.UtcNow.Date).ToListAsync(cancellationToken);
            var enriched = await EnrichSamplesAsync(todaySamples, cancellationToken);
            var cards = BuildHomeCards(
                enriched.Count(x => x.SampleType.Contains("Receiving", StringComparison.OrdinalIgnoreCase)),
                enriched.Count(x => x.IsReady),
                enriched.Count(x => !x.IsReady),
                enriched.Count(x => x.EmailStatus == "Sent"),
                enriched.Count(x => x.ReviewReasons.Count > 0));
            return new HomeDashboardViewModel
            {
                Cards = cards,
                TodaySamples = enriched
            };
        }
        catch
        {
            return new HomeDashboardViewModel
            {
                DataWarning = DataWarning,
                Cards = BuildHomeCards(0, 0, 0, 0, 0)
            };
        }
    }

    private IReadOnlyList<StatusCountCard> BuildHomeCards(int todaySamples, int ready, int missing, int sent, int review)
    {
        var cards = new List<StatusCountCard>
        {
            new("Today's Receiving Samples", todaySamples, "/Receipts?DateFilter=today&SampleType=Receiving", "info", "Receipts with receiving QC activity today."),
            new("Samples Ready to Email", ready, "/DailyQc?status=ReadyToSend", "ready", "Samples with required data and photos ready for QC summary email."),
            new("Samples Missing Data", missing, "/DailyQc?status=MissingData", "missing", "Samples that are saved but missing required fields/photos for completion or email."),
            new("Samples Already Sent", sent, "/DailyQc?status=Sent", "sent", "Samples that already have a QC summary email recorded."),
            new("Samples Needing Review", review, "/DailyQc?status=NeedsReview", "review", "Samples with review flags such as pressure/starch/defect/variance thresholds.")
        };

        var user = httpContextAccessor.HttpContext?.User;
        if (user?.IsInRole("Admin") == true || user?.IsInRole("Manager") == true)
        {
            cards.Add(new("Master Data/Admin Links", 8, "/MasterData", "admin", "Management pages available to your role."));
        }

        return cards;
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
        catch
        {
            return new MasterDataPageViewModel("Master data", DataWarning, ["Page"], MasterDataLinks().Select(x => Row(x.Label)).ToList());
        }
    }

    public async Task<ReceiptListViewModel> SearchReceiptsAsync(ReceiptSearchForm search, CancellationToken cancellationToken)
    {
        try
        {
            search.CropYear ??= cropYearService.GetCurrentCropYear(DateTimeOffset.Now);
            if (search.WarehouseId is not null && search.RoomId is not null)
            {
                var roomMatchesWarehouse = await dbContext.Rooms.AsNoTracking()
                    .AnyAsync(x => x.Id == search.RoomId && x.WarehouseId == search.WarehouseId, cancellationToken);
                if (!roomMatchesWarehouse)
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
            if (!search.AllCropYears && search.CropYear is not null) query = query.Where(x => x.CropYear == search.CropYear);
            if (string.Equals(search.DateFilter, "today", StringComparison.OrdinalIgnoreCase))
            {
                var today = DateTimeOffset.UtcNow.Date;
                query = query.Where(x => x.ReceivedAt.Date == today);
            }

            if (!string.IsNullOrWhiteSpace(search.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(search.ReceiptId));
            if (!string.IsNullOrWhiteSpace(search.Grower)) query = query.Where(x => x.GrowerName.Contains(search.Grower));
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

            var receipts = await query.OrderByDescending(x => x.ReceivedAt).Take(500).ToListAsync(cancellationToken);
            var receiptIds = receipts.Select(x => x.Id).ToList();
            var sampleSummaries = await dbContext.QcSamples.AsNoTracking()
                .Where(x => receiptIds.Contains(x.ReceiptId) && !x.IsDeleted)
                .GroupBy(x => x.ReceiptId)
                .Select(x => new ReceiptSampleSummary(
                    x.Key,
                    x.Count(),
                    x.Max(s => s.UpdatedAt ?? s.CreatedAt),
                    x.Any(s => s.Status == "Ready to Send"),
                    x.Any(s => s.Status.Contains("Needs Review")),
                    x.Any(s => s.EmailStatus == "Sent")))
                .ToDictionaryAsync(x => x.ReceiptId, cancellationToken);
            return new ReceiptListViewModel
            {
                Search = search,
                Receipts = receipts.Select(receipt => ReceiptListItem(receipt, sampleSummaries.GetValueOrDefault(receipt.Id))).ToList(),
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Rooms = await dbContext.Rooms.AsNoTracking().OrderBy(x => x.WarehouseId).ThenBy(x => x.Code).ToListAsync(cancellationToken),
                FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                AvailableCropYears = await cropYearService.GetAvailableCropYearsAsync(cancellationToken),
                CurrentCropYear = cropYearService.GetCurrentCropYear(DateTimeOffset.Now),
                CropYearHelpText = "Crop years use the starting-year convention by default: CropYear 2026 starts 2026-08-01 and ends 2027-07-31. Confirm crop year when season dates overlap."
            };
        }
        catch
        {
            return new ReceiptListViewModel { Search = search, DataWarning = DataWarning };
        }
    }

    public async Task<string?> CreateReceiptAsync(CreateReceiptForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.CompuTechReceiptId) || string.IsNullOrWhiteSpace(form.GrowerName) || string.IsNullOrWhiteSpace(form.LotCode) || form.BinCount <= 0)
        {
            return "Receipt ID, grower, lot, and bin count are required.";
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

        if (cropYearService.RequiresConfirmation(form.ReceivedAt, form.CropYear) && !form.ConfirmCropYear)
        {
            var candidates = string.Join(", ", cropYearService.GetCandidateCropYears(form.ReceivedAt));
            return $"Confirm Crop Year before saving. Suggested crop year option(s) for this received date: {candidates}.";
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.Receipts.Add(new Receipt
        {
            CropYear = form.CropYear,
            ReceivedAt = form.ReceivedAt,
            CompuTechReceiptId = form.CompuTechReceiptId.Trim(),
            WarehouseId = form.WarehouseId,
            RoomId = form.RoomId,
            FruitProfileId = form.FruitProfileId,
            GrowerName = form.GrowerName.Trim(),
            LotCode = form.LotCode.Trim(),
            BinCount = form.BinCount,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
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

            var samples = await QuerySamples().Where(x => x.ReceiptId == id).OrderBy(x => x.SampleTakenAt).ThenBy(x => x.SampleSequenceNumber).ToListAsync(cancellationToken);
            var photos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.ReceiptId == id).OrderByDescending(x => x.CapturedAt).ToListAsync(cancellationToken);
            return new ReceiptDetailViewModel
            {
                Receipt = ReceiptListItem(receipt),
                Samples = await EnrichSamplesAsync(samples, cancellationToken),
                PhotoGroups = GroupPhotos(photos),
                CanDeleteSamples = httpContextAccessor.HttpContext?.User.IsInRole("Admin") == true,
                AddPhotoForm = new AddPhotoMetadataForm
                {
                    ReceiptId = receipt.Id,
                    PhotoType = "BinTruck",
                    PhotoSource = "Upload File",
                    ContentType = "image/jpeg"
                }
            };
        }
        catch
        {
            return new ReceiptDetailViewModel { DataWarning = DataWarning };
        }
    }

    public async Task<(long? SampleId, int? SampleSequenceNumber, string? Warning, string? Error)> CreateReceivingSampleAsync(long receiptId, CancellationToken cancellationToken)
    {
        var receiptExists = await dbContext.Receipts.AnyAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (!receiptExists)
        {
            return (null, null, null, "Receipt not found.");
        }

        var receivingSampleType = await dbContext.SampleTypes
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Name == "Receiving Sample", cancellationToken);
        if (receivingSampleType is null)
        {
            return (null, null, null, "Receiving Sample type is not configured.");
        }

        var existingReceivingSampleCount = await dbContext.QcSamples
            .CountAsync(x => x.ReceiptId == receiptId && x.SampleTypeId == receivingSampleType.Id && !x.IsDeleted, cancellationToken);
        var nextSequenceNumber = existingReceivingSampleCount + 1;
        var now = DateTimeOffset.UtcNow;
        var sample = new QcSample
        {
            ReceiptId = receiptId,
            SampleTypeId = receivingSampleType.Id,
            SampleSequenceNumber = nextSequenceNumber,
            Status = nextSequenceNumber > 1 ? "Needs Review" : "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Photo Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.QcSamples.Add(sample);
        await dbContext.SaveChangesAsync(cancellationToken);

        var warning = nextSequenceNumber > 1
            ? "A receiving sample already exists for this receipt. The new sample was created with the next sequence number and marked Needs Review."
            : null;
        return (sample.Id, nextSequenceNumber, warning, null);
    }

    public async Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (sample is null)
            {
                return new SampleDetailViewModel { DataWarning = "QC sample not found." };
            }

            var rowModels = await GetFruitReadingRowsAsync(id, cancellationToken);

            var photos = await dbContext.QcPhotos.AsNoTracking()
                .Where(x => x.QcSampleId == id && (x.PhotoType == "Hectre" || x.PhotoType == "SampleBeforeCutting" || x.PhotoType == "CutFruit" || x.PhotoType == "Other"))
                .OrderByDescending(x => x.CapturedAt)
                .ToListAsync(cancellationToken);
            var grades = await dbContext.Grades.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(cancellationToken);
            var defectTypes = await dbContext.DefectTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
            return new SampleDetailViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                FruitRows = rowModels,
                PhotoGroups = GroupPhotos(photos),
                Readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken),
                RecipientEmail = emailOptions.QcRecipientHeader,
                Grades = grades,
                DefectTypes = defectTypes,
                FruitReadingForm = new SaveFruitReadingsForm
                {
                    SampleId = sample.Id,
                    Rows = rowModels.Select(row => new FruitReadingEditRow
                    {
                        RowNumber = row.RowNumber,
                        Pressure1Lbs = row.Pressure1Lbs,
                        Pressure2Lbs = row.Pressure2Lbs,
                        WeightGrams = row.WeightGrams,
                        GradeId = row.GradeId,
                        DefectTypeIds = row.DefectTypeIds.ToList(),
                        OtherDefectNotes = row.OtherDefectNotes
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
        catch
        {
            return new SampleDetailViewModel { DataWarning = DataWarning };
        }
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
            ReceiptId = sample.ReceiptId,
            CropYear = sample.Receipt.CropYear,
            DisplayReceiptId = sample.GetDisplayReceiptId(),
            Warehouse = sample.Receipt.Warehouse.Code,
            GrowerName = sample.Receipt.GrowerName,
            LotCode = sample.Receipt.LotCode,
            VarietyCode = sample.Receipt.FruitProfile.VarietyCode,
            SampleType = sample.SampleType.Name,
            PhotoCount = sample.Photos.Count,
            EmailStatus = sample.EmailStatus
        };
    }

    public async Task<(long? ReceiptId, string? Error)> SoftDeleteSampleAsync(long id, string? reason, CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.IsInRole("Admin") != true)
        {
            return (null, "Only Admin users can delete QC samples.");
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
        var before = System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.ReceiptId, sample.Status, sample.EmailStatus, PhotoCount = sample.Photos.Count });
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
            System.Text.Json.JsonSerializer.Serialize(new { sample.Id, sample.ReceiptId, sample.IsDeleted, sample.DeletedAt, sample.DeleteReason, PhotoCount = sample.Photos.Count }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (sample.ReceiptId, null);
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

        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key is < 1 or > 25) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return "Rows must be unique and numbered 1 through 25.";
        }

        var validGradeIds = await dbContext.Grades.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
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

        foreach (var submittedRow in form.Rows.OrderBy(x => x.RowNumber))
        {
            var selectedDefectIds = submittedRow.DefectTypeIds.Distinct().ToList();
            if (submittedRow.GradeId is not null && !validGradeIds.Contains(submittedRow.GradeId.Value))
            {
                return $"Row {submittedRow.RowNumber} has an invalid grade.";
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
            reading.GradeId = submittedRow.GradeId;
            reading.SizeCategory = size.SizeCategory;
            reading.SizeStatus = size.SizeStatus;
            reading.IsCompleted = isCompleted;
            reading.UpdatedAt = DateTimeOffset.UtcNow;

            dbContext.QcFruitDefects.RemoveRange(reading.Defects);
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
        await RefreshSampleStatusesAsync(sample, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
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

            var rowModels = await GetFruitReadingRowsAsync(id, cancellationToken);
            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
            var photos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == id && (x.PhotoType == "FruitAfterStarch" || x.PhotoType == "Other")).OrderByDescending(x => x.CapturedAt).ToListAsync(cancellationToken);
            return new StarchTestViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                FruitRows = rowModels,
                StarchScaleValues = await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken),
                Readiness = readiness,
                PhotoGroups = GroupPhotos(photos),
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
        catch
        {
            return new StarchTestViewModel { DataWarning = DataWarning };
        }
    }

    public async Task<string?> SaveStarchTestAsync(SaveStarchTestForm form, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.SingleOrDefaultAsync(x => x.Id == form.SampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var rowsByNumber = form.Rows.GroupBy(x => x.RowNumber).ToList();
        if (rowsByNumber.Any(x => x.Key is < 1 or > 25) || rowsByNumber.Any(x => x.Count() > 1))
        {
            return "Rows must be unique and numbered 1 through 25.";
        }

        var validStarchIds = await dbContext.StarchScaleValues.AsNoTracking().Select(x => x.Id).ToHashSetAsync(cancellationToken);
        if (form.Rows.Any(x => x.StarchScaleValueId is not null && !validStarchIds.Contains(x.StarchScaleValueId.Value)))
        {
            return "One or more starch values are invalid.";
        }

        var existingRows = await dbContext.QcFruitReadings
            .Where(x => x.QcSampleId == sample.Id)
            .ToListAsync(cancellationToken);
        foreach (var submittedRow in form.Rows.OrderBy(x => x.RowNumber))
        {
            var reading = existingRows.SingleOrDefault(x => x.RowNumber == submittedRow.RowNumber);
            if (reading is null || !reading.IsCompleted)
            {
                continue;
            }

            reading.StarchScaleValueId = submittedRow.StarchScaleValueId;
            reading.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
            var sender = await GetCurrentUserAsync(cancellationToken);
            var senderEmail = sender?.Email ?? GetCurrentUserEmail();
            var senderDomain = GoogleAuthenticationOptions.GetEmailDomain(senderEmail);
            var credentialDiagnostic = sender is null
                ? new GoogleCredentialDiagnostic(false, false)
                : await googleCredentialStore.GetDiagnosticAsync(sender, cancellationToken);

            return new OverrideSendViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                Readiness = readiness,
                Checklist = readiness.Checklist,
                SenderEmail = senderEmail,
                SenderDomain = senderDomain,
                SenderDomainAllowed = senderDomain is not null && authOptions.AllowedDomains.Contains(senderDomain),
                RecipientEmail = emailOptions.QcRecipientHeader,
                GmailReconnectRequired = !string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase)
                    || !credentialDiagnostic.GmailSendPermissionGranted,
                GmailCredentialPresent = credentialDiagnostic.CredentialPresent,
                GmailSendPermissionGranted = credentialDiagnostic.GmailSendPermissionGranted,
                GmailUserProviderEnabled = string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase),
                AllowedGoogleDomains = string.Join(", ", authOptions.AllowedDomains.OrderBy(x => x)),
                Form = new OverrideSendForm { SampleId = sample.Id }
            };
        }
        catch
        {
            return new OverrideSendViewModel { DataWarning = DataWarning };
        }
    }

    public async Task<string?> SendQcSummaryAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await QuerySamples().SingleOrDefaultAsync(x => x.Id == sampleId, cancellationToken);
        if (sample is null)
        {
            return "QC sample not found.";
        }

        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
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

        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
        return await SendAndLogQcSummaryAsync(sample, readiness, isOverride: true, overrideReason: form.OverrideReason.Trim(), cancellationToken);
    }

    private async Task<string?> SendAndLogQcSummaryAsync(QcSample sample, ReadinessViewModel readiness, bool isOverride, string? overrideReason, CancellationToken cancellationToken)
    {
        var sender = await GetCurrentUserAsync(cancellationToken);
        if (sender is null)
        {
            return "A logged-in user is required to send QC Summary email.";
        }

        var emailContent = await emailComposer.ComposeAsync(sample, readiness, sender, isOverride, overrideReason, cancellationToken);
        var recipients = emailOptions.QcRecipientHeader;
        var message = new QcEmailMessage(sender.Email, recipients, sample.TakenByUser?.Email, emailContent.Subject, emailContent.TextBody, emailContent.HtmlBody, emailContent.InlineImages);

        var now = DateTimeOffset.UtcNow;
        var sendResult = await emailSender.SendAsync(sender, message, cancellationToken);
        var status = sendResult.Success ? "Sent" : "Failed";

        dbContext.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            ReceiptId = sample.ReceiptId,
            QcSampleId = sample.Id,
            FromAddress = sender.Email,
            ToAddress = recipients,
            ReplyToAddress = sample.TakenByUser?.Email,
            Subject = emailContent.Subject,
            Status = status,
            MessageId = sendResult.MessageId,
            SentByUserId = sender.Id,
            SentAt = sendResult.Success ? now : null,
            IsResend = false,
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

        receipt ??= sample?.Receipt;
        if (receipt is null)
        {
            return "Receipt context is required for photo storage.";
        }

        var capturedAt = DateTimeOffset.UtcNow;
        FileStorageReference reference;
        try
        {
            reference = await SavePhotoFileOrPlaceholderAsync(form, receipt, capturedAt, cancellationToken);
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
            PhotoType = form.PhotoType.Trim(),
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
        logger.LogInformation(
            "QcPhoto metadata insert succeeded. QcPhotoId: {QcPhotoId}. StorageProvider: {StorageProvider}. FileId: {FileId}. FolderId: {FolderId}.",
            photo.Id,
            photo.StorageProvider,
            photo.FileId ?? photo.SharePointItemId,
            photo.FolderId ?? photo.SharePointDriveId);
        if (sample is not null)
        {
            await RefreshSampleStatusesAsync(sample, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (form.ReceiptId is not null)
        {
            var receiptSamples = await dbContext.QcSamples.Where(x => x.ReceiptId == form.ReceiptId).ToListAsync(cancellationToken);
            foreach (var receiptSample in receiptSamples)
            {
                await RefreshSampleStatusesAsync(receiptSample, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    public async Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, string? status, CancellationToken cancellationToken)
    {
        try
        {
            var query = QuerySamples().Where(x => x.SampleTakenAt.Date == DateTimeOffset.UtcNow.Date);
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
                Status = status,
                StatusDescription = BuildDailyQcStatusDescription(status),
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Samples = enriched
            };
        }
        catch
        {
            return new DailyQcDashboardViewModel { WarehouseId = warehouseId, Status = status, DataWarning = DataWarning };
        }
    }

    private static IReadOnlyList<SampleListItemViewModel> FilterDailyQcSamples(IReadOnlyList<SampleListItemViewModel> samples, string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "readytosend" => samples.Where(x => x.IsReady).ToList(),
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

    private IQueryable<QcSample> QuerySamples(bool includeDeleted = false)
    {
        var query = dbContext.QcSamples.AsNoTracking();
        if (!includeDeleted)
        {
            query = query.Where(x => !x.IsDeleted);
        }

        return query
            .Include(x => x.SampleType)
            .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt).ThenInclude(x => x.Room)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .Include(x => x.Receipt).ThenInclude(x => x.Photos)
            .Include(x => x.TakenByUser)
            .Include(x => x.Photos)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType);
    }

    private async Task<IReadOnlyList<SampleListItemViewModel>> EnrichSamplesAsync(IReadOnlyList<QcSample> samples, CancellationToken cancellationToken)
    {
        var result = new List<SampleListItemViewModel>();
        foreach (var sample in samples)
        {
            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
            var averagePressure = AveragePressure(sample.FruitReadings);
            result.Add(new SampleListItemViewModel
            {
                Id = sample.Id,
                ReceiptId = sample.ReceiptId,
                CropYear = sample.Receipt.CropYear,
                ReceiptIdText = sample.Receipt.CompuTechReceiptId,
                DisplayReceiptId = sample.SampleSequenceNumber <= 1 ? sample.Receipt.CompuTechReceiptId : $"{sample.Receipt.CompuTechReceiptId}({sample.SampleSequenceNumber})",
                Warehouse = sample.Receipt.Warehouse.Code,
                SampleType = sample.SampleType.Name,
                Status = sample.Status,
                StarchStatus = sample.StarchStatus,
                PhotoStatus = sample.PhotoStatus,
                EmailStatus = sample.EmailStatus,
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

        var pressureValues = sample.FruitReadings
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

    private async Task<IReadOnlyList<FruitReadingRowViewModel>> GetFruitReadingRowsAsync(long sampleId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.QcFruitReadings.AsNoTracking()
            .Include(x => x.Grade)
            .Include(x => x.StarchScaleValue)
            .Include(x => x.Defects).ThenInclude(x => x.DefectType)
            .Where(x => x.QcSampleId == sampleId)
            .ToListAsync(cancellationToken);

        return Enumerable.Range(1, 25)
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
                        OtherDefectNotes = row.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes
                    };
            })
            .ToList();
    }

    public static FruitRowEntryStatus GetFruitRowEntryStatus(FruitReadingEditRow row, IReadOnlyCollection<int>? selectedDefectIds = null)
    {
        var hasAnyValue = row.Pressure1Lbs is not null
            || row.Pressure2Lbs is not null
            || row.WeightGrams is not null
            || row.GradeId is not null
            || row.StarchScaleValueId is not null
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

    private async Task<ReadinessViewModel> GetReadinessAsync(long sampleId, long receiptId, CancellationToken cancellationToken)
    {
        var sampleTypeName = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.Id == sampleId)
            .Select(x => x.SampleType.Name)
            .SingleOrDefaultAsync(cancellationToken);
        var completedRows = await dbContext.QcFruitReadings.AsNoTracking().Where(x => x.QcSampleId == sampleId && x.IsCompleted).ToListAsync(cancellationToken);
        var receiptPhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.ReceiptId == receiptId).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var samplePhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == sampleId).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var missing = new List<string>();
        var invalidRows = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null || x.WeightGrams is null || x.GradeId is null);
        var pressureMissing = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null);
        var weightMissing = completedRows.Count(x => x.WeightGrams is null);
        var gradeMissing = completedRows.Count(x => x.GradeId is null);
        var starchMissing = completedRows.Count(x => x.StarchScaleValueId is null);

        if (completedRows.Count == 0) missing.Add("At least one completed fruit row is required.");
        if (invalidRows > 0) missing.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        if (starchMissing > 0) missing.Add("Starch is required for all completed fruit rows.");
        var hasBinTruck = receiptPhotos.Contains("BinTruck");
        var hasSampleBeforeCutting = samplePhotos.Contains("SampleBeforeCutting");
        var hasCutFruit = samplePhotos.Contains("CutFruit");
        var hasFruitAfterStarch = samplePhotos.Contains("FruitAfterStarch");
        var requiredPhotoChecklist = photoRequirementPolicy.BuildChecklist(sampleTypeName, receiptPhotos, samplePhotos);
        missing.AddRange(photoRequirementPolicy.MissingRequiredPhotos(sampleTypeName, receiptPhotos, samplePhotos));

        var checklist = new List<ReadinessChecklistItem>
        {
            ChecklistItem("Required data", "At least one completed fruit row", completedRows.Count > 0, "Missing"),
            ChecklistItem("Required data", "Pressure 1 and Pressure 2 for every completed fruit row", completedRows.Count == 0 || pressureMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Weight for every completed fruit row", completedRows.Count == 0 || weightMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Grade for every completed fruit row", completedRows.Count == 0 || gradeMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Starch for every completed fruit row", completedRows.Count == 0 || starchMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing")
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

    private async Task RefreshSampleStatusesAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
        sample.ActualSampleSize = readiness.CompletedFruitCount;
        sample.StarchStatus = readiness.CompletedFruitCount > 0 && readiness.StarchMissingCount == 0
            ? "Starch Complete"
            : "Starch Pending";
        sample.PhotoStatus = readiness.RequiredPhotoChecklist.All(x => x.Status == "Complete")
            ? "Photos Complete"
            : "Photo Pending";
        if (!sample.Status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase) && sample.EmailStatus != "Sent")
        {
            sample.Status = readiness.IsReady
                ? "Ready to Send"
                : readiness.StarchMissingCount > 0 ? "Starch Pending"
                : sample.PhotoStatus == "Photo Pending" ? "Photo Pending"
                : "Data Entry In Progress";
        }

        sample.UpdatedAt = DateTimeOffset.UtcNow;
    }

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

    private static ReceiptListItemViewModel ReceiptListItem(Receipt receipt, ReceiptSampleSummary? sampleSummary = null) => new(
        receipt.Id,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.CompuTechReceiptId,
        receipt.Warehouse.Code,
        receipt.Room.Code,
        receipt.GrowerName,
        receipt.LotCode,
        receipt.FruitProfile.VarietyCode,
        receipt.BinCount,
        sampleSummary?.SampleCount ?? 0,
        BuildReceiptQcStatus(sampleSummary),
        sampleSummary?.LastUpdatedAt ?? receipt.UpdatedAt);

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
        var values = rows
            .Where(x => x.IsCompleted)
            .Select(x => Average(x.Pressure1Lbs, x.Pressure2Lbs))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        return values.Count == 0 ? null : decimal.Round(values.Average(), 2);
    }

    private sealed record ReceiptSampleSummary(long ReceiptId, int SampleCount, DateTimeOffset LastUpdatedAt, bool HasReady, bool HasReview, bool HasSent);

    private static IReadOnlyList<PhotoGroupViewModel> GroupPhotos(IReadOnlyList<QcPhoto> photos) =>
        photos.GroupBy(x => x.PhotoType)
            .OrderBy(x => x.Key)
            .Select(x => new PhotoGroupViewModel(x.Key, x.Select(photo => new PhotoMetadataViewModel(photo.PhotoType, photo.PhotoSource, photo.FileName, photo.ContentType, photo.FileSizeBytes, photo.WebUrl, photo.CapturedAt)).ToList()))
            .ToList();

    private async Task<FileStorageReference> SavePhotoFileOrPlaceholderAsync(AddPhotoMetadataForm form, Receipt receipt, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        var context = new FileStorageTargetContext(
            receipt.CropYear,
            receipt.Warehouse.Code,
            receipt.CompuTechReceiptId,
            form.PhotoType.Trim(),
            capturedAt);
        var targetPath = fileStorageService.GenerateTargetPath(context);
        logger.LogInformation(
            "Storage save started. Provider: {StorageProvider}. TargetPath: {TargetPath}. ReceiptId: {ReceiptId}. PhotoType: {PhotoType}. Uploaded file present: {HasFile}.",
            fileStorageOptions.Provider,
            targetPath,
            receipt.CompuTechReceiptId,
            form.PhotoType,
            form.PhotoFile is not null);

        if (form.PhotoFile is null)
        {
            if (string.Equals(form.PhotoSource, "Upload File", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("No photo file was selected.");
            }

            var placeholderName = GeneratePhotoFileName(receipt.CompuTechReceiptId, form.PhotoType, capturedAt, ".jpg");
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

        var fileName = GeneratePhotoFileName(receipt.CompuTechReceiptId, form.PhotoType, capturedAt, extension);
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
        ("Starch scale values", "/MasterData/starch-scale-values"),
        ("Size thresholds", "/MasterData/size-thresholds")
    ];
}
