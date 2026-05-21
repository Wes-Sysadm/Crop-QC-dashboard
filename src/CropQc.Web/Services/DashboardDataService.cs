using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IDashboardDataService
{
    Task<HomeDashboardViewModel> GetHomeDashboardAsync(CancellationToken cancellationToken);
    Task<MasterDataPageViewModel> GetMasterDataPageAsync(string type, CancellationToken cancellationToken);
    Task<ReceiptListViewModel> SearchReceiptsAsync(ReceiptSearchForm search, CancellationToken cancellationToken);
    Task<string?> CreateReceiptAsync(CreateReceiptForm form, CancellationToken cancellationToken);
    Task<ReceiptDetailViewModel> GetReceiptDetailAsync(long id, CancellationToken cancellationToken);
    Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken);
    Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, CancellationToken cancellationToken);
}

public sealed class DashboardDataService(CropQcDbContext dbContext) : IDashboardDataService
{
    private const string DataWarning = "Database is not available yet. The dashboard shell is running with empty data.";

    public async Task<HomeDashboardViewModel> GetHomeDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            var todaySamples = await QuerySamples().Where(x => x.SampleTakenAt.Date == DateTimeOffset.UtcNow.Date).ToListAsync(cancellationToken);
            var enriched = await EnrichSamplesAsync(todaySamples, cancellationToken);
            return new HomeDashboardViewModel
            {
                Cards =
                [
                    new("Today's receiving samples", enriched.Count, "/DailyQc", "info"),
                    new("Samples ready to send", enriched.Count(x => x.IsReady), "/DailyQc", "ready"),
                    new("Samples missing required data", enriched.Count(x => !x.IsReady), "/DailyQc", "missing"),
                    new("Samples already sent", enriched.Count(x => x.EmailStatus == "Sent"), "/DailyQc", "sent"),
                    new("Samples needing review", enriched.Count(x => x.Status.Contains("Needs Review", StringComparison.OrdinalIgnoreCase)), "/DailyQc", "review"),
                    new("Master data/admin links", 8, "/MasterData", "admin")
                ],
                TodaySamples = enriched
            };
        }
        catch
        {
            return new HomeDashboardViewModel
            {
                DataWarning = DataWarning,
                Cards =
                [
                    new("Today's receiving samples", 0, "/DailyQc", "info"),
                    new("Samples ready to send", 0, "/DailyQc", "ready"),
                    new("Samples missing required data", 0, "/DailyQc", "missing"),
                    new("Samples already sent", 0, "/DailyQc", "sent"),
                    new("Samples needing review", 0, "/DailyQc", "review"),
                    new("Master data/admin links", 8, "/MasterData", "admin")
                ]
            };
        }
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
            var query = dbContext.Receipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).AsQueryable();
            if (search.CropYear is not null) query = query.Where(x => x.CropYear == search.CropYear);
            if (!string.IsNullOrWhiteSpace(search.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(search.ReceiptId));
            if (!string.IsNullOrWhiteSpace(search.Grower)) query = query.Where(x => x.GrowerName.Contains(search.Grower));
            if (!string.IsNullOrWhiteSpace(search.Lot)) query = query.Where(x => x.LotCode.Contains(search.Lot));
            if (search.WarehouseId is not null) query = query.Where(x => x.WarehouseId == search.WarehouseId);
            if (search.RoomId is not null) query = query.Where(x => x.RoomId == search.RoomId);
            if (search.FruitProfileId is not null) query = query.Where(x => x.FruitProfileId == search.FruitProfileId);

            return new ReceiptListViewModel
            {
                Search = search,
                Receipts = await query.OrderByDescending(x => x.ReceivedAt).Take(200).Select(x => ReceiptListItem(x)).ToListAsync(cancellationToken),
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Rooms = await dbContext.Rooms.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                FruitProfiles = await dbContext.FruitProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken)
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
            var receipt = await dbContext.Receipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (receipt is null)
            {
                return new ReceiptDetailViewModel { DataWarning = "Receipt not found." };
            }

            var samples = await QuerySamples().Where(x => x.ReceiptId == id).ToListAsync(cancellationToken);
            var photos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.ReceiptId == id).OrderByDescending(x => x.CapturedAt).ToListAsync(cancellationToken);
            return new ReceiptDetailViewModel
            {
                Receipt = ReceiptListItem(receipt),
                Samples = await EnrichSamplesAsync(samples, cancellationToken),
                PhotoGroups = GroupPhotos(photos)
            };
        }
        catch
        {
            return new ReceiptDetailViewModel { DataWarning = DataWarning };
        }
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

            var rows = await dbContext.QcFruitReadings.AsNoTracking()
                .Include(x => x.Grade)
                .Include(x => x.StarchScaleValue)
                .Include(x => x.Defects).ThenInclude(x => x.DefectType)
                .Where(x => x.QcSampleId == id)
                .ToListAsync(cancellationToken);

            var rowModels = Enumerable.Range(1, 25)
                .Select(rowNumber =>
                {
                    var row = rows.SingleOrDefault(x => x.RowNumber == rowNumber);
                    return row is null
                        ? new FruitReadingRowViewModel(rowNumber, null, null, null, null, null, null, null, "", false, [])
                        : new FruitReadingRowViewModel(row.RowNumber, row.Pressure1Lbs, row.Pressure2Lbs, Average(row.Pressure1Lbs, row.Pressure2Lbs), row.WeightGrams, row.Grade?.Code, row.StarchScaleValue?.Value.ToString("0.0"), row.SizeCategory, row.SizeStatus, row.IsCompleted, row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x).ToList());
                })
                .ToList();

            var photos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == id).OrderByDescending(x => x.CapturedAt).ToListAsync(cancellationToken);
            return new SampleDetailViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                FruitRows = rowModels,
                PhotoGroups = GroupPhotos(photos),
                Readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken)
            };
        }
        catch
        {
            return new SampleDetailViewModel { DataWarning = DataWarning };
        }
    }

    public async Task<DailyQcDashboardViewModel> GetDailyQcDashboardAsync(int? warehouseId, CancellationToken cancellationToken)
    {
        try
        {
            var query = QuerySamples().Where(x => x.SampleTakenAt.Date == DateTimeOffset.UtcNow.Date);
            if (warehouseId is not null)
            {
                query = query.Where(x => x.Receipt.WarehouseId == warehouseId);
            }

            var samples = await query.OrderByDescending(x => x.SampleTakenAt).ToListAsync(cancellationToken);
            return new DailyQcDashboardViewModel
            {
                WarehouseId = warehouseId,
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Samples = await EnrichSamplesAsync(samples, cancellationToken)
            };
        }
        catch
        {
            return new DailyQcDashboardViewModel { WarehouseId = warehouseId, DataWarning = DataWarning };
        }
    }

    private IQueryable<QcSample> QuerySamples() =>
        dbContext.QcSamples.AsNoTracking()
            .Include(x => x.Receipt).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt).ThenInclude(x => x.Room)
            .Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .Include(x => x.TakenByUser);

    private async Task<IReadOnlyList<SampleListItemViewModel>> EnrichSamplesAsync(IReadOnlyList<QcSample> samples, CancellationToken cancellationToken)
    {
        var result = new List<SampleListItemViewModel>();
        foreach (var sample in samples)
        {
            var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
            result.Add(new SampleListItemViewModel
            {
                Id = sample.Id,
                ReceiptId = sample.ReceiptId,
                ReceiptIdText = sample.Receipt.CompuTechReceiptId,
                DisplayReceiptId = sample.SampleSequenceNumber <= 1 ? sample.Receipt.CompuTechReceiptId : $"{sample.Receipt.CompuTechReceiptId}({sample.SampleSequenceNumber})",
                Warehouse = sample.Receipt.Warehouse.Code,
                Status = sample.Status,
                StarchStatus = sample.StarchStatus,
                PhotoStatus = sample.PhotoStatus,
                EmailStatus = sample.EmailStatus,
                TakenBy = sample.TakenByUser?.DisplayName,
                SampleTakenAt = sample.SampleTakenAt,
                ActualSampleSize = sample.ActualSampleSize,
                IsReady = readiness.IsReady,
                MissingItems = readiness.MissingItems
            });
        }

        return result;
    }

    private async Task<ReadinessViewModel> GetReadinessAsync(long sampleId, long receiptId, CancellationToken cancellationToken)
    {
        var completedRows = await dbContext.QcFruitReadings.AsNoTracking().Where(x => x.QcSampleId == sampleId && x.IsCompleted).ToListAsync(cancellationToken);
        var receiptPhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.ReceiptId == receiptId).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var samplePhotos = await dbContext.QcPhotos.AsNoTracking().Where(x => x.QcSampleId == sampleId).Select(x => x.PhotoType).ToListAsync(cancellationToken);
        var missing = new List<string>();
        var invalidRows = completedRows.Count(x => x.Pressure1Lbs is null || x.Pressure2Lbs is null || x.WeightGrams is null || x.GradeId is null);
        var starchMissing = completedRows.Count(x => x.StarchScaleValueId is null);

        if (completedRows.Count == 0) missing.Add("At least one completed fruit row is required.");
        if (invalidRows > 0) missing.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        if (starchMissing > 0) missing.Add("Starch is required for all completed fruit rows.");
        var hasBinTruck = receiptPhotos.Contains("BinTruck");
        var hasSampleBeforeCutting = samplePhotos.Contains("SampleBeforeCutting");
        var hasCutFruit = samplePhotos.Contains("CutFruit");
        var hasFruitAfterStarch = samplePhotos.Contains("FruitAfterStarch");
        if (!hasBinTruck) missing.Add("At least one bin/truck photo is required on the receipt.");
        if (!hasSampleBeforeCutting) missing.Add("Sample before cutting photo is required.");
        if (!hasCutFruit) missing.Add("Cut fruit photo is required.");
        if (!hasFruitAfterStarch) missing.Add("Fruit after starch photo is required.");

        return new ReadinessViewModel
        {
            IsReady = missing.Count == 0,
            MissingItems = missing,
            CompletedFruitCount = completedRows.Count,
            StarchMissingCount = starchMissing,
            HasBinTruck = hasBinTruck,
            HasSampleBeforeCutting = hasSampleBeforeCutting,
            HasCutFruit = hasCutFruit,
            HasFruitAfterStarch = hasFruitAfterStarch
        };
    }

    private static ReceiptListItemViewModel ReceiptListItem(Receipt receipt) => new(
        receipt.Id,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.CompuTechReceiptId,
        receipt.Warehouse.Code,
        receipt.Room.Code,
        receipt.GrowerName,
        receipt.LotCode,
        receipt.FruitProfile.VarietyCode,
        receipt.BinCount);

    private static IReadOnlyList<PhotoGroupViewModel> GroupPhotos(IReadOnlyList<QcPhoto> photos) =>
        photos.GroupBy(x => x.PhotoType)
            .OrderBy(x => x.Key)
            .Select(x => new PhotoGroupViewModel(x.Key, x.Select(photo => new PhotoMetadataViewModel(photo.PhotoType, photo.PhotoSource, photo.FileName, photo.ContentType, photo.FileSizeBytes, photo.WebUrl, photo.CapturedAt)).ToList()))
            .ToList();

    private static decimal? Average(decimal? first, decimal? second) =>
        first is null || second is null ? null : decimal.Round((first.Value + second.Value) / 2m, 2);

    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static IReadOnlyList<string> Row(params string[] values) => values;
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
