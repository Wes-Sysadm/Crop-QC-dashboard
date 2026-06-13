using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IAdminManagementService
{
    Task<MasterDataPageViewModel> GetMasterDataAsync(string type, bool canEdit, CancellationToken cancellationToken);
    Task<MasterDataEditForm?> GetEditFormAsync(string type, int id, CancellationToken cancellationToken);
    Task<string?> SaveMasterDataAsync(MasterDataEditForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> DeactivateAsync(string type, int id, string changedByEmail, CancellationToken cancellationToken);
    Task<ConfigurationPageViewModel> GetConfigurationAsync(bool canEdit, CancellationToken cancellationToken);
    Task<string?> SaveConfigurationAsync(ConfigurationEditForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class AdminManagementService(CropQcDbContext dbContext) : IAdminManagementService
{
    private static readonly string[] DefaultCommodityOptions = ["Apple", "Pear"];

    private static readonly IReadOnlyList<(string Key, string Value, string Description, string ValueType)> ConfigurationDefaults =
    [
        ("DefaultCropYear", DateTimeOffset.UtcNow.Year.ToString(), "Default crop year", "Integer"),
        ("MaximumSampleRows", "25", "Maximum sample rows", "Integer"),
        ("AllowedSampleSizes", "10,25,50", "Allowed QC sample target sizes. Use comma-separated values.", "IntegerList"),
        ("UnsyncedWarningHours", "2", "Unsynced warning hours", "Integer"),
        ("UnsyncedCriticalHours", "12", "Unsynced critical hours", "Integer"),
        ("OfflineSessionDays", "7", "Offline session days", "Integer"),
        ("DefaultQcSummaryRecipient", "rob@earlbrownandsons.com,wes@fruitandland.com", "Legacy default QC summary testing recipients. Use QcEmailDefaultRecipients for active sends.", "String"),
        (QcEmailRecipientSettings.Key, EmailOptions.TestingQcDefaultRecipients, "Default QC Summary email recipients. Enter one email per line or comma-separated.", "EmailList"),
        ("PhotoRetentionCropYearsAfterCurrent", "3", "Photo retention crop years after current. Planning value only; no automatic photo deletion currently runs.", "Integer"),
        ("AllowOverrideSendWithMissingData", "true", "Allow override send with missing data", "Boolean")
    ];

    public async Task<MasterDataPageViewModel> GetMasterDataAsync(string type, bool canEdit, CancellationToken cancellationToken) =>
        type.ToLowerInvariant() switch
        {
            "warehouses" => await WarehousesPage(canEdit, cancellationToken),
            "rooms" => await RoomsPage(canEdit, cancellationToken),
            "fruit-profiles" => await FruitProfilesPage(canEdit, cancellationToken),
            "grades" => await GradesPage(canEdit, cancellationToken),
            "defects" => await DefectsPage(canEdit, cancellationToken),
            "sample-types" => await SampleTypesPage(canEdit, cancellationToken),
            "starch-scale-values" => await StarchPage(canEdit, cancellationToken),
            "size-thresholds" => await SizeThresholdsPage(canEdit, cancellationToken),
            "grower-lots" => await GrowerLotsPage(canEdit, cancellationToken),
            _ => new("Master data", null, ["Page"], MasterDataLinks().Select(x => (IReadOnlyList<string>)[x.Label]).ToList(), "index", canEdit)
        };

    public async Task<MasterDataEditForm?> GetEditFormAsync(string type, int id, CancellationToken cancellationToken)
    {
        return type.ToLowerInvariant() switch
        {
            "warehouses" => await dbContext.Warehouses.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "rooms" => await dbContext.Rooms.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, WarehouseId = x.WarehouseId, Code = x.Code, Name = x.Name, CapacityBins = x.CapacityBins, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "fruit-profiles" => await WithCommodityOptions(await dbContext.FruitProfiles.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.VarietyCode, Name = x.Name, Description = x.Description, FruitType = x.FruitType, ProductionType = x.ProductionType, IsOrganic = x.IsOrganic, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken), cancellationToken),
            "grades" => await dbContext.Grades.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "defects" => await dbContext.DefectTypes.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "sample-types" => await dbContext.SampleTypes.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "grower-lots" => await dbContext.GrowerLots.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Grower, Code = x.LotNumber, PoolStart = x.PoolStart, Description = x.Notes, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "starch-scale-values" => await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Value = x.Value, SortOrder = x.SortOrder, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "size-thresholds" => await WithCommodityOptions(await dbContext.FruitSizeConversionThresholds.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, FruitType = x.FruitType, SizeCategory = x.SizeCategory, MinimumWeightGrams = x.MinimumWeightGrams, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken), cancellationToken),
            _ => null
        };
    }

    public async Task<string?> SaveMasterDataAsync(MasterDataEditForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        return form.Type.ToLowerInvariant() switch
        {
            "warehouses" => await SaveWarehouse(form, changedByEmail, cancellationToken),
            "rooms" => await SaveRoom(form, changedByEmail, cancellationToken),
            "fruit-profiles" => await SaveFruitProfile(form, changedByEmail, cancellationToken),
            "grades" => await SaveGrade(form, changedByEmail, cancellationToken),
            "defects" => await SaveDefect(form, changedByEmail, cancellationToken),
            "sample-types" => await SaveSampleType(form, changedByEmail, cancellationToken),
            "grower-lots" => await SaveGrowerLot(form, changedByEmail, cancellationToken),
            "starch-scale-values" => await SaveStarchValue(form, changedByEmail, cancellationToken),
            "size-thresholds" => await SaveSizeThreshold(form, changedByEmail, cancellationToken),
            _ => "Unsupported master data type."
        };
    }

    public async Task<string?> DeactivateAsync(string type, int id, string changedByEmail, CancellationToken cancellationToken)
    {
        object? entity = type.ToLowerInvariant() switch
        {
            "warehouses" => await dbContext.Warehouses.FindAsync([id], cancellationToken),
            "rooms" => await dbContext.Rooms.FindAsync([id], cancellationToken),
            "fruit-profiles" => await dbContext.FruitProfiles.FindAsync([id], cancellationToken),
            "grades" => await dbContext.Grades.FindAsync([id], cancellationToken),
            "defects" => await dbContext.DefectTypes.FindAsync([id], cancellationToken),
            "sample-types" => await dbContext.SampleTypes.FindAsync([id], cancellationToken),
            "grower-lots" => await dbContext.GrowerLots.FindAsync([id], cancellationToken),
            "starch-scale-values" => await dbContext.StarchScaleValues.FindAsync([id], cancellationToken),
            "size-thresholds" => await dbContext.FruitSizeConversionThresholds.FindAsync([id], cancellationToken),
            _ => null
        };

        if (entity is null) return "Record not found.";
        var before = JsonSerializer.Serialize(entity);
        entity.GetType().GetProperty("IsActive")?.SetValue(entity, false);
        await AddAuditAsync("deactivate", type, id.ToString(), changedByEmail, before, JsonSerializer.Serialize(entity), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<ConfigurationPageViewModel> GetConfigurationAsync(bool canEdit, CancellationToken cancellationToken)
    {
        await EnsureConfigurationTableAsync(cancellationToken);
        await EnsureConfigurationDefaultsAsync(cancellationToken);
        var items = await dbContext.DashboardConfigurations.AsNoTracking()
            .OrderBy(x => x.Key)
            .Select(x => new ConfigurationItemViewModel(x.Id, x.Key, x.Value, x.Description, x.ValueType))
            .ToListAsync(cancellationToken);
        return new ConfigurationPageViewModel { CanEdit = canEdit, Items = items };
    }

    public async Task<string?> SaveConfigurationAsync(ConfigurationEditForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureConfigurationTableAsync(cancellationToken);
        var configs = await dbContext.DashboardConfigurations.Where(x => form.Values.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var config in configs)
        {
            var submittedValue = form.Values[config.Id]?.Trim() ?? "";
            if (config.Key == QcEmailRecipientSettings.Key)
            {
                var parsed = QcEmailRecipientParser.Parse(submittedValue);
                if (parsed.InvalidRecipients.Count > 0)
                {
                    return $"Invalid QC email recipient: {string.Join(", ", parsed.InvalidRecipients)}.";
                }

                submittedValue = string.Join(Environment.NewLine, parsed.Recipients);
            }

            var before = JsonSerializer.Serialize(config);
            config.Value = submittedValue;
            config.UpdatedAt = DateTimeOffset.UtcNow;
            await AddAuditAsync("update", "configuration", config.Id.ToString(), changedByEmail, before, JsonSerializer.Serialize(config), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<MasterDataPageViewModel> WarehousesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).Select(x => new MasterDataEditItem(x.Id, new[] { x.Code, x.Name, YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return Page("Warehouses", "warehouses", ["Code", "Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> RoomsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.Code)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.Warehouse.Code, x.Warehouse.Name, x.Code, x.Name, x.CapacityBins.ToString(), YesNo(x.IsActive) }, x.IsActive))
            .ToListAsync(ct);
        return Page("Rooms", "rooms", ["Warehouse Code", "Warehouse Name", "Room Code", "Room Name", "Capacity Bins", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> FruitProfilesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.FruitProfiles.AsNoTracking()
            .OrderBy(x => x.FruitType)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.VarietyCode, x.Name, x.FruitType, x.ProductionType, YesNo(x.IsActive) }, x.IsActive))
            .ToListAsync(ct);
        return await PageWithCommodityOptions("Fruit profiles / variety codes", "fruit-profiles", ["Variety Code", "Name", "Commodity", "Production Type", "Active"], rows, canEdit, ct);
    }

    private async Task<MasterDataPageViewModel> GradesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Grades.AsNoTracking().OrderBy(x => x.Id).Select(x => new MasterDataEditItem(x.Id, new[] { x.Code, x.Name, YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return Page("Grades", "grades", ["Code", "Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> DefectsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.DefectTypes.AsNoTracking().OrderBy(x => x.Name).Select(x => new MasterDataEditItem(x.Id, new[] { x.Name, YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return Page("Defects", "defects", ["Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> SampleTypesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.SampleTypes.AsNoTracking().OrderBy(x => x.Id).Select(x => new MasterDataEditItem(x.Id, new[] { x.Name, YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return Page("Sample types", "sample-types", ["Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> StarchPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.StarchScaleValues.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new MasterDataEditItem(x.Id, new[] { x.Value.ToString("0.0"), x.SortOrder.ToString(), YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return Page("Starch scale values", "starch-scale-values", ["Value", "Display Order", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> SizeThresholdsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.FruitSizeConversionThresholds.AsNoTracking().OrderBy(x => x.FruitType).ThenByDescending(x => x.MinimumWeightGrams).Select(x => new MasterDataEditItem(x.Id, new[] { x.FruitType, x.SizeCategory.ToString(), x.MinimumWeightGrams.ToString("0.0000"), YesNo(x.IsActive) }, x.IsActive)).ToListAsync(ct);
        return await PageWithCommodityOptions("Size thresholds", "size-thresholds", ["Commodity", "Size", "Minimum Weight (g)", "Active"], rows, canEdit, ct);
    }

    private async Task<MasterDataPageViewModel> GrowerLotsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.GrowerLots.AsNoTracking()
            .OrderBy(x => x.Grower)
            .ThenBy(x => x.LotNumber)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.Grower, x.LotNumber, x.PoolStart ?? "", x.Notes ?? "", YesNo(x.IsActive) }, x.IsActive))
            .ToListAsync(ct);
        return Page("Grower Lots", "grower-lots", ["Grower", "Lot #", "Pool Start", "Notes", "Active"], rows, canEdit);
    }

    private async Task<string?> SaveGrowerLot(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name) || Blank(form.Code)) return "Grower and Lot # are required.";
        var grower = form.Name.Trim();
        var lotNumber = form.Code.Trim();
        if (await dbContext.GrowerLots.AnyAsync(x => x.Grower == grower && x.LotNumber == lotNumber && x.Id != (form.Id ?? 0), ct)) return "Grower and Lot # combination must be unique.";
        var entity = form.Id is null ? new GrowerLot { Grower = "", LotNumber = "" } : await dbContext.GrowerLots.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Grower lot not found.";
        var action = form.Id is null ? "create" : "update";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.Grower = grower;
        entity.LotNumber = lotNumber;
        entity.PoolStart = string.IsNullOrWhiteSpace(form.PoolStart) ? null : form.PoolStart.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        entity.IsActive = form.IsActive && !grower.StartsWith("INACTIVE", StringComparison.OrdinalIgnoreCase);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (form.Id is null)
        {
            entity.CreatedAt = entity.UpdatedAt;
            dbContext.GrowerLots.Add(entity);
        }

        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(action, "grower-lots", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveWarehouse(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Code) || Blank(form.Name)) return "Code and name are required.";
        if (await dbContext.Warehouses.AnyAsync(x => x.Code == form.Code.Trim() && x.Id != (form.Id ?? 0), ct)) return "Warehouse code must be unique.";
        var entity = form.Id is null ? new Warehouse { Code = "", Name = "" } : await dbContext.Warehouses.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Warehouse not found.";
        var action = form.Id is null ? "create" : "update";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.Code = form.Code.Trim(); entity.Name = form.Name.Trim(); entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.Warehouses.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(action, "warehouses", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveRoom(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (form.WarehouseId is null || Blank(form.Code) || Blank(form.Name)) return "Warehouse, code, and name are required.";
        if (await dbContext.Rooms.AnyAsync(x => x.WarehouseId == form.WarehouseId && x.Code == form.Code.Trim() && x.Id != (form.Id ?? 0), ct)) return "Room code must be unique per warehouse.";
        var entity = form.Id is null ? new Room { Code = "", Name = "" } : await dbContext.Rooms.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Room not found.";
        var action = form.Id is null ? "create" : "update";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.WarehouseId = form.WarehouseId.Value; entity.Code = form.Code.Trim(); entity.Name = form.Name.Trim(); entity.CapacityBins = form.CapacityBins; entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.Rooms.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(action, "rooms", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveFruitProfile(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Code) || Blank(form.Name) || Blank(form.FruitType) || Blank(form.ProductionType)) return "Variety code, name, commodity, and production type are required.";
        if (!IsValidProductionType(form.ProductionType)) return "Production type must be Conventional or Organic.";
        if (await dbContext.FruitProfiles.AnyAsync(x => x.VarietyCode == form.Code.Trim() && x.Id != (form.Id ?? 0), ct)) return "Variety code must be unique.";
        var entity = form.Id is null ? new FruitProfile { VarietyCode = "", Name = "", FruitType = "", ProductionType = "" } : await dbContext.FruitProfiles.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Fruit profile not found.";
        var action = form.Id is null ? "create" : "update";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        var productionType = NormalizeProductionType(form.ProductionType);
        entity.VarietyCode = form.Code.Trim();
        entity.Name = form.Name.Trim();
        entity.Description = form.Description;
        entity.FruitType = form.FruitType.Trim();
        entity.ProductionType = productionType;
        entity.IsOrganic = productionType == "Organic";
        entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.FruitProfiles.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(action, "fruit-profiles", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveGrade(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Code) || Blank(form.Name)) return "Code and name are required.";
        if (await dbContext.Grades.AnyAsync(x => x.Code == form.Code.Trim() && x.Id != (form.Id ?? 0), ct)) return "Grade code must be unique.";
        var entity = form.Id is null ? new Grade { Code = "", Name = "" } : await dbContext.Grades.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Grade not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.Code = form.Code.Trim(); entity.Name = form.Name.Trim(); entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.Grades.Add(entity);
        await dbContext.SaveChangesAsync(ct); await AddAuditAsync(form.Id is null ? "create" : "update", "grades", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct); await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveDefect(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name)) return "Name is required.";
        if (await dbContext.DefectTypes.AnyAsync(x => x.Name == form.Name.Trim() && x.Id != (form.Id ?? 0), ct)) return "Defect name must be unique.";
        var entity = form.Id is null ? new DefectType { Name = "" } : await dbContext.DefectTypes.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Defect not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.Name = form.Name.Trim(); entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.DefectTypes.Add(entity);
        await dbContext.SaveChangesAsync(ct); await AddAuditAsync(form.Id is null ? "create" : "update", "defects", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct); await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveSampleType(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name)) return "Name is required.";
        if (await dbContext.SampleTypes.AnyAsync(x => x.Name == form.Name.Trim() && x.Id != (form.Id ?? 0), ct)) return "Sample type name must be unique.";
        var entity = form.Id is null ? new SampleType { Name = "" } : await dbContext.SampleTypes.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Sample type not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.Name = form.Name.Trim(); entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.SampleTypes.Add(entity);
        await dbContext.SaveChangesAsync(ct); await AddAuditAsync(form.Id is null ? "create" : "update", "sample-types", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct); await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveStarchValue(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (form.Value is null) return "Starch value is required.";
        var scale = await dbContext.StarchScales.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (scale is null) return "Starch scale is not configured.";
        var entity = form.Id is null ? new StarchScaleValue { StarchScaleId = scale.Id } : await dbContext.StarchScaleValues.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Starch value not found.";
        var action = form.Id is null ? "create" : "update"; var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.StarchScaleId = scale.Id; entity.Value = form.Value.Value; entity.SortOrder = form.SortOrder ?? 0; entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.StarchScaleValues.Add(entity);
        await dbContext.SaveChangesAsync(ct); await AddAuditAsync(action, "starch-scale-values", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct); await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveSizeThreshold(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.FruitType) || form.SizeCategory is null || form.MinimumWeightGrams is null) return "Commodity, size, and minimum weight are required.";
        var entity = form.Id is null ? new FruitSizeConversionThreshold { FruitType = "" } : await dbContext.FruitSizeConversionThresholds.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Size threshold not found.";
        var action = form.Id is null ? "create" : "update"; var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.FruitType = form.FruitType.Trim(); entity.SizeCategory = form.SizeCategory.Value; entity.MinimumWeightGrams = form.MinimumWeightGrams.Value; entity.IsActive = form.IsActive;
        if (form.Id is null) dbContext.FruitSizeConversionThresholds.Add(entity);
        await dbContext.SaveChangesAsync(ct); await AddAuditAsync(action, "size-thresholds", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct); await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task EnsureConfigurationDefaultsAsync(CancellationToken ct)
    {
        foreach (var item in ConfigurationDefaults)
        {
            if (!await dbContext.DashboardConfigurations.AnyAsync(x => x.Key == item.Key, ct))
            {
                dbContext.DashboardConfigurations.Add(new DashboardConfiguration { Key = item.Key, Value = item.Value, Description = item.Description, ValueType = item.ValueType, CreatedAt = DateTimeOffset.UtcNow });
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task EnsureConfigurationTableAsync(CancellationToken ct)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "DashboardConfigurations" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "Key" character varying(150) NOT NULL,
                    "Value" character varying(1000) NOT NULL,
                    "Description" character varying(500) NOT NULL,
                    "ValueType" character varying(50) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_DashboardConfigurations" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_DashboardConfigurations_Key" ON "DashboardConfigurations" ("Key");
                """, ct);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[DashboardConfigurations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [DashboardConfigurations] (
                        [Id] int NOT NULL IDENTITY,
                        [Key] nvarchar(150) NOT NULL,
                        [Value] nvarchar(1000) NOT NULL,
                        [Description] nvarchar(500) NOT NULL,
                        [ValueType] nvarchar(50) NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NULL,
                        CONSTRAINT [PK_DashboardConfigurations] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_DashboardConfigurations_Key] ON [DashboardConfigurations] ([Key]);
                END
                """, ct);
        }
    }

    private async Task AddAuditAsync(string action, string entityName, string entityKey, string by, string? before, string? after, CancellationToken ct)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == by, ct);
        dbContext.AuditLogs.Add(new AuditLog { Action = action, EntityName = entityName, EntityKey = entityKey, UserId = user?.Id, BeforeValuesJson = before, AfterValuesJson = after, SourceApplication = "CropQc.Web", CreatedAt = DateTimeOffset.UtcNow });
    }

    private static MasterDataPageViewModel Page(string title, string type, IReadOnlyList<string> columns, IReadOnlyList<MasterDataEditItem> items, bool canEdit) =>
        new(title, null, columns, items.Select(x => x.Cells).ToList(), type, canEdit, items, new MasterDataEditForm { Type = type });

    private async Task<MasterDataPageViewModel> PageWithCommodityOptions(string title, string type, IReadOnlyList<string> columns, IReadOnlyList<MasterDataEditItem> items, bool canEdit, CancellationToken ct) =>
        new(title, null, columns, items.Select(x => x.Cells).ToList(), type, canEdit, items, new MasterDataEditForm { Type = type, CommodityOptions = await GetCommodityOptionsAsync(ct) });

    private async Task<MasterDataEditForm?> WithCommodityOptions(MasterDataEditForm? form, CancellationToken ct)
    {
        if (form is null) return null;
        form.CommodityOptions = await GetCommodityOptionsAsync(ct);
        return form;
    }

    private async Task<IReadOnlyList<string>> GetCommodityOptionsAsync(CancellationToken ct)
    {
        var fruitProfileTypes = await dbContext.FruitProfiles.AsNoTracking()
            .Select(x => x.FruitType)
            .Where(x => x != "")
            .Distinct()
            .ToListAsync(ct);
        var thresholdTypes = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Select(x => x.FruitType)
            .Where(x => x != "")
            .Distinct()
            .ToListAsync(ct);

        return DefaultCommodityOptions
            .Concat(fruitProfileTypes)
            .Concat(thresholdTypes)
            .Where(x => !Blank(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static bool IsValidProductionType(string value) =>
        string.Equals(value.Trim(), "Conventional", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value.Trim(), "Organic", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProductionType(string value) =>
        string.Equals(value.Trim(), "Organic", StringComparison.OrdinalIgnoreCase) ? "Organic" : "Conventional";

    private static bool Blank(string value) => string.IsNullOrWhiteSpace(value);
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static IReadOnlyList<(string Label, string Href)> MasterDataLinks() =>
    [
        ("Warehouses", "/MasterData/warehouses"),
        ("Rooms", "/MasterData/rooms"),
        ("Fruit profiles / variety codes", "/MasterData/fruit-profiles"),
        ("Grades", "/MasterData/grades"),
        ("Defects", "/MasterData/defects"),
        ("Sample types", "/MasterData/sample-types"),
        ("Grower lots / room inventory", "/MasterData/grower-lots"),
        ("Starch scale values", "/MasterData/starch-scale-values"),
        ("Size thresholds", "/MasterData/size-thresholds")
    ];
}
