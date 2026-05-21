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
    Task<(long? SampleId, int? SampleSequenceNumber, string? Warning, string? Error)> CreateReceivingSampleAsync(long receiptId, CancellationToken cancellationToken);
    Task<SampleDetailViewModel> GetSampleDetailAsync(long id, CancellationToken cancellationToken);
    Task<string?> SaveFruitReadingsAsync(SaveFruitReadingsForm form, CancellationToken cancellationToken);
    Task<StarchTestViewModel> GetStarchTestAsync(long id, CancellationToken cancellationToken);
    Task<string?> SaveStarchTestAsync(SaveStarchTestForm form, CancellationToken cancellationToken);
    Task<OverrideSendViewModel> GetOverrideSendAsync(long id, CancellationToken cancellationToken);
    Task<string?> LogOverrideSendAsync(OverrideSendForm form, CancellationToken cancellationToken);
    Task<string?> AddPhotoMetadataAsync(AddPhotoMetadataForm form, CancellationToken cancellationToken);
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
            if (search.WarehouseId is not null && search.RoomId is not null)
            {
                var roomMatchesWarehouse = await dbContext.Rooms.AsNoTracking()
                    .AnyAsync(x => x.Id == search.RoomId && x.WarehouseId == search.WarehouseId, cancellationToken);
                if (!roomMatchesWarehouse)
                {
                    search.RoomId = null;
                }
            }

            var query = dbContext.Receipts.AsNoTracking().Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).AsQueryable();
            if (search.CropYear is not null) query = query.Where(x => x.CropYear == search.CropYear);
            if (!string.IsNullOrWhiteSpace(search.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(search.ReceiptId));
            if (!string.IsNullOrWhiteSpace(search.Grower)) query = query.Where(x => x.GrowerName.Contains(search.Grower));
            if (!string.IsNullOrWhiteSpace(search.Lot)) query = query.Where(x => x.LotCode.Contains(search.Lot));
            if (search.WarehouseId is not null) query = query.Where(x => x.WarehouseId == search.WarehouseId);
            if (search.RoomId is not null) query = query.Where(x => x.RoomId == search.RoomId);
            if (search.FruitProfileId is not null) query = query.Where(x => x.FruitProfileId == search.FruitProfileId);

            var receipts = await query.OrderByDescending(x => x.ReceivedAt).Take(200).ToListAsync(cancellationToken);
            return new ReceiptListViewModel
            {
                Search = search,
                Receipts = receipts.Select(ReceiptListItem).ToList(),
                Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
                Rooms = await dbContext.Rooms.AsNoTracking().OrderBy(x => x.WarehouseId).ThenBy(x => x.Code).ToListAsync(cancellationToken),
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

        var room = await dbContext.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.RoomId, cancellationToken);
        if (room is null)
        {
            return "Selected room was not found.";
        }

        if (room.WarehouseId != form.WarehouseId)
        {
            return "Selected room does not belong to the selected warehouse.";
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
                PhotoGroups = GroupPhotos(photos),
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
        var receiptExists = await dbContext.Receipts.AnyAsync(x => x.Id == receiptId, cancellationToken);
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
            .CountAsync(x => x.ReceiptId == receiptId && x.SampleTypeId == receivingSampleType.Id, cancellationToken);
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
                .Where(x => x.QcSampleId == id && (x.PhotoType == "SampleBeforeCutting" || x.PhotoType == "CutFruit" || x.PhotoType == "Other"))
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

            var isBlank = submittedRow.Pressure1Lbs is null
                && submittedRow.Pressure2Lbs is null
                && submittedRow.WeightGrams is null
                && submittedRow.GradeId is null
                && selectedDefectIds.Count == 0
                && string.IsNullOrWhiteSpace(submittedRow.OtherDefectNotes);
            var isCompleted = submittedRow.Pressure1Lbs is not null
                && submittedRow.Pressure2Lbs is not null
                && submittedRow.WeightGrams is not null
                && submittedRow.GradeId is not null;
            if (!isBlank && !isCompleted)
            {
                return $"Row {submittedRow.RowNumber} is partially entered. Completed rows require Pressure 1, Pressure 2, weight, and grade.";
            }

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
            return new OverrideSendViewModel
            {
                Sample = (await EnrichSamplesAsync([sample], cancellationToken)).Single(),
                Receipt = ReceiptListItem(sample.Receipt),
                Readiness = readiness,
                Checklist = readiness.Checklist,
                Form = new OverrideSendForm { SampleId = sample.Id }
            };
        }
        catch
        {
            return new OverrideSendViewModel { DataWarning = DataWarning };
        }
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
        dbContext.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            ReceiptId = sample.ReceiptId,
            QcSampleId = sample.Id,
            FromAddress = "HL@fruitandland.com",
            ToAddress = "QC@fruitandland.com",
            ReplyToAddress = sample.TakenByUser?.Email,
            Subject = $"QC Summary Override Placeholder - {sample.GetDisplayReceiptId()}",
            Status = "OverrideLogged",
            SentAt = null,
            IsResend = false,
            IsOverride = true,
            OverrideReason = form.OverrideReason.Trim(),
            MissingItemsSnapshot = string.Join(Environment.NewLine, readiness.MissingItems),
            EmailBodySnapshot = "Override send placeholder logged; no email was sent.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> AddPhotoMetadataAsync(AddPhotoMetadataForm form, CancellationToken cancellationToken)
    {
        if ((form.ReceiptId is null && form.QcSampleId is null) || (form.ReceiptId is not null && form.QcSampleId is not null))
        {
            return "Photo metadata must attach to either a receipt or a QC sample.";
        }

        if (string.IsNullOrWhiteSpace(form.PhotoType) || string.IsNullOrWhiteSpace(form.PhotoSource) || string.IsNullOrWhiteSpace(form.FileName) || string.IsNullOrWhiteSpace(form.ContentType) || string.IsNullOrWhiteSpace(form.SharePointDriveId) || string.IsNullOrWhiteSpace(form.SharePointItemId))
        {
            return "Photo type, source, file name, content type, target folder, and file reference are required.";
        }

        if (form.ReceiptId is not null && !await dbContext.Receipts.AnyAsync(x => x.Id == form.ReceiptId, cancellationToken))
        {
            return "Receipt not found.";
        }

        var sample = form.QcSampleId is null
            ? null
            : await dbContext.QcSamples.Include(x => x.Receipt).SingleOrDefaultAsync(x => x.Id == form.QcSampleId, cancellationToken);
        if (form.QcSampleId is not null && sample is null)
        {
            return "QC sample not found.";
        }

        dbContext.QcPhotos.Add(new QcPhoto
        {
            ReceiptId = form.ReceiptId,
            QcSampleId = form.QcSampleId,
            PhotoType = form.PhotoType.Trim(),
            PhotoSource = form.PhotoSource.Trim(),
            FileName = form.FileName.Trim(),
            ContentType = form.ContentType.Trim(),
            FileSizeBytes = form.FileSizeBytes,
            SharePointDriveId = form.SharePointDriveId.Trim(),
            SharePointItemId = form.SharePointItemId.Trim(),
            WebUrl = string.IsNullOrWhiteSpace(form.WebUrl) ? null : form.WebUrl.Trim(),
            CapturedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
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
                CropYear = sample.Receipt.CropYear,
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
                MissingItems = readiness.MissingItems,
                Checklist = readiness.Checklist
            });
        }

        return result;
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
                    ? new FruitReadingRowViewModel { RowNumber = rowNumber }
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
                        DefectTypeIds = row.Defects.Select(x => x.DefectTypeId).ToList(),
                        Defects = row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x).ToList(),
                        OtherDefectNotes = row.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes
                    };
            })
            .ToList();
    }

    private async Task<ReadinessViewModel> GetReadinessAsync(long sampleId, long receiptId, CancellationToken cancellationToken)
    {
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
        if (!hasBinTruck) missing.Add("At least one bin/truck photo is required on the receipt.");
        if (!hasSampleBeforeCutting) missing.Add("Sample before cutting photo is required.");
        if (!hasCutFruit) missing.Add("Cut fruit photo is required.");
        if (!hasFruitAfterStarch) missing.Add("Fruit after starch photo is required.");

        var checklist = new List<ReadinessChecklistItem>
        {
            ChecklistItem("Required data", "At least one completed fruit row", completedRows.Count > 0, "Missing"),
            ChecklistItem("Required data", "Pressure 1 and Pressure 2 for every completed fruit row", completedRows.Count == 0 || pressureMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Weight for every completed fruit row", completedRows.Count == 0 || weightMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Grade for every completed fruit row", completedRows.Count == 0 || gradeMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required data", "Starch for every completed fruit row", completedRows.Count == 0 || starchMissing == 0, completedRows.Count == 0 ? "Not applicable" : "Missing"),
            ChecklistItem("Required photos", "At least one BinTruck photo on the receipt", hasBinTruck, "Missing"),
            ChecklistItem("Required photos", "SampleBeforeCutting photo on the sample", hasSampleBeforeCutting, "Missing"),
            ChecklistItem("Required photos", "CutFruit photo on the sample", hasCutFruit, "Missing"),
            ChecklistItem("Required photos", "FruitAfterStarch photo on the starch page/sample", hasFruitAfterStarch, "Missing")
        };

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
            HasFruitAfterStarch = hasFruitAfterStarch
        };
    }

    private async Task RefreshSampleStatusesAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var readiness = await GetReadinessAsync(sample.Id, sample.ReceiptId, cancellationToken);
        sample.ActualSampleSize = readiness.CompletedFruitCount;
        sample.StarchStatus = readiness.CompletedFruitCount > 0 && readiness.StarchMissingCount == 0
            ? "Starch Complete"
            : "Starch Pending";
        sample.PhotoStatus = readiness.HasBinTruck && readiness.HasSampleBeforeCutting && readiness.HasCutFruit && readiness.HasFruitAfterStarch
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
