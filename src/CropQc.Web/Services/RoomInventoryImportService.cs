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
}

public sealed class RoomInventoryImportService(CropQcDbContext dbContext, IWebHostEnvironment environment) : IRoomInventoryImportService
{
    public const string BuiltInEbsSeedFileName = "ebs-starting-room-inventory.csv";
    public const string StartingInventoryAdjustmentType = "StartingInventoryImport";
    public const string DefaultStartingInventorySource = "Compu-Tech Starting Inventory";

    public async Task<RoomInventoryImportPageViewModel> GetPageAsync(RoomInventoryImportForm filter, CancellationToken cancellationToken)
    {
        return new RoomInventoryImportPageViewModel
        {
            Form = filter,
            CurrentLots = await GetCurrentLotsAsync(filter, cancellationToken)
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
            return (preview, "Confirm Import EBS Starting Inventory before applying changes.");
        }

        if (!preview.CanApply)
        {
            return (preview, "Resolve invalid or duplicate rows before importing room inventory.");
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in preview.Rows.Where(x => x.Action is "Add" or "Update"))
        {
            var oldCount = row.OldBinCount;
            var newCount = row.NewBinCount ?? 0;
            var adjustment = new RoomInventoryAdjustment
            {
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
                Reason = row.Source,
                Notes = $"Imported from {row.Source}; Crop QC room {row.CropQcRoomName}; Compu-Tech room {row.CompuTechRoomCode}; mapped to master room {row.NormalizedRoomCode}.",
                AdjustmentAt = now,
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
                BeforeValuesJson = oldCount is null ? null : JsonSerializer.Serialize(new { row.Facility, row.SubLocation, row.CropQcRoomName, row.CompuTechRoomCode, row.LotNumber, row.Variety, BinCount = oldCount }),
                AfterValuesJson = JsonSerializer.Serialize(new { row.Facility, row.SubLocation, row.CropQcRoomName, row.CompuTechRoomCode, row.LotNumber, row.Variety, BinCount = newCount, row.Source }),
                SourceApplication = "Web",
                CreatedAt = now
            });
        }

        await UpdateEbsRoomMetadataAsync(preview.Rows.Where(x => x.Action is "Add" or "Update" or "Unchanged"), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (preview, null);
    }

    private async Task<RoomInventoryImportPreviewViewModel> BuildPreviewAsync(string csvText, bool isBuiltInSeed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            return new RoomInventoryImportPreviewViewModel
            {
                CsvText = "",
                IsBuiltInSeed = isBuiltInSeed,
                InvalidCount = 1,
                Rows = [new() { RowNumber = 0, Action = "Invalid", Message = "Upload a CSV or use the built-in EBS starting inventory file." }]
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
                Rows = [new() { RowNumber = 0, Action = "Invalid", Message = "CSV must include a header row and at least one inventory row." }]
            };
        }

        var headers = parsedRows[0].Select(NormalizeHeader).ToList();
        var facilityIndex = FindHeader(headers, ["facility"]);
        var subLocationIndex = FindHeader(headers, ["sublocation", "location"]);
        var cropQcRoomIndex = FindHeader(headers, ["cropqcroomname", "cropqcname", "cropqcroom", "displayroom", "roomname"]);
        var compuTechRoomIndex = FindHeader(headers, ["computechroomcode", "compu-techroomcode", "roomcode", "room", "compu-techroom", "computechroom"]);
        var varietyIndex = FindHeader(headers, ["variety"]);
        var lotIndex = FindHeader(headers, ["lotnumber", "lot#", "lot", "growernumber", "grower#"]);
        var binIndex = FindHeader(headers, ["bincount", "count", "bins"]);
        var sourceIndex = FindHeader(headers, ["source"]);
        if (facilityIndex < 0 || compuTechRoomIndex < 0 || varietyIndex < 0 || lotIndex < 0 || binIndex < 0)
        {
            return new RoomInventoryImportPreviewViewModel
            {
                CsvText = csvText,
                IsBuiltInSeed = isBuiltInSeed,
                InvalidCount = 1,
                Rows = [new() { RowNumber = 0, Action = "Invalid", Message = "CSV headers must include Facility, CompuTechRoomCode (or RoomCode), Variety, LotNumber, and BinCount. CropQcRoomName, SubLocation, and Source are recommended." }]
            };
        }

