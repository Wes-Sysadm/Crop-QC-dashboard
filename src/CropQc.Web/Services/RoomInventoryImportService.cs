using System.Globalization;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IRoomInventoryImportService
{
    Task<RoomInventoryImportPageViewModel> GetPageAsync(RoomInventoryImportForm filter, CancellationToken cancellationToken);
    Task<RoomInventoryImportPreviewViewModel> PreviewAsync(RoomInventoryImportForm form, CancellationToken cancellationToken);
    Task<(RoomInventoryImportPreviewViewModel Preview, string? Error)> ApplyAsync(RoomInventoryImportForm form, string changedByEmail, CancellationToken cancellationToken);
    string GetCsvTemplate();
    string GetCsvExample();
}

public sealed class RoomInventoryImportService(CropQcDbContext dbContext, IWebHostEnvironment environment, ICropYearService cropYearService) : IRoomInventoryImportService
{
    public const string BuiltInEbsSeedFileName = "ebs-starting-room-inventory.csv";
    public const string StartingInventoryAdjustmentType = "StartingInventoryImport";
    public const string CurrentInventoryBaselineType = StartingInventoryAdjustmentType;
    public const string DefaultStartingInventorySource = "Current Inventory Baseline";
    private const string TemplateHeader = "CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes";
    private const string ExampleCsv = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2026,EBS,EVANCA12,,1560,FUJI,118,Sealed,2026-06-18,Wes verified baseline
2026,EBS,EVANCA12,,1570,FUJI,819,Sealed,2026-06-18,Wes verified baseline
2026,EBS,EVANCA12,,1030,FUJI,85,Sealed,2026-06-18,Wes verified baseline
""";

    public async Task<RoomInventoryImportPageViewModel> GetPageAsync(RoomInventoryImportForm filter, CancellationToken cancellationToken)
    {
        return new RoomInventoryImportPageViewModel
        {
            Form = filter,
            CurrentLots = await GetCurrentLotsAsync(filter, cancellationToken),
            CsvTemplateHeader = TemplateHeader,
            CsvExample = ExampleCsv
        };
    }

    public async Task<RoomInventoryImportPreviewViewModel> PreviewAsync(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        var csvText = await ReadCsvTextAsync(form, cancellationToken);
        return await BuildPreviewAsync(csvText, form.UseBuiltInSeed, cancellationToken);
    }

    public async Task<(RoomInventoryImportPreviewViewModel Preview, string? Error)> ApplyAsync(RoomInventoryImportForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var csvText = await ReadCsvTextAsync(form, cancellationToken);
        var preview = await BuildPreviewAsync(csvText, form.UseBuiltInSeed, cancellationToken);
        if (!form.ConfirmImport)
        {
            return (preview, "Confirm Current Inventory Baseline import before applying changes.");
        }

        if (!preview.CanApply)
        {
            return (preview, "Resolve invalid or duplicate rows before importing room inventory.");
        }

        if (preview.RequiresReplaceConfirmation && !form.ConfirmReplaceExistingBatch)
        {
            return (preview, "This import replaces rows from an existing baseline batch with the same crop year, room, lot, variety, and effective date. Confirm replacement before applying.");
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in preview.Rows.Where(x => x.Action is "Add" or "Update" or "Replace"))
        {
            var oldCount = row.OldBinCount;
            var newCount = row.NewBinCount ?? 0;
            var adjustment = new RoomInventoryAdjustment
            {
                CropYear = row.CropYear,
                ReceiptId = null,
                RoomDepletionId = null,
                WarehouseId = row.WarehouseId!.Value,
                RoomId = row.RoomId!.Value,
                GrowerLotId = row.GrowerLotId,
                FruitProfileId = row.FruitProfileId,
                GrowerName = string.IsNullOrWhiteSpace(row.Grower) ? "Grower not found in Master Data" : row.Grower,
                LotNumber = row.LotNumber,
                PoolStart = string.IsNullOrWhiteSpace(row.PoolStart) ? null : row.PoolStart,
                VarietyCode = row.Variety,
                OldBinCount = oldCount,
                ChangeAmount = newCount - (oldCount ?? 0),
                NewBinCount = newCount,
                AdjustmentType = StartingInventoryAdjustmentType,
                Source = row.Source,
                SourceRoomCode = row.CompuTechRoomCode,
                SourceSubLocation = row.SubLocation,
                InventoryStatus = string.IsNullOrWhiteSpace(row.InventoryStatus) ? null : row.InventoryStatus,
                Reason = row.Source,
                Notes = $"Current inventory baseline imported from {row.Source}; Crop year {row.CropYear}; status {DisplayOrDash(row.InventoryStatus)}; Crop QC room {row.CropQcRoomName}; Compu-Tech room {row.CompuTechRoomCode}; mapped to master room {row.NormalizedRoomCode}. {row.Notes}".Trim(),
                AdjustmentAt = row.EffectiveDate,
                CreatedByUserId = user?.Id,
                CreatedAt = now
            };
            dbContext.RoomInventoryAdjustments.Add(adjustment);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "BinCountChange",
                EntityName = nameof(RoomInventoryAdjustment),
                EntityKey = $"{row.Facility}:{row.CropQcRoomName}:{row.CompuTechRoomCode}:{row.LotNumber}:{row.Variety}",
                UserId = user?.Id,
                BeforeValuesJson = oldCount is null ? null : JsonSerializer.Serialize(new { row.CropYear, row.Facility, row.SubLocation, row.CropQcRoomName, row.CompuTechRoomCode, row.LotNumber, row.Variety, row.InventoryStatus, BinCount = oldCount }),
                AfterValuesJson = JsonSerializer.Serialize(new { row.CropYear, row.Facility, row.SubLocation, row.CropQcRoomName, row.CompuTechRoomCode, row.LotNumber, row.Variety, row.InventoryStatus, BinCount = newCount, row.Source, row.EffectiveDate }),
                SourceApplication = "Web",
                CreatedAt = now
            });
        }

        await UpdateEbsRoomMetadataAsync(preview.Rows.Where(x => x.Action is "Add" or "Update" or "Replace" or "Unchanged"), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (preview, null);
    }

    public string GetCsvTemplate() =>
        $"{TemplateHeader}{Environment.NewLine}";

    public string GetCsvExample() =>
        ExampleCsv;

    private async Task<RoomInventoryImportPreviewViewModel> BuildPreviewAsync(string csvText, bool isBuiltInSeed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            return new RoomInventoryImportPreviewViewModel
            {
                CsvText = "",
                IsBuiltInSeed = isBuiltInSeed,
                InvalidCount = 1,
                Rows = [Invalid(new() { RowNumber = 0 }, "File", "Upload a CSV or use the built-in EBS current inventory baseline file.")]
            };
        }

        var parsedRows = ParseCsv(csvText).ToList();
        if (parsedRows.Count < 2)
        {
            return new RoomInventoryImportPreviewViewModel
            {
                CsvText = csvText,
                IsBuiltInSeed = isBuiltInSeed,
                InvalidCount = 1,
                Rows = [Invalid(new() { RowNumber = 0 }, "File", "CSV must include a header row and at least one inventory row.")]
            };
        }

        var headers = parsedRows[0].Select(NormalizeHeader).ToList();
        var cropYearIndex = FindHeader(headers, ["cropyear"]);
        var facilityIndex = FindHeader(headers, ["warehouse", "facility"]);
        var subLocationIndex = FindHeader(headers, ["sublocation", "location"]);
        var cropQcRoomIndex = FindHeader(headers, ["cropqcroomname", "cropqcname", "cropqcroom", "displayroom", "roomname"]);
        var compuTechRoomIndex = FindHeader(headers, ["roomcode", "room", "computechroomcode", "compu-techroomcode", "compu-techroom", "computechroom"]);
        var growerIndex = FindHeader(headers, ["grower", "growername"]);
        var varietyIndex = FindHeader(headers, ["variety"]);
        var lotIndex = FindHeader(headers, ["lot", "lotnumber", "lot#"]);
        var binIndex = FindHeader(headers, ["bins", "bincount", "count"]);
        var statusIndex = FindHeader(headers, ["status"]);
        var effectiveDateIndex = FindHeader(headers, ["effectivedate"]);
        var notesIndex = FindHeader(headers, ["notes"]);
        var missingHeaders = MissingRequiredHeaders(
            (cropYearIndex, "CropYear"),
            (facilityIndex, "Warehouse"),
            (compuTechRoomIndex, "RoomCode"),
            (lotIndex, "Lot"),
            (varietyIndex, "Variety"),
            (binIndex, "Bins"),
            (effectiveDateIndex, "EffectiveDate"));
        if (missingHeaders.Count > 0)
        {
            return new RoomInventoryImportPreviewViewModel
            {
                CsvText = csvText,
                IsBuiltInSeed = isBuiltInSeed,
                InvalidCount = 1,
                Rows = [Invalid(new() { RowNumber = 0 }, "Headers", $"CSV headers are missing required column(s): {string.Join(", ", missingHeaders)}. Required format: {TemplateHeader}.")]
            };
        }

        var warehouses = await dbContext.Warehouses.AsNoTracking().ToListAsync(cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).ToListAsync(cancellationToken);
        var growerLotsByLot = (await dbContext.GrowerLots.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(x => x.LotNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var fruitProfiles = await dbContext.FruitProfiles.AsNoTracking().ToListAsync(cancellationToken);
        var defaultEffectiveDate = DateTimeOffset.UtcNow;
        var defaultCropYear = cropYearService.GetCurrentCropYear(defaultEffectiveDate);
        var currentByKey = (await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.ReceiptId == null && x.AdjustmentType == StartingInventoryAdjustmentType)
                .ToListAsync(cancellationToken))
            .GroupBy(StartingInventoryKey)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First(), StringComparer.OrdinalIgnoreCase);
        var existingBaselineBatchByKey = (await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.ReceiptId == null && x.AdjustmentType == StartingInventoryAdjustmentType)
                .ToListAsync(cancellationToken))
            .GroupBy(BaselineBatchKey)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First(), StringComparer.OrdinalIgnoreCase);

        var seenRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previewRows = new List<RoomInventoryImportPreviewRow>();
        for (var i = 1; i < parsedRows.Count; i++)
        {
            var raw = parsedRows[i];
            var rowNumber = i + 1;
            var facility = GetCell(raw, facilityIndex).Trim();
            var compuTechRoomCode = GetCell(raw, compuTechRoomIndex).Trim();
            var cropQcRoomName = cropQcRoomIndex >= 0 ? GetCell(raw, cropQcRoomIndex).Trim() : "";
            var variety = GetCell(raw, varietyIndex).Trim();
            var lotNumber = GetCell(raw, lotIndex).Trim();
            var binText = GetCell(raw, binIndex).Trim();
            var status = statusIndex >= 0 ? GetCell(raw, statusIndex).Trim() : "";
            var source = DefaultStartingInventorySource;
            var subLocation = subLocationIndex >= 0 ? GetCell(raw, subLocationIndex).Trim() : "";
            var notes = notesIndex >= 0 ? GetCell(raw, notesIndex).Trim() : "";
            var effectiveDateText = GetCell(raw, effectiveDateIndex).Trim();
            var effectiveDate = ParseEffectiveDate(effectiveDateText, defaultEffectiveDate);
            var cropYearText = GetCell(raw, cropYearIndex).Trim();
            var cropYear = ParseCropYear(cropYearText, cropYearService.GetCurrentCropYear(effectiveDate), defaultCropYear);
            cropQcRoomName = string.IsNullOrWhiteSpace(cropQcRoomName) ? CropQcRoomNameForCompuTechCode(compuTechRoomCode) : NormalizeCropQcRoomName(cropQcRoomName);
            subLocation = string.IsNullOrWhiteSpace(subLocation) ? DetermineEbsSubLocation(cropQcRoomName, compuTechRoomCode) : subLocation;
            var mappedRoomCode = MasterRoomCodeFor(cropQcRoomName, compuTechRoomCode);
            var normalizedRoomCode = NormalizeCode(mappedRoomCode);
            var row = new RoomInventoryImportPreviewRow
            {
                RowNumber = rowNumber,
                CropYear = cropYear,
                Facility = facility,
                SubLocation = subLocation,
                CropQcRoomName = cropQcRoomName,
                CompuTechRoomCode = compuTechRoomCode,
                RoomCode = cropQcRoomName,
                NormalizedRoomCode = mappedRoomCode,
                Variety = NormalizeVariety(variety),
                LotNumber = lotNumber,
                InventoryStatus = NormalizeStatus(status),
                EffectiveDate = effectiveDate,
                Source = source,
                Notes = notes
            };

            if (string.IsNullOrWhiteSpace(facility) || string.IsNullOrWhiteSpace(compuTechRoomCode) || string.IsNullOrWhiteSpace(variety) || string.IsNullOrWhiteSpace(lotNumber) || string.IsNullOrWhiteSpace(cropYearText) || string.IsNullOrWhiteSpace(effectiveDateText))
            {
                previewRows.Add(Invalid(row, MissingRequiredColumn(
                    (cropYearText, "CropYear"),
                    (facility, "Warehouse"),
                    (compuTechRoomCode, "RoomCode"),
                    (lotNumber, "Lot"),
                    (variety, "Variety"),
                    (binText, "Bins"),
                    (effectiveDateText, "EffectiveDate")), $"CropYear, Warehouse, RoomCode, Lot, Variety, Bins, and EffectiveDate are required. Read values: CropYear={DisplayOrDash(cropYearText)}, Warehouse={DisplayOrDash(facility)}, RoomCode={DisplayOrDash(compuTechRoomCode)}, Lot={DisplayOrDash(lotNumber)}, Variety={DisplayOrDash(variety)}, Bins={DisplayOrDash(binText)}, EffectiveDate={DisplayOrDash(effectiveDateText)}."));
                continue;
            }

            if (!IsValidCropYear(cropYearText))
            {
                previewRows.Add(Invalid(row, "CropYear", "CropYear must be a four-digit year."));
                continue;
            }

            if (!int.TryParse(binText, out var binCount) || binCount < 0)
            {
                previewRows.Add(Invalid(row, "Bins", $"Bins must be zero or a positive whole number. Read value: {DisplayOrDash(binText)}."));
                continue;
            }

            if (!IsValidEffectiveDate(effectiveDateText))
            {
                previewRows.Add(Invalid(row, "EffectiveDate", $"EffectiveDate must use YYYY-MM-DD format. Read value: {DisplayOrDash(effectiveDateText)}."));
                continue;
            }

            if (!IsValidStatus(status))
            {
                previewRows.Add(Invalid(row, "Status", $"Status must be blank, Sealed, or Open. Read value: {DisplayOrDash(status)}."));
                continue;
            }

            row.BinCount = binCount;
            row.NewBinCount = binCount;
            var warehouse = warehouses
                .OrderByDescending(x => x.Id)
                .FirstOrDefault(x => string.Equals(x.Code, facility, StringComparison.OrdinalIgnoreCase));
            if (warehouse is null)
            {
                previewRows.Add(Invalid(row, "Warehouse", $"Warehouse {facility} was not found in Master Data."));
                continue;
            }

            row.WarehouseId = warehouse.Id;
            var room = rooms.FirstOrDefault(x =>
                    x.WarehouseId == warehouse.Id
                    && !string.IsNullOrWhiteSpace(x.CompuTechRoomCode)
                    && string.Equals(NormalizeCode(x.CompuTechRoomCode), NormalizeCode(compuTechRoomCode), StringComparison.OrdinalIgnoreCase))
                ?? rooms.FirstOrDefault(x => x.WarehouseId == warehouse.Id && string.Equals(x.Code, mappedRoomCode, StringComparison.OrdinalIgnoreCase));
            if (room is null)
            {
                previewRows.Add(Invalid(row, "RoomCode", $"RoomCode {compuTechRoomCode} was not recognized. Expected mappings include evanca05 -> Evans-5, evanca12 -> Evans-12, Blueca04 -> BM-4, blueca01 -> BM-1, Evanca01 -> Evans-01, and Lambca17 -> Lamb-17."));
                continue;
            }

            row.RoomId = room.Id;
            row.NormalizedRoomCode = room.Code;
            var uploadKey = BaselineBatchKey(row.CropYear, row.RoomId.Value, lotNumber, row.Variety, row.EffectiveDate);
            if (seenRows.TryGetValue(uploadKey, out var firstRow))
            {
                previewRows.Add(Invalid(row, "Lot", $"Duplicate inventory row conflicts with CSV row {firstRow}.", "Duplicate"));
                continue;
            }
            seenRows.Add(uploadKey, rowNumber);

            var messages = new List<string>();
            if (string.IsNullOrWhiteSpace(subLocation) || subLocation == "Other EBS")
            {
                row.IsWarning = true;
                messages.Add("Sub-location could not be confidently mapped.");
            }

            if (growerIndex >= 0 && !string.IsNullOrWhiteSpace(GetCell(raw, growerIndex)))
            {
                row.Grower = GetCell(raw, growerIndex).Trim();
            }

            if (growerLotsByLot.TryGetValue(lotNumber, out var lotMatches))
            {
                if (lotMatches.Count == 1)
                {
                    var growerLot = lotMatches[0];
                    row.GrowerLotId = growerLot.Id;
                    row.Grower = string.IsNullOrWhiteSpace(row.Grower) ? growerLot.Grower : row.Grower;
                    row.PoolStart = growerLot.PoolStart ?? "";
                }
                else
                {
                    row.IsWarning = true;
                    messages.Add($"Multiple Grower Lots use Lot # {lotNumber}; import will keep the lot number but not link automatically.");
                }
            }
            var fruitProfile = fruitProfiles.FirstOrDefault(x =>
                string.Equals(x.VarietyCode, row.Variety, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Name, variety, StringComparison.OrdinalIgnoreCase));
            if (fruitProfile is null)
            {
                row.IsWarning = true;
                messages.Add("Variety was not found in Fruit Profiles; import will preserve the variety text.");
            }
            else
            {
                row.FruitProfileId = fruitProfile.Id;
                row.Variety = fruitProfile.VarietyCode;
            }

            var existingKey = CurrentStorageLotKey(row.RoomId.Value, row.LotNumber, row.Variety);
            var batchKey = BaselineBatchKey(row.CropYear, row.RoomId.Value, row.LotNumber, row.Variety, row.EffectiveDate);
            if (existingBaselineBatchByKey.TryGetValue(batchKey, out var existingBatch))
            {
                row.OldBinCount = existingBatch.NewBinCount;
                if (existingBatch.NewBinCount == binCount
                    && string.Equals(existingBatch.InventoryStatus ?? "", row.InventoryStatus, StringComparison.OrdinalIgnoreCase))
                {
                    row.Action = "Unchanged";
                    row.Message = JoinMessages("Same baseline batch already exists; importing again is not needed.", messages);
                }
                else
                {
                    row.Action = "Replace";
                    row.Message = JoinMessages($"Existing baseline batch will be replaced from {existingBatch.NewBinCount} to {binCount} bins.", messages);
                }

                previewRows.Add(row);
                continue;
            }

            if (!currentByKey.TryGetValue(existingKey, out var current))
            {
                row.Action = "Add";
                row.Message = JoinMessages("New current inventory baseline row.", messages);
                previewRows.Add(row);
                continue;
            }

            row.OldBinCount = current.NewBinCount;
            if (current.NewBinCount == binCount)
            {
                row.Action = "Unchanged";
                row.Message = JoinMessages("No changes.", messages);
            }
            else
            {
                row.Action = "Update";
                row.Message = JoinMessages($"Current bins will change from {current.NewBinCount} to {binCount}.", messages);
            }

            previewRows.Add(row);
        }

        return new RoomInventoryImportPreviewViewModel
        {
            CsvText = csvText,
            IsBuiltInSeed = isBuiltInSeed,
            Rows = previewRows,
            AddCount = previewRows.Count(x => x.Action == "Add"),
            UpdateCount = previewRows.Count(x => x.Action == "Update"),
            ReplaceBatchCount = previewRows.Count(x => x.Action == "Replace"),
            UnchangedCount = previewRows.Count(x => x.Action == "Unchanged"),
            WarningCount = previewRows.Count(x => x.IsWarning && x.Action is not "Invalid" and not "Duplicate"),
            DuplicateCount = previewRows.Count(x => x.Action == "Duplicate"),
            InvalidCount = previewRows.Count(x => x.Action == "Invalid"),
            RoomTotals = BuildRoomTotalPreview(previewRows)
        };
    }

    private async Task<IReadOnlyList<RoomInventoryCurrentLotViewModel>> GetCurrentLotsAsync(RoomInventoryImportForm filter, CancellationToken cancellationToken)
    {
        var adjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Include(x => x.Room)
                .ThenInclude(x => x.Warehouse)
            .Where(x => x.ReceiptId == null && x.AdjustmentType == StartingInventoryAdjustmentType)
            .ToListAsync(cancellationToken);
        var latest = adjustments
            .GroupBy(StartingInventoryKey)
            .Select(x => x.OrderByDescending(y => y.AdjustmentAt).ThenByDescending(y => y.Id).First())
            .Where(x => x.NewBinCount > 0)
            .Select(x => new RoomInventoryCurrentLotViewModel
            {
                RoomId = x.RoomId,
                CropYear = x.CropYear,
                Facility = x.Warehouse.Code,
                SubLocation = !string.IsNullOrWhiteSpace(x.SourceSubLocation) ? x.SourceSubLocation! : DetermineEbsSubLocation(x.Room.CropQcRoomName ?? x.Room.Code, x.SourceRoomCode ?? x.Room.CompuTechRoomCode ?? ""),
                CropQcRoomName = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                CompuTechRoomCode = x.SourceRoomCode ?? x.Room.CompuTechRoomCode ?? "",
                RoomCode = x.Room.CropQcRoomName ?? x.Room.Code,
                MasterRoomCode = x.Room.Code,
                Grower = x.GrowerName,
                LotNumber = x.LotNumber,
                PoolStart = x.PoolStart ?? "",
                Variety = x.VarietyCode ?? "",
                InventoryStatus = x.InventoryStatus ?? "",
                CurrentBins = x.NewBinCount,
                Source = x.Source ?? x.Reason ?? "",
                LastAdjustmentAt = x.AdjustmentAt
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(filter.Facility) && !filter.Facility.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            latest = latest.Where(x => x.Facility.Equals(filter.Facility, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (filter.Facility.Equals("EBS", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(filter.EbsLocation)
            && !filter.EbsLocation.Equals("All EBS", StringComparison.OrdinalIgnoreCase))
        {
            latest = latest.Where(x => x.SubLocation.Equals(filter.EbsLocation, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.RoomCode))
        {
            latest = latest.Where(x => x.CropQcRoomName.Contains(filter.RoomCode, StringComparison.OrdinalIgnoreCase) || x.CompuTechRoomCode.Contains(filter.RoomCode, StringComparison.OrdinalIgnoreCase) || x.MasterRoomCode.Contains(filter.RoomCode, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.LotNumber))
        {
            latest = latest.Where(x => x.LotNumber.Contains(filter.LotNumber, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Grower))
        {
            latest = latest.Where(x => x.Grower.Contains(filter.Grower, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Variety))
        {
            latest = latest.Where(x => x.Variety.Contains(filter.Variety, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return latest
            .OrderBy(x => x.Facility)
            .ThenBy(x => x.SubLocation)
            .ThenBy(x => x.RoomCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LotNumber)
            .ToList();
    }

    private async Task UpdateEbsRoomMetadataAsync(IEnumerable<RoomInventoryImportPreviewRow> rows, CancellationToken cancellationToken)
    {
        var metadataRows = rows
            .Where(x => x.RoomId is not null)
            .GroupBy(x => x.RoomId!.Value)
            .Select(x => x.First())
            .ToList();
        if (metadataRows.Count == 0)
        {
            return;
        }

        var roomIds = metadataRows.Select(x => x.RoomId!.Value).ToList();
        var rooms = await dbContext.Rooms.Where(x => roomIds.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var row in metadataRows)
        {
            var room = rooms.SingleOrDefault(x => x.Id == row.RoomId);
            if (room is null)
            {
                continue;
            }

            room.SubLocation = row.SubLocation;
            room.CropQcRoomName = row.CropQcRoomName;
            room.CompuTechRoomCode = row.CompuTechRoomCode;
            room.DisplayName = row.CropQcRoomName;
            room.SortOrder = EbsRoomSortOrder(row.SubLocation, row.CropQcRoomName);
        }
    }

    private static int EbsRoomSortOrder(string subLocation, string cropQcRoomName)
    {
        var locationBase = subLocation.Equals("Evans", StringComparison.OrdinalIgnoreCase) ? 1000
            : subLocation.Equals("BM", StringComparison.OrdinalIgnoreCase) ? 2000
            : subLocation.Equals("Lamb", StringComparison.OrdinalIgnoreCase) ? 3000
            : 9000;
        var digits = new string(cropQcRoomName.Where(char.IsDigit).ToArray());
        return locationBase + (int.TryParse(digits, out var roomNumber) ? roomNumber : 999);
    }

    private async Task<string> ReadCsvTextAsync(RoomInventoryImportForm form, CancellationToken cancellationToken)
    {
        if (form.UseBuiltInSeed)
        {
            var path = Path.Combine(environment.ContentRootPath, "Data", "Seed", BuiltInEbsSeedFileName);
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(form.CsvText))
        {
            return form.CsvText;
        }

        if (form.CsvFile is null || form.CsvFile.Length == 0)
        {
            return "";
        }

        using var reader = new StreamReader(form.CsvFile.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static RoomInventoryImportPreviewViewModel ServerFailurePreview(string referenceId, string safeMessage) => new()
    {
        InvalidCount = 1,
        Rows =
        [
            Invalid(new RoomInventoryImportPreviewRow { RowNumber = 0 }, "Server", $"Import failed before it could complete. Reference {referenceId}. {safeMessage}")
        ]
    };

    private static RoomInventoryImportPreviewRow Invalid(RoomInventoryImportPreviewRow row, string column, string message, string action = "Invalid")
    {
        row.Column = column;
        row.Action = action;
        row.Message = message;
        return row;
    }

    private static string MissingRequiredColumn(params (string Value, string Column)[] values) =>
        values.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Value)).Column ?? "Required";

    private static string StartingInventoryKey(RoomInventoryAdjustment adjustment) =>
        CurrentStorageLotKey(adjustment.RoomId, adjustment.LotNumber, adjustment.VarietyCode ?? "");

    private static string BaselineBatchKey(RoomInventoryAdjustment adjustment) =>
        BaselineBatchKey(adjustment.CropYear ?? 0, adjustment.RoomId, adjustment.LotNumber, adjustment.VarietyCode ?? "", adjustment.AdjustmentAt);

    private static string BaselineBatchKey(int cropYear, int roomId, string lotNumber, string variety, DateTimeOffset effectiveDate) =>
        $"{cropYear}|{roomId}|{lotNumber.Trim().ToUpperInvariant()}|{NormalizeVariety(variety)}|{effectiveDate:yyyy-MM-dd}";

    private static IReadOnlyList<RoomInventoryImportRoomTotalPreview> BuildRoomTotalPreview(IReadOnlyList<RoomInventoryImportPreviewRow> rows) =>
        rows
            .Where(x => x.Action is "Add" or "Update" or "Replace" or "Unchanged")
            .Where(x => x.RoomId is not null && x.NewBinCount is not null)
            .GroupBy(x => new { x.CropYear, x.Facility, x.NormalizedRoomCode, x.Variety, x.InventoryStatus, EffectiveDate = x.EffectiveDate.Date })
            .Select(x => new RoomInventoryImportRoomTotalPreview(
                x.Key.CropYear,
                x.Key.Facility,
                x.Key.NormalizedRoomCode,
                x.Key.Variety,
                string.IsNullOrWhiteSpace(x.Key.InventoryStatus) ? "-" : x.Key.InventoryStatus,
                x.First().EffectiveDate,
                x.Count(),
                x.Sum(y => y.NewBinCount ?? 0)))
            .OrderBy(x => x.Warehouse)
            .ThenBy(x => x.RoomCode)
            .ThenBy(x => x.Variety)
            .ThenBy(x => x.Status)
            .ToList();

    private static DateTimeOffset ParseEffectiveDate(string value, DateTimeOffset fallback) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed)
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedOffset)
                ? parsedOffset
                : fallback;

    private static int ParseCropYear(string value, int effectiveDateCropYear, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 2000
            ? parsed
            : effectiveDateCropYear > 2000 ? effectiveDateCropYear : fallback;

    private static bool IsValidCropYear(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 2000 and <= 2100;

    private static bool IsValidEffectiveDate(string value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsValidStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("Sealed", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Open", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatus(string value) =>
        value.Equals("Sealed", StringComparison.OrdinalIgnoreCase) ? "Sealed"
            : value.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "Open"
            : "";

    private static IReadOnlyList<string> MissingRequiredHeaders(params (int Index, string Name)[] headers) =>
        headers.Where(x => x.Index < 0).Select(x => x.Name).ToList();

    private static string DisplayOrDash(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    public static string CurrentStorageLotKey(int roomId, string lotNumber, string variety) =>
        $"{roomId}|{lotNumber.Trim().ToUpperInvariant()}|{NormalizeVariety(variety)}";

    private static string JoinMessages(string first, IReadOnlyList<string> messages) =>
        messages.Count == 0 ? first : $"{first} {string.Join(" ", messages)}";

    public static string DetermineEbsSubLocation(string cropQcRoomName, string compuTechRoomCode = "")
    {
        var normalized = NormalizeCode($"{cropQcRoomName} {compuTechRoomCode}");
        if (normalized.StartsWith("EVANCA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("EVANS", StringComparison.OrdinalIgnoreCase)) return "Evans";
        if (normalized.StartsWith("LAMBCA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("LAMB", StringComparison.OrdinalIgnoreCase)) return "Lamb";
        if (normalized.StartsWith("BLUECA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("BM", StringComparison.OrdinalIgnoreCase) || normalized.Contains("BLUEMOUNTAIN", StringComparison.OrdinalIgnoreCase)) return "BM";
        return "Other EBS";
    }

    public static string MapCompuTechRoomCode(string roomCode)
    {
        var normalized = NormalizeCode(roomCode);
        return TryMap("EVANCA", "EVANS-", normalized)
            ?? TryMap("BLUECA", "BM-", normalized)
            ?? TryMap("LAMBCA", "LAMB-", normalized)
            ?? normalized;
    }

    public static string CropQcRoomNameForCompuTechCode(string compuTechRoomCode) =>
        NormalizeCode(compuTechRoomCode) switch
        {
            "EVANCA05" => "Evans-5",
            "EVANCA12" => "Evans-12",
            "BLUECA04" => "BM-4",
            "BLUECA06" => "BM-6",
            "BLUECA01" => "BM-1",
            "EVANCA01" => "Evans-01",
            "LAMBCA17" => "Lamb-17",
            _ => NormalizeCropQcRoomName(compuTechRoomCode)
        };

    public static string MasterRoomCodeFor(string cropQcRoomName, string compuTechRoomCode) =>
        NormalizeCode(compuTechRoomCode) switch
        {
            "EVANCA05" => "EVANS-5",
            "EVANCA12" => "EVANS-12",
            "BLUECA04" => "BM-4",
            "BLUECA06" => "BM-6",
            "BLUECA01" => "BM-1",
            "EVANCA01" => "EVANS-1",
            "LAMBCA17" => "LAMB-17",
            _ => NormalizeCropQcRoomName(cropQcRoomName)
        };

    public static string NormalizeCropQcRoomName(string roomName)
    {
        var normalized = NormalizeCode(roomName);
        return normalized switch
        {
            "EVANS5" => "Evans-5",
            "EVANS05" => "Evans-5",
            "EVANS12" => "Evans-12",
            "BM4" => "BM-4",
            "BM6" => "BM-6",
            "BM1" => "BM-1",
            "BLUEMOUNTAINROOM4" => "BM-4",
            "BLUEMOUNTAIN4" => "BM-4",
            "BLUEMTROOM4" => "BM-4",
            "BLUEMT4" => "BM-4",
            "BLUEMOUNTAINROOM6" => "BM-6",
            "BLUEMOUNTAIN6" => "BM-6",
            "BLUEMTROOM6" => "BM-6",
            "BLUEMT6" => "BM-6",
            "EVANS1" => "Evans-01",
            "EVANS01" => "Evans-01",
            "LAMB17" => "Lamb-17",
            _ => roomName.Trim()
        };
    }

    private static string? TryMap(string prefix, string targetPrefix, string normalized)
    {
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = normalized[prefix.Length..].TrimStart('0');
        return string.IsNullOrWhiteSpace(suffix) ? null : $"{targetPrefix}{suffix}";
    }

    private static string NormalizeVariety(string variety)
    {
        var trimmed = variety.Trim();
        return trimmed.Equals("pink", StringComparison.OrdinalIgnoreCase) ? "PINK"
            : trimmed.Equals("red", StringComparison.OrdinalIgnoreCase) ? "RED"
            : trimmed.ToUpperInvariant();
    }

    private static string NormalizeCode(string value) =>
        value.Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToUpperInvariant();

    private static string NormalizeHeader(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int FindHeader(IReadOnlyList<string> headers, IReadOnlyList<string> candidates)
    {
        var normalizedCandidates = candidates.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            if (normalizedCandidates.Contains(headers[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : "";

    private static IEnumerable<IReadOnlyList<string>> ParseCsv(string text)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                yield return row;
                row = [];
                cell.Clear();
                continue;
            }

            cell.Append(ch);
        }

        row.Add(cell.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            yield return row;
        }
    }
}
