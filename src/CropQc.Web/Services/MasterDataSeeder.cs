using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IMasterDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

public sealed class MasterDataSeeder(CropQcDbContext dbContext, ILogger<MasterDataSeeder> logger) : IMasterDataSeeder
{
    public const int ExpectedSeededRoomCount = 68;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Master data seed started.");

        var roomsBeforeSeed = await dbContext.Rooms.CountAsync(cancellationToken);
        await SeedRolesAsync(cancellationToken);
        var warehouses = await SeedWarehousesAsync(cancellationToken);
        var roomsAdded = await SeedRoomsAsync(warehouses, cancellationToken);
        await SeedGradesAsync(cancellationToken);
        await SeedDefectsAsync(cancellationToken);
        await SeedSampleTypesAsync(cancellationToken);
        await SeedFruitProfilesAsync(cancellationToken);
        await SeedStarchScaleAsync(cancellationToken);
        await SeedSizeThresholdsAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var warehouseCount = await dbContext.Warehouses.CountAsync(cancellationToken);
        var roomCount = await dbContext.Rooms.CountAsync(cancellationToken);
        var fruitProfileCount = await dbContext.FruitProfiles.CountAsync(cancellationToken);
        var gradeCount = await dbContext.Grades.CountAsync(cancellationToken);
        var defectCount = await dbContext.DefectTypes.CountAsync(cancellationToken);
        logger.LogInformation(
            "Master data seed completed. Warehouses: {WarehouseCount}; Rooms before seed: {RoomsBeforeSeed}; Rooms added: {RoomsAdded}; Rooms after seed: {RoomCount}; Fruit profiles: {FruitProfileCount}; Grades: {GradeCount}; Defects: {DefectCount}.",
            warehouseCount,
            roomsBeforeSeed,
            roomsAdded,
            roomCount,
            fruitProfileCount,
            gradeCount,
            defectCount);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in new[]
        {
            ("Admin", "Full dashboard and configuration access."),
            ("Manager", "Manage QC receiving workflows and resend summaries."),
            ("QC User", "Capture receiving samples and QC readings."),
            ("Viewer", "Read-only dashboard access.")
        })
        {
            if (!await dbContext.Roles.AnyAsync(x => x.Name == role.Item1, cancellationToken))
            {
                dbContext.Roles.Add(new Role { Name = role.Item1, Description = role.Item2, IsSystemRole = true });
            }
        }
    }

    private async Task<Dictionary<string, Warehouse>> SeedWarehousesAsync(CancellationToken cancellationToken)
    {
        foreach (var warehouse in new[] { ("EBS", "EBS"), ("DH", "DH"), ("McDougall", "McDougall"), ("WP", "WP") })
        {
            if (!await dbContext.Warehouses.AnyAsync(x => x.Code == warehouse.Item1, cancellationToken))
            {
                dbContext.Warehouses.Add(new Warehouse { Code = warehouse.Item1, Name = warehouse.Item2, IsActive = true });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var warehouses = await dbContext.Warehouses.ToListAsync(cancellationToken);
        return warehouses
            .GroupBy(x => NormalizeCode(x.Code), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> SeedRoomsAsync(IReadOnlyDictionary<string, Warehouse> warehouses, CancellationToken cancellationToken)
    {
        logger.LogInformation("Room seed started. Rooms before seed: {RoomsBeforeSeed}.", await dbContext.Rooms.CountAsync(cancellationToken));
        var missingWarehouseCodes = RoomSeeds()
            .Select(x => NormalizeCode(x.WarehouseCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !warehouses.ContainsKey(x))
            .OrderBy(x => x)
            .ToList();
        foreach (var warehouseCode in missingWarehouseCodes)
        {
            logger.LogWarning("Room seed missing warehouse code {WarehouseCode}.", warehouseCode);
        }

        var existingRoomKeys = await dbContext.Rooms
            .Select(x => new { x.Id, x.WarehouseId, x.Code })
            .ToListAsync(cancellationToken);
        var existingKeys = existingRoomKeys
            .Select(x => RoomKey(x.WarehouseId, x.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var repaired = 0;

        foreach (var room in RoomSeeds())
        {
            if (!warehouses.TryGetValue(NormalizeCode(room.WarehouseCode), out var warehouse))
            {
                logger.LogWarning("Room seed skipped for {RoomCode}; warehouse code {WarehouseCode} was not found.", room.Code, room.WarehouseCode);
                continue;
            }

            var key = RoomKey(warehouse.Id, room.Code);
            if (!existingKeys.Contains(key))
            {
                dbContext.Rooms.Add(new Room
                {
                    WarehouseId = warehouse.Id,
                    Code = room.Code,
                    Name = room.Name,
                    CapacityBins = 0,
                    IsActive = true
                });
                existingKeys.Add(key);
                added++;
                continue;
            }

            var existingRoomId = existingRoomKeys.FirstOrDefault(x => string.Equals(RoomKey(x.WarehouseId, x.Code), key, StringComparison.OrdinalIgnoreCase))?.Id;
            if (existingRoomId is null)
            {
                continue;
            }

            var existingRoom = await dbContext.Rooms.FindAsync([existingRoomId.Value], cancellationToken);
            if (existingRoom is null)
            {
                continue;
            }

            var changed = false;
            if (string.IsNullOrWhiteSpace(existingRoom.Name))
            {
                existingRoom.Name = room.Name;
                changed = true;
            }

            if (existingRoom.CapacityBins < 0)
            {
                existingRoom.CapacityBins = 0;
                changed = true;
            }

            if (changed)
            {
                repaired++;
            }
        }

        if (added > 0 || repaired > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var roomsAfterSeed = await dbContext.Rooms.CountAsync(cancellationToken);
        logger.LogInformation(
            "Room seed completed. Expected rooms: {ExpectedRoomCount}; Missing warehouse codes: {MissingWarehouseCodes}; Rooms added: {RoomsAdded}; Rooms repaired: {RoomsRepaired}; Rooms after seed: {RoomsAfterSeed}.",
            ExpectedSeededRoomCount,
            missingWarehouseCodes.Count == 0 ? "(none)" : string.Join(", ", missingWarehouseCodes),
            added,
            repaired,
            roomsAfterSeed);
        return added;
    }

    private async Task SeedGradesAsync(CancellationToken cancellationToken)
    {
        foreach (var code in new[] { "W1", "W2", "W3", "W4", "WF", "US1", "US2", "USF" })
        {
            if (!await dbContext.Grades.AnyAsync(x => x.Code == code, cancellationToken))
            {
                dbContext.Grades.Add(new Grade { Code = code, Name = code, IsActive = true });
            }
        }
    }

    private async Task SeedDefectsAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { "Bruise", "Sunburn", "Bitter Pit", "Scald", "Decay", "Puncture", "Watercore", "Limb Rub", "Stem Bowl Crack", "Internal Browning", "Other" })
        {
            if (!await dbContext.DefectTypes.AnyAsync(x => x.Name == name, cancellationToken))
            {
                dbContext.DefectTypes.Add(new DefectType { Name = name, IsActive = true });
            }
        }
    }

    private async Task SeedSampleTypesAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { "Receiving Sample", "Door Sample", "Line QC Sample" })
        {
            if (!await dbContext.SampleTypes.AnyAsync(x => x.Name == name, cancellationToken))
            {
                dbContext.SampleTypes.Add(new SampleType { Name = name, IsActive = true });
            }
        }
    }

    private async Task SeedFruitProfilesAsync(CancellationToken cancellationToken)
    {
        foreach (var fruit in FruitProfileSeeds())
        {
            if (!await dbContext.FruitProfiles.AnyAsync(x => x.VarietyCode == fruit.VarietyCode, cancellationToken))
            {
                dbContext.FruitProfiles.Add(new FruitProfile
                {
                    Name = fruit.Name,
                    Description = fruit.Name,
                    VarietyCode = fruit.VarietyCode,
                    FruitType = fruit.FruitType,
                    ProductionType = fruit.IsOrganic ? "Organic" : "Conventional",
                    IsOrganic = fruit.IsOrganic,
                    IsActive = true
                });
            }
        }
    }

    private async Task SeedStarchScaleAsync(CancellationToken cancellationToken)
    {
        var scale = await dbContext.StarchScales.SingleOrDefaultAsync(x => x.Name == "6-point starch scale" && x.FruitType == null && x.FruitProfileId == null, cancellationToken);
        if (scale is null)
        {
            scale = new StarchScale { Name = "6-point starch scale", IsActive = true };
            dbContext.StarchScales.Add(scale);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var values = new[] { 1.0m, 1.2m, 1.5m, 1.8m, 2.0m, 2.5m, 3.0m, 3.5m, 4.0m, 4.5m, 5.0m, 6.0m };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (!await dbContext.StarchScaleValues.AnyAsync(x => x.StarchScaleId == scale.Id && x.Value == value, cancellationToken))
            {
                dbContext.StarchScaleValues.Add(new StarchScaleValue { StarchScaleId = scale.Id, Value = value, SortOrder = (i + 1) * 10, IsActive = true });
            }
        }
    }

    private async Task SeedSizeThresholdsAsync(CancellationToken cancellationToken)
    {
        foreach (var threshold in SizeThresholdSeeds())
        {
            if (!await dbContext.FruitSizeConversionThresholds.AnyAsync(x => x.FruitType == threshold.FruitType && x.SizeCategory == threshold.SizeCategory, cancellationToken))
            {
                dbContext.FruitSizeConversionThresholds.Add(new FruitSizeConversionThreshold
                {
                    FruitType = threshold.FruitType,
                    SizeCategory = threshold.SizeCategory,
                    MinimumWeightGrams = threshold.MinimumWeightGrams,
                    IsActive = true
                });
            }
        }
    }

    private static IReadOnlyList<(string WarehouseCode, string Code, string Name)> RoomSeeds() =>
    [
        ("WP", "WP-4", "Room 4"), ("WP", "WP-5", "Room 5"), ("WP", "WP-6", "Room 6"), ("WP", "WP-7", "Room 7"), ("WP", "WP-8", "Room 8"),
        ("EBS", "LAMB-13", "Lamb Street 13"), ("EBS", "LAMB-14", "Lamb Street 14"), ("EBS", "LAMB-15", "Lamb Street 15"), ("EBS", "LAMB-16", "Lamb Street 16"), ("EBS", "LAMB-17", "Lamb Street 17"),
        ("EBS", "EVANS-1", "Evans Street 1"), ("EBS", "EVANS-2", "Evans Street 2"), ("EBS", "EVANS-3", "Evans Street 3"), ("EBS", "EVANS-4", "Evans Street 4"), ("EBS", "EVANS-5", "Evans Street 5"), ("EBS", "EVANS-6", "Evans Street 6"), ("EBS", "EVANS-7", "Evans Street 7"), ("EBS", "EVANS-8", "Evans Street 8"), ("EBS", "EVANS-9", "Evans Street 9"), ("EBS", "EVANS-10", "Evans Street 10"), ("EBS", "EVANS-11", "Evans Street 11"), ("EBS", "EVANS-12", "Evans Street 12"), ("EBS", "EVANS-BKT", "Evans Street BKT"), ("EBS", "EVANS-BACKSIDE", "Evans Street Backside"), ("EBS", "EVANS-HALLWAY1", "Evans Street Hallway 1"), ("EBS", "EVANS-HALLWAY2", "Evans Street Hallway 2"),
        ("EBS", "BM-1", "Bluemountain 1"), ("EBS", "BM-2", "Bluemountain 2"), ("EBS", "BM-3", "Bluemountain 3"), ("EBS", "BM-4", "Bluemountain 4"), ("EBS", "BM-5", "Bluemountain 5"), ("EBS", "BM-6", "Bluemountain 6"),
        ("DH", "DH-1", "Room 1"), ("DH", "DH-2", "Room 2"), ("DH", "DH-3", "Room 3"), ("DH", "DH-4", "Room 4"), ("DH", "DH-5", "Room 5"), ("DH", "DH-6", "Room 6"), ("DH", "DH-7", "Room 7"), ("DH", "DH-8", "Room 8"), ("DH", "DH-9", "Room 9"), ("DH", "DH-10", "Room 10"), ("DH", "DH-11", "Room 11"), ("DH", "DH-12", "Room 12"), ("DH", "DH-13", "Room 13"), ("DH", "DH-14", "Room 14"), ("DH", "DH-15", "Room 15"), ("DH", "DH-16", "Room 16"), ("DH", "DH-17", "Room 17"), ("DH", "DH-18", "Room 18"), ("DH", "DH-19", "Room 19"), ("DH", "DH-20", "Room 20"), ("DH", "DH-21", "Room 21"), ("DH", "DH-22", "Room 22"),
        ("McDougall", "MCD-3", "Room 3"), ("McDougall", "MCD-4", "Room 4"), ("McDougall", "MCD-5", "Room 5"), ("McDougall", "MCD-6", "Room 6"), ("McDougall", "MCD-7", "Room 7"), ("McDougall", "MCD-8", "Room 8"), ("McDougall", "MCD-9", "Room 9"), ("McDougall", "MCD-10", "Room 10"), ("McDougall", "MCD-11", "Room 11"), ("McDougall", "MCD-12", "Room 12"), ("McDougall", "MCD-13", "Room 13"), ("McDougall", "MCD-14", "Room 14"), ("McDougall", "MCD-15", "Room 15"), ("McDougall", "MCD-16", "Room 16")
    ];

    private static IReadOnlyList<(string Name, string VarietyCode, string FruitType, bool IsOrganic)> FruitProfileSeeds() =>
    [
        ("Fuji", "FUJI", "Apple", false), ("Gala", "GALA", "Apple", false), ("Golden Delicious", "GOLD", "Apple", false), ("Granny Smith", "GSMT", "Apple", false), ("Honey Crisp", "HONY", "Apple", false),
        ("Organic Fuji", "ORFU", "Apple", true), ("Organic Gala", "ORGA", "Apple", true), ("Organic Golden Delicious", "ORGD", "Apple", true), ("Organic Granny Smith", "ORGS", "Apple", true), ("Organic Honey Crisp", "ORHC", "Apple", true), ("Organic Pink Lady", "ORPL", "Apple", true), ("Organic Red Delicious", "ORRD", "Apple", true),
        ("Pink Lady", "PINK", "Apple", false), ("Red Delicious", "RED", "Apple", false), ("Mardi Gras", "MDGS", "Pear", false), ("Bosc", "BOSC", "Pear", false), ("Bartlett", "BART", "Pear", false), ("D'Anjou", "DANJ", "Pear", false), ("Organic Bartlett", "ORBA", "Pear", true), ("Organic Bosc", "ORBO", "Pear", true), ("Organic D'anjou", "ORDA", "Pear", true), ("Autumn Glory", "ATGL", "Apple", false)
    ];

    private static IReadOnlyList<(string FruitType, int SizeCategory, decimal MinimumWeightGrams)> SizeThresholdSeeds() =>
    [
        ("Apple", 48, 405.0000m), ("Apple", 56, 354.0000m), ("Apple", 64, 298.0000m), ("Apple", 72, 264.0000m), ("Apple", 80, 238.0000m), ("Apple", 88, 215.0000m), ("Apple", 100, 190.0000m), ("Apple", 113, 167.0000m), ("Apple", 125, 153.0000m), ("Apple", 138, 136.0000m), ("Apple", 150, 128.0000m), ("Apple", 163, 116.0000m), ("Apple", 175, 108.0000m), ("Apple", 198, 96.0000m), ("Apple", 216, 88.0000m),
        ("Pear", 50, 360.0000m), ("Pear", 60, 303.0000m), ("Pear", 70, 260.0000m), ("Pear", 80, 227.0000m), ("Pear", 90, 203.0000m), ("Pear", 100, 182.0000m), ("Pear", 110, 165.0000m), ("Pear", 120, 151.0000m), ("Pear", 135, 135.0000m), ("Pear", 150, 121.0000m), ("Pear", 165, 110.0000m), ("Pear", 180, 101.0000m), ("Pear", 193, 94.0000m), ("Pear", 210, 87.0000m), ("Pear", 225, 81.0000m)
    ];

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string RoomKey(int warehouseId, string roomCode) => $"{warehouseId}:{NormalizeCode(roomCode)}";
}