        var warehouses = await dbContext.Warehouses.AsNoTracking().ToListAsync(cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).ToListAsync(cancellationToken);
        var growerLotsByLot = (await dbContext.GrowerLots.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(x => x.LotNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var fruitProfiles = await dbContext.FruitProfiles.AsNoTracking().ToListAsync(cancellationToken);
        var currentByKey = (await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.ReceiptId == null && x.AdjustmentType == StartingInventoryAdjustmentType)
                .ToListAsync(cancellationToken))
            .GroupBy(StartingInventoryKey)
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
            var source = sourceIndex >= 0 ? GetCell(raw, sourceIndex).Trim() : DefaultStartingInventorySource;
            source = string.IsNullOrWhiteSpace(source) ? DefaultStartingInventorySource : source;
            var subLocation = subLocationIndex >= 0 ? GetCell(raw, subLocationIndex).Trim() : "";
            cropQcRoomName = string.IsNullOrWhiteSpace(cropQcRoomName) ? CropQcRoomNameForCompuTechCode(compuTechRoomCode) : NormalizeCropQcRoomName(cropQcRoomName);
            subLocation = string.IsNullOrWhiteSpace(subLocation) ? DetermineEbsSubLocation(cropQcRoomName, compuTechRoomCode) : subLocation;
            var mappedRoomCode = MasterRoomCodeFor(cropQcRoomName, compuTechRoomCode);
            var normalizedRoomCode = NormalizeCode(mappedRoomCode);
            var row = new RoomInventoryImportPreviewRow
            {
                RowNumber = rowNumber,
                Facility = facility,
                SubLocation = subLocation,
                CropQcRoomName = cropQcRoomName,
                CompuTechRoomCode = compuTechRoomCode,
                RoomCode = cropQcRoomName,
                NormalizedRoomCode = mappedRoomCode,
                Variety = NormalizeVariety(variety),
                LotNumber = lotNumber,
                Source = source
            };

            if (string.IsNullOrWhiteSpace(facility) || string.IsNullOrWhiteSpace(compuTechRoomCode) || string.IsNullOrWhiteSpace(cropQcRoomName) || string.IsNullOrWhiteSpace(variety) || string.IsNullOrWhiteSpace(lotNumber))
            {
                previewRows.Add(Invalid(row, "Facility, CropQcRoomName, CompuTechRoomCode, Variety, and LotNumber are required."));
                continue;
            }

            if (!int.TryParse(binText, out var binCount) || binCount < 0)
            {
                previewRows.Add(Invalid(row, "BinCount must be zero or a positive whole number."));
                continue;
            }

            row.BinCount = binCount;
            row.NewBinCount = binCount;
            if (!string.Equals(facility, "EBS", StringComparison.OrdinalIgnoreCase))
            {
                previewRows.Add(Invalid(row, "This starting inventory import currently supports EBS rows only."));
                continue;
            }

            var uploadKey = $"{facility}|{NormalizeCode(cropQcRoomName)}|{NormalizeCode(compuTechRoomCode)}|{lotNumber}|{row.Variety}|{source}";
            if (seenRows.TryGetValue(uploadKey, out var firstRow))
            {
                previewRows.Add(Invalid(row, $"Duplicate inventory row conflicts with CSV row {firstRow}.", "Duplicate"));
                continue;
            }
            seenRows.Add(uploadKey, rowNumber);

            var warehouse = warehouses.SingleOrDefault(x => string.Equals(x.Code, facility, StringComparison.OrdinalIgnoreCase));
            if (warehouse is null)
            {
                previewRows.Add(Invalid(row, $"Warehouse/facility {facility} was not found in Master Data."));
                continue;
            }

            row.WarehouseId = warehouse.Id;
            var room = rooms.SingleOrDefault(x => x.WarehouseId == warehouse.Id && string.Equals(x.Code, mappedRoomCode, StringComparison.OrdinalIgnoreCase));
            if (room is null)
            {
                previewRows.Add(Invalid(row, $"Room {cropQcRoomName} / Compu-Tech code {compuTechRoomCode} was not recognized. Expected mappings include evanca05 -> Evans-5, evanca12 -> Evans-12, Blueca04 -> BM-4, blueca01 -> BM-1, Evanca01 -> Evans-01, and Lambca17 -> Lamb-17."));
                continue;
            }

            row.RoomId = room.Id;
            row.NormalizedRoomCode = room.Code;
            var messages = new List<string>();
            if (string.IsNullOrWhiteSpace(subLocation) || subLocation == "Other EBS")
            {
                row.IsWarning = true;
                messages.Add("Sub-location could not be confidently mapped.");
            }

            if (growerLotsByLot.TryGetValue(lotNumber, out var lotMatches))
            {
                if (lotMatches.Count == 1)
                {
                    var growerLot = lotMatches[0];
                    row.GrowerLotId = growerLot.Id;
                    row.Grower = growerLot.Grower;
                    row.PoolStart = growerLot.PoolStart ?? "";
                }
                else
                {
                    row.IsWarning = true;
                    messages.Add($"Multiple Grower Lots use Lot # {lotNumber}; import will keep the lot number but not link automatically.");
                }
            }
            else
            {
                row.IsWarning = true;
                messages.Add("Grower not found in Master Data.");
            }

            var fruitProfile = fruitProfiles.SingleOrDefault(x =>
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

            var existingKey = StartingInventoryKey(row.RoomId.Value, row.LotNumber, row.Variety, row.Source, row.CompuTechRoomCode);
            if (!currentByKey.TryGetValue(existingKey, out var current))
            {
                row.Action = "Add";
                row.Message = JoinMessages("New starting inventory row.", messages);
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
            UnchangedCount = previewRows.Count(x => x.Action == "Unchanged"),
            WarningCount = previewRows.Count(x => x.IsWarning && x.Action is not "Invalid" and not "Duplicate"),
            DuplicateCount = previewRows.Count(x => x.Action == "Duplicate"),
            InvalidCount = previewRows.Count(x => x.Action == "Invalid")
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

    private static RoomInventoryImportPreviewRow Invalid(RoomInventoryImportPreviewRow row, string message, string action = "Invalid")
    {
        row.Action = action;
        row.Message = message;
        return row;
    }

    private static string StartingInventoryKey(RoomInventoryAdjustment adjustment) =>
        StartingInventoryKey(adjustment.RoomId, adjustment.LotNumber, adjustment.VarietyCode ?? "", adjustment.Source ?? adjustment.Reason ?? "", adjustment.SourceRoomCode ?? "");

    private static string StartingInventoryKey(int roomId, string lotNumber, string variety, string source, string compuTechRoomCode = "") =>
        $"{roomId}|{NormalizeCode(compuTechRoomCode)}|{lotNumber.Trim().ToUpperInvariant()}|{NormalizeVariety(variety)}|{source.Trim().ToUpperInvariant()}";

    private static string JoinMessages(string first, IReadOnlyList<string> messages) =>
        messages.Count == 0 ? first : $"{first} {string.Join(" ", messages)}";

    public static string DetermineEbsSubLocation(string cropQcRoomName, string compuTechRoomCode = "")
    {
        var normalized = NormalizeCode($"{cropQcRoomName} {compuTechRoomCode}");
        if (normalized.StartsWith("EVANCA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("EVANS", StringComparison.OrdinalIgnoreCase)) return "Evans";
        if (normalized.StartsWith("LAMBCA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("LAMB", StringComparison.OrdinalIgnoreCase)) return "Lamb";
        if (normalized.StartsWith("BLUECA", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("BM", StringComparison.OrdinalIgnoreCase)) return "BM";
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
            "BLUECA01" => "BM-1",
            "EVANCA01" => "EVANS-1",
            "LAMBCA17" => "LAMB-17",
            _ => NormalizeCode(cropQcRoomName)
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
            "BM1" => "BM-1",
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
