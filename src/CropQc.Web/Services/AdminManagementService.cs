using System.Text.Json;
using System.Text;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IAdminManagementService
{
    Task<MasterDataPageViewModel> GetMasterDataAsync(string type, bool canEdit, CancellationToken cancellationToken);
    Task<MasterDataEditForm?> GetEditFormAsync(string type, int id, CancellationToken cancellationToken);
    Task<string?> SaveMasterDataAsync(MasterDataEditForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> DeactivateAsync(string type, int id, string changedByEmail, CancellationToken cancellationToken);
    Task<GrowerMappingPageViewModel> GetGrowerMappingAsync(GrowerMappingForm form, CancellationToken cancellationToken);
    Task<string?> SaveGrowerMappingAsync(GrowerMappingForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<GrowerLotImportPreviewViewModel> PreviewGrowerLotImportAsync(GrowerLotImportForm form, CancellationToken cancellationToken);
    Task<(GrowerLotImportPreviewViewModel Preview, string? Error)> ApplyGrowerLotImportAsync(GrowerLotImportForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<ConfigurationPageViewModel> GetConfigurationAsync(bool canEdit, CancellationToken cancellationToken);
    Task<string?> SaveConfigurationAsync(ConfigurationEditForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class AdminManagementService(
    CropQcDbContext dbContext,
    IVarietyColorService varietyColorService,
    ICanonicalGrowerService? canonicalGrowerService = null,
    IFacilityContextService? facilityContextService = null,
    IEndOfDayFillWarehouseLabelResolver? endOfDayFillWarehouseLabelResolver = null,
    IReviewedGrowerLotPolicy? reviewedGrowerLotPolicy = null) : IAdminManagementService
{
    private static readonly IBusinessTimeService BusinessTime = new PacificBusinessTimeService(new SystemClock());
    private readonly IFacilityContextService facilityContext = facilityContextService ?? new FacilityContextService(dbContext);
    private readonly IEndOfDayFillWarehouseLabelResolver endOfDayFillWarehouseLabelResolver = endOfDayFillWarehouseLabelResolver ?? new EndOfDayFillWarehouseLabelResolver();
    private static readonly string[] DefaultCommodityOptions = ["Apple", "Pear"];

    private static readonly IReadOnlyList<(string Key, string Value, string Description, string ValueType)> ConfigurationDefaults =
    [
        (CropYearService.ActiveCropYearKey, "2026", "Active operational crop year used by dashboard and new-entry defaults. Historical records are not changed.", "Integer"),
        ("MaximumSampleRows", "25", "Maximum sample rows", "Integer"),
        ("AllowedSampleSizes", "10,25,50", "Allowed QC sample target sizes. Use comma-separated values.", "IntegerList"),
        ("UnsyncedWarningHours", "2", "Unsynced warning hours", "Integer"),
        ("UnsyncedCriticalHours", "12", "Unsynced critical hours", "Integer"),
        ("OfflineSessionDays", "7", "Offline session days", "Integer"),
        ("DefaultQcSummaryRecipient", QcReportEmailDefaults.RequiredRecipient, "Legacy display value. Active QC report sends use Email:QcReportDefaultRecipient.", "String"),
        (QcEmailRecipientSettings.Key, QcReportEmailDefaults.RequiredRecipient, "Legacy display value. Active QC report sends always include the required QC recipient.", "EmailList"),
        (EbsDailyBinsEmailSettings.RecipientsKey, EbsDailyBinsEmailSettings.DefaultRecipients, "Daily end-of-day EBS bin availability email recipients. Enter one email per line or comma-separated.", "EmailList"),
        (EbsDailyBinsEmailSettings.EnabledKey, "false", "Send daily end-of-day EBS bin availability email automatically.", "Boolean"),
        (EbsDailyBinsEmailSettings.SendHourLocalKey, "17", "Local Pacific hour when automatic EBS bin availability email may send, 0-23.", "Integer"),
        (EbsDailyBinsEmailSettings.SenderEmailKey, "wes@fruitandland.com", "Active Gmail-connected user used by the scheduled EBS bin availability email.", "Email"),
        (EbsDailyBinsEmailSettings.LastSentDateKey, "", "Last successful automatic EBS bin availability email date. Managed by the system.", "Date"),
        (RunProjectionSettings.ApplePoundsPerBinKey, "880", "Gross pounds per apple bin used by newly created run projections.", "Decimal"),
        (RunProjectionSettings.PearPoundsPerBinKey, "920", "Gross pounds per pear bin used by newly created run projections.", "Decimal"),
        (RunProjectionSettings.StandardBoxWeightKey, "40", "Standard box-equivalent weight in pounds used by newly created run projections.", "Decimal"),
        (RunProjectionSettings.DraftExpirationDaysKey, "14", "Days after the planned date before an unconverted draft projection is marked expired.", "Integer"),
        (RunProjectionSettings.VisibilityPastDaysKey, "30", "Recent Pacific business days shown in the run-planner calendar.", "Integer"),
        (RunProjectionSettings.VisibilityFutureDaysKey, "14", "Future Pacific business days shown in the run-planner calendar.", "Integer"),
        (RunProjectionSettings.DefaultExpectedPackoutPercentKey, "85", "Default Expected Packout % copied to newly added projection sources. Existing projections are unchanged.", "Decimal"),
        (RunProjectionSettings.MinimumDistributionFruitKey, "10", "Minimum meaningful fruit count before a projection distribution is no longer flagged as sparse.", "Integer"),
        ("PhotoRetentionCropYearsAfterCurrent", "3", "Photo retention crop years after current. Planning value only; no automatic photo deletion currently runs.", "Integer"),
        ("AllowOverrideSendWithMissingData", "true", "Allow override send with missing data", "Boolean"),
        ("DeviceCapture__Enabled", "false", "Enable optional browser/local testing-device capture controls. Manual workflow remains available.", "Boolean"),
        ("DeviceCapture__BrioEnabled", "false", "Enable Logitech Brio 4K apple photo capture controls.", "Boolean"),
        ("DeviceCapture__ObsbotEnabled", "false", "Enable OBSBOT Tiny 2 Lite truck/top-of-truck photo capture controls.", "Boolean"),
        ("DeviceCapture__ScaleEnabled", "false", "Enable optional browser scale controls for US Solid USS-DB28-50 grams capture.", "Boolean")
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
            "canonical-growers" => await CanonicalGrowersPage(canEdit, cancellationToken),
            "orchard-blocks" => await OrchardBlocksPage(canEdit, cancellationToken),
            "starch-scale-values" => await StarchPage(canEdit, cancellationToken),
            "size-thresholds" => await SizeThresholdsPage(canEdit, cancellationToken),
            "treatment-chemicals" => await TreatmentChemicalsPage(canEdit, cancellationToken),
            "processors" => await ProcessorsPage(canEdit, cancellationToken),
            "grower-lots" => await GrowerLotsPage(canEdit, cancellationToken),
            _ => new("Master data", null, ["Page"], MasterDataLinks().Select(x => (IReadOnlyList<string>)[x.Label]).ToList(), "index", canEdit)
        };

    public async Task<MasterDataEditForm?> GetEditFormAsync(string type, int id, CancellationToken cancellationToken)
    {
        return type.ToLowerInvariant() switch
        {
            "warehouses" => await dbContext.Warehouses.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "rooms" => await WithEndOfDayFillReportGroupsAsync(await dbContext.Rooms.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, WarehouseId = x.WarehouseId, EndOfDayFillReportGroupId = x.EndOfDayFillReportGroupId, Code = x.Code, Name = x.Name, CompuTechCode = x.CompuTechRoomCode, CapacityBins = x.CapacityBins, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken), cancellationToken),
            "fruit-profiles" => await WithFruitProfileColorAsync(await WithCommodityOptions(await dbContext.FruitProfiles.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.VarietyCode, Name = x.Name, Description = x.Description, FruitType = x.FruitType, ProductionType = x.ProductionType, IsOrganic = x.IsOrganic, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken), cancellationToken), cancellationToken),
            "grades" => await dbContext.Grades.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "defects" => await dbContext.DefectTypes.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "sample-types" => await dbContext.SampleTypes.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Name, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "canonical-growers" => await GetCanonicalGrowerEditFormAsync(type, id, cancellationToken),
            "orchard-blocks" => await GetOrchardBlockEditFormAsync(type, id, cancellationToken),
            "grower-lots" => await dbContext.GrowerLots.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Grower, Code = x.LotNumber, PoolStart = x.PoolStart, Description = x.Notes, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "starch-scale-values" => await dbContext.StarchScaleValues.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Value = x.Value, SortOrder = x.SortOrder, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "size-thresholds" => await WithCommodityOptions(await dbContext.FruitSizeConversionThresholds.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, FruitType = x.FruitType, SizeCategory = x.SizeCategory, MinimumWeightGrams = x.MinimumWeightGrams, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken), cancellationToken),
            "treatment-chemicals" => await dbContext.TreatmentChemicals.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, ProductName = x.ProductName, CommonName = x.CommonName, Crop = x.Crop, ApplicationLevel = x.ApplicationLevel, Volume = x.Volume, Unit = x.Unit, UnitPrice = x.UnitPrice, Currency = x.Currency, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
            "processors" => await dbContext.Processors.AsNoTracking().Where(x => x.Id == id).Select(x => new MasterDataEditForm { Type = type, Id = x.Id, Name = x.Name, Code = x.Code ?? "", Description = x.Notes, IsActive = x.IsActive }).SingleOrDefaultAsync(cancellationToken),
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
            "canonical-growers" => await SaveCanonicalGrower(form, changedByEmail, cancellationToken),
            "orchard-blocks" => await SaveOrchardBlock(form, changedByEmail, cancellationToken),
            "grower-lots" => await SaveGrowerLot(form, changedByEmail, cancellationToken),
            "starch-scale-values" => await SaveStarchValue(form, changedByEmail, cancellationToken),
            "size-thresholds" => await SaveSizeThreshold(form, changedByEmail, cancellationToken),
            "treatment-chemicals" => await SaveTreatmentChemical(form, changedByEmail, cancellationToken),
            "processors" => await SaveProcessor(form, changedByEmail, cancellationToken),
            _ => "Unsupported master data type."
        };
    }

    public async Task<string?> DeactivateAsync(string type, int id, string changedByEmail, CancellationToken cancellationToken)
    {
        if (reviewedGrowerLotPolicy is not null && type.Equals("grower-lots", StringComparison.OrdinalIgnoreCase))
        {
            var lot = await dbContext.GrowerLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (lot is not null)
            {
                var reviewed = await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(cancellationToken);
                if (reviewed.ContainsKey(CanonicalGrowerService.NormalizeGrowerNumber(lot.LotNumber)))
                    return "Current Grower Lots are controlled by the reviewed Grower master and cannot be deactivated manually.";
            }
        }
        if (reviewedGrowerLotPolicy is not null && type.Equals("canonical-growers", StringComparison.OrdinalIgnoreCase))
        {
            var reviewed = await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(cancellationToken);
            var reviewedNumbers = reviewed.Keys.ToList();
            var hasReviewedNumber = await dbContext.CanonicalGrowerNumbers.AsNoTracking().AnyAsync(
                x => x.CanonicalGrowerId == id && x.IsActive && reviewedNumbers.Contains(x.NormalizedGrowerNumber),
                cancellationToken);
            if (hasReviewedNumber)
                return "Reviewed Growers are controlled by the authoritative Grower master and cannot be deactivated manually.";
        }

        object? entity = type.ToLowerInvariant() switch
        {
            "warehouses" => await dbContext.Warehouses.FindAsync([id], cancellationToken),
            "rooms" => await dbContext.Rooms.FindAsync([id], cancellationToken),
            "fruit-profiles" => await dbContext.FruitProfiles.FindAsync([id], cancellationToken),
            "grades" => await dbContext.Grades.FindAsync([id], cancellationToken),
            "defects" => await dbContext.DefectTypes.FindAsync([id], cancellationToken),
            "sample-types" => await dbContext.SampleTypes.FindAsync([id], cancellationToken),
            "canonical-growers" => await dbContext.CanonicalGrowers.FindAsync([id], cancellationToken),
            "orchard-blocks" => await dbContext.CanonicalOrchardBlocks.FindAsync([id], cancellationToken),
            "grower-lots" => await dbContext.GrowerLots.FindAsync([id], cancellationToken),
            "starch-scale-values" => await dbContext.StarchScaleValues.FindAsync([id], cancellationToken),
            "size-thresholds" => await dbContext.FruitSizeConversionThresholds.FindAsync([id], cancellationToken),
            "treatment-chemicals" => await dbContext.TreatmentChemicals.FindAsync([id], cancellationToken),
            "processors" => await dbContext.Processors.FindAsync([id], cancellationToken),
            _ => null
        };

        if (entity is null) return "Record not found.";
        var before = entity is Processor processorBefore
            ? JsonSerializer.Serialize(new { processorBefore.Name, processorBefore.Code, processorBefore.Notes, processorBefore.IsActive })
            : JsonSerializer.Serialize(entity);
        entity.GetType().GetProperty("IsActive")?.SetValue(entity, false);
        if (entity is Processor processor)
        {
            var actor = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
            processor.UpdatedAt = BusinessTime.UtcNow;
            processor.UpdatedByUserId = actor?.Id;
        }
        var after = entity is Processor processorAfter
            ? JsonSerializer.Serialize(new { processorAfter.Name, processorAfter.Code, processorAfter.Notes, processorAfter.IsActive })
            : JsonSerializer.Serialize(entity);
        var isProcessor = type.Equals("processors", StringComparison.OrdinalIgnoreCase);
        await AddAuditAsync(isProcessor ? "ProcessorDeactivated" : "deactivate", isProcessor ? nameof(Processor) : type, id.ToString(), changedByEmail, before, after, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (type == "canonical-growers")
        {
            canonicalGrowerService?.InvalidateResolutionSet();
        }
        return null;
    }

    public async Task<GrowerMappingPageViewModel> GetGrowerMappingAsync(GrowerMappingForm form, CancellationToken cancellationToken)
    {
        var growerService = canonicalGrowerService ?? new CanonicalGrowerService(dbContext);
        var resolver = await growerService.LoadResolutionSetAsync(cancellationToken);
        var current = resolver.Resolve(form.SourceGrowerName, form.GrowerNumber);
        var source = await BuildUnmappedSourceSummaryAsync(form.SourceGrowerName, form.GrowerNumber, form.Facility, form.CropYear, cancellationToken);
        var growers = await GetCanonicalGrowerOptionsAsync(cancellationToken);
        var suggested = BuildSuggestedGrowers(form, growers);
        form.NewCanonicalGrowerName = string.IsNullOrWhiteSpace(form.NewCanonicalGrowerName)
            ? CleanSuggestedCanonicalName(form.SourceGrowerName)
            : form.NewCanonicalGrowerName;

        return new GrowerMappingPageViewModel
        {
            Form = form,
            Source = source,
            ExistingGrowers = growers,
            SuggestedGrowers = suggested,
            AlreadyMappedTo = current.IsMapped ? current.DisplayName : null
        };
    }

    public async Task<string?> SaveGrowerMappingAsync(GrowerMappingForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        if (!form.ConfirmMapping)
        {
            return "Confirm the grower mapping before saving.";
        }

        if (Blank(form.SourceGrowerName))
        {
            return "Source grower name is required.";
        }

        var growerService = canonicalGrowerService ?? new CanonicalGrowerService(dbContext);
        var resolver = await growerService.LoadResolutionSetAsync(cancellationToken);
        var current = resolver.Resolve(form.SourceGrowerName, form.GrowerNumber);
        if (current.IsMapped)
        {
            return $"This source identity is already mapped to {current.DisplayName}. Reload Crop Year Review.";
        }

        CanonicalGrower? target;
        var action = "map-grower-source";
        if (string.Equals(form.MappingMode, "New", StringComparison.OrdinalIgnoreCase))
        {
            if (Blank(form.NewCanonicalGrowerName))
            {
                return "Canonical grower name is required.";
            }

            var normalizedName = CanonicalGrowerService.NormalizeGrowerKey(form.NewCanonicalGrowerName);
            if (await dbContext.CanonicalGrowers.AnyAsync(x => x.NormalizedKey == normalizedName && x.IsActive, cancellationToken))
            {
                return "A canonical grower with that normalized name already exists. Choose the existing grower instead.";
            }

            target = new CanonicalGrower
            {
                DisplayName = form.NewCanonicalGrowerName.Trim(),
                NormalizedKey = normalizedName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.CanonicalGrowers.Add(target);
            action = "create-and-map-grower-source";
        }
        else
        {
            if (form.CanonicalGrowerId is null)
            {
                return "Select a canonical grower.";
            }

            target = await dbContext.CanonicalGrowers
                .Include(x => x.Aliases)
                .Include(x => x.GrowerNumbers)
                .SingleOrDefaultAsync(x => x.Id == form.CanonicalGrowerId.Value, cancellationToken);
            if (target is null || !target.IsActive || target.MergedIntoCanonicalGrowerId is not null)
            {
                return "Selected canonical grower is not active.";
            }
        }

        var aliasKey = CanonicalGrowerService.NormalizeGrowerKey(form.SourceGrowerName);
        var aliasConflict = await dbContext.CanonicalGrowerAliases
            .Include(x => x.CanonicalGrower)
            .Where(x => x.IsActive && x.NormalizedAliasKey == aliasKey && x.CanonicalGrowerId != target.Id)
            .Select(x => x.CanonicalGrower.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (aliasConflict is not null)
        {
            return $"Source name is already mapped to {aliasConflict}.";
        }

        var numberKey = CanonicalGrowerService.NormalizeGrowerNumber(form.GrowerNumber);
        if (numberKey.Length > 0)
        {
            var numberConflict = await dbContext.CanonicalGrowerNumbers
                .Include(x => x.CanonicalGrower)
                .Where(x => x.IsActive && x.NormalizedGrowerNumber == numberKey && x.CanonicalGrowerId != target.Id)
                .Select(x => x.CanonicalGrower.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);
            if (numberConflict is not null)
            {
                return $"Grower number is already mapped to {numberConflict}.";
            }
        }

        if (!target.Aliases.Any(x => x.IsActive && x.NormalizedAliasKey == aliasKey))
        {
            target.Aliases.Add(new CanonicalGrowerAlias
            {
                AliasName = form.SourceGrowerName.Trim(),
                NormalizedAliasKey = aliasKey,
                SourceSystem = Blank(form.Facility) ? null : form.Facility.Trim(),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (numberKey.Length > 0 && !target.GrowerNumbers.Any(x => x.IsActive && x.NormalizedGrowerNumber == numberKey))
        {
            target.GrowerNumbers.Add(new CanonicalGrowerNumber
            {
                GrowerNumber = form.GrowerNumber.Trim(),
                NormalizedGrowerNumber = numberKey,
                Facility = Blank(form.Facility) ? null : form.Facility.Trim(),
                CropYear = form.CropYear,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(action, "canonical-grower-mapping", target.Id.ToString(), changedByEmail, null, JsonSerializer.Serialize(new
        {
            SourceGrowerName = form.SourceGrowerName,
            form.GrowerNumber,
            Facility = form.Facility,
            form.CropYear,
            CanonicalGrowerId = target.Id,
            CanonicalGrowerName = target.DisplayName,
            MappingMode = form.MappingMode
        }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        growerService.InvalidateResolutionSet();
        return null;
    }

    public async Task<GrowerLotImportPreviewViewModel> PreviewGrowerLotImportAsync(GrowerLotImportForm form, CancellationToken cancellationToken)
    {
        var csvText = await ReadCsvTextAsync(form, cancellationToken);
        return await BuildGrowerLotImportPreviewAsync(csvText, cancellationToken);
    }

    public async Task<(GrowerLotImportPreviewViewModel Preview, string? Error)> ApplyGrowerLotImportAsync(GrowerLotImportForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        var csvText = await ReadCsvTextAsync(form, cancellationToken);
        var preview = await BuildGrowerLotImportPreviewAsync(csvText, cancellationToken);
        if (!form.ConfirmImport)
        {
            return (preview, "Confirm Import Grower Lots before applying changes.");
        }

        if (!preview.CanApply)
        {
            return (preview, "Resolve duplicate/conflicting or invalid rows before importing grower lots.");
        }

        var existingLots = await dbContext.GrowerLots.ToListAsync(cancellationToken);
        foreach (var row in preview.Rows.Where(x => x.Action is "Add" or "Update"))
        {
            var rowNumber = CanonicalGrowerService.NormalizeGrowerNumber(row.LotNumber);
            var existing = existingLots.SingleOrDefault(x => CanonicalGrowerService.NormalizeGrowerNumber(x.LotNumber).Equals(rowNumber, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var now = DateTimeOffset.UtcNow;
                var entity = new GrowerLot
                {
                    Grower = row.Grower,
                    LotNumber = row.LotNumber,
                    PoolStart = reviewedGrowerLotPolicy is null && !string.IsNullOrWhiteSpace(row.PoolStart) ? row.PoolStart : null,
                    IsActive = reviewedGrowerLotPolicy is not null || !row.IsInactive,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.GrowerLots.Add(entity);
                existingLots.Add(entity);
                await AddAuditAsync("import-add", "grower-lots", row.LotNumber, changedByEmail, null, JsonSerializer.Serialize(entity), cancellationToken);
                continue;
            }

            var before = JsonSerializer.Serialize(existing);
            existing.Grower = row.Grower;
            if (reviewedGrowerLotPolicy is null)
                existing.PoolStart = string.IsNullOrWhiteSpace(row.PoolStart) ? null : row.PoolStart;
            existing.IsActive = reviewedGrowerLotPolicy is not null || !row.IsInactive;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await AddAuditAsync("import-update", "grower-lots", existing.Id.ToString(), changedByEmail, before, JsonSerializer.Serialize(existing), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (preview, null);
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
            if (config.Key == CropYearService.ActiveCropYearKey
                && (!int.TryParse(submittedValue, out var cropYear) || cropYear < 2000 || cropYear > DateTimeOffset.UtcNow.Year + 5))
            {
                return $"Active crop year must be between 2000 and {DateTimeOffset.UtcNow.Year + 5}.";
            }
            if (config.Key is RunProjectionSettings.ApplePoundsPerBinKey
                or RunProjectionSettings.PearPoundsPerBinKey
                or RunProjectionSettings.StandardBoxWeightKey
                && (!decimal.TryParse(submittedValue, out var pounds) || pounds <= 0 || pounds > 10000))
            {
                return "Run projection weight assumptions must be positive numbers no greater than 10,000 pounds.";
            }
            if (config.Key is RunProjectionSettings.DraftExpirationDaysKey
                or RunProjectionSettings.VisibilityPastDaysKey
                or RunProjectionSettings.VisibilityFutureDaysKey
                && (!int.TryParse(submittedValue, out var days) || days < 1 || days > 365))
            {
                return "Run projection visibility and expiration values must be between 1 and 365 days.";
            }
            if (config.Key == RunProjectionSettings.DefaultExpectedPackoutPercentKey
                && (!decimal.TryParse(submittedValue, out var packout) || packout is < 0 or > 100))
            {
                return "Default Expected Packout % must be between 0 and 100.";
            }
            if (config.Key == RunProjectionSettings.MinimumDistributionFruitKey
                && (!int.TryParse(submittedValue, out var minimumFruit) || minimumFruit is < 1 or > 50))
            {
                return "Minimum distribution fruit must be between 1 and 50.";
            }
            if (config.Key is QcEmailRecipientSettings.Key or EbsDailyBinsEmailSettings.RecipientsKey)
            {
                var parsed = QcEmailRecipientParser.Parse(submittedValue);
                if (parsed.InvalidRecipients.Count > 0)
                {
                    return config.Key == QcEmailRecipientSettings.Key
                        ? $"Invalid QC email recipient: {string.Join(", ", parsed.InvalidRecipients)}."
                        : $"Invalid EBS daily bin email recipient: {string.Join(", ", parsed.InvalidRecipients)}.";
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
        var rows = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Name).Select(x => new MasterDataEditItem(x.Id, new[] { x.Code, x.Name, YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return Page("Warehouses", "warehouses", ["Code", "Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> RoomsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Rooms.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.EndOfDayFillReportGroup)
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.SubLocation)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.CropQcRoomName ?? x.Code)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.Warehouse.Code, x.Warehouse.Name, x.CropQcRoomName ?? x.Code, x.CompuTechRoomCode ?? "", x.SubLocation ?? "", x.Name, x.CapacityBins.ToString(), x.EndOfDayFillReportGroup == null ? "Not included" : x.EndOfDayFillReportGroup.Name, YesNo(x.IsActive) }, x.IsActive, null))
            .ToListAsync(ct);
        var page = Page("Rooms", "rooms", ["Warehouse Code", "Warehouse Name", "Crop QC Room", "Compu-Tech Code", "SubLocation", "Room Name", "Capacity Bins", "End of Day Fill Report", "Active"], rows, canEdit);
        return page with { EditForm = await WithEndOfDayFillReportGroupsAsync(page.EditForm, ct) };
    }

    private async Task<MasterDataPageViewModel> FruitProfilesPage(bool canEdit, CancellationToken ct)
    {
        var colorMap = await varietyColorService.GetResolvedColorsForMasterDataAsync(ct);
        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .OrderBy(x => x.FruitType)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        var rows = profiles
            .GroupBy(x => VarietyColorService.IdentityFromProfile(x).Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var primary = group
                    .OrderBy(x => x.IsOrganic)
                    .ThenByDescending(x => x.IsActive)
                    .ThenBy(x => x.Name.Length)
                    .ThenBy(x => x.Id)
                    .First();
                var identity = VarietyColorService.IdentityFromProfile(primary);
                colorMap.TryGetValue(identity.Key, out var color);
                var fallback = VarietyColorService.FallbackColor(identity.Key);
                var aliases = string.Join(", ", group
                    .SelectMany(x => new[] { x.VarietyCode, x.Name })
                    .Append(VarietyColorService.AliasesForIdentity(identity))
                    .SelectMany(x => (x ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    .Where(x => !x.Equals(identity.Name, StringComparison.OrdinalIgnoreCase) && !x.Equals(identity.Key, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x));
                var colorInfo = new MasterDataVarietyColorViewModel
                {
                    VarietyKey = identity.Key,
                    VarietyName = color?.VarietyName ?? identity.Name,
                    Aliases = aliases,
                    HexColor = color?.HexColor ?? fallback,
                    FallbackColor = fallback,
                    IsConfigured = color?.IsConfigured == true
                };
                return new MasterDataEditItem(
                    primary.Id,
                    [
                        string.Join(", ", group.Select(x => x.VarietyCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                        identity.Name,
                        string.Join(", ", group.Select(x => x.FruitType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                        string.Join(", ", group.Select(x => x.ProductionType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                        aliases,
                        colorInfo.HexColor,
                        colorInfo.IsConfigured ? "Configured" : "Fallback",
                        YesNo(group.Any(x => x.IsActive))
                    ],
                    group.Any(x => x.IsActive),
                    colorInfo);
            })
            .OrderBy(x => x.Cells[2])
            .ThenBy(x => x.VarietyColor?.VarietyName ?? x.Cells[1])
            .ToList();
        return await PageWithCommodityOptions("Fruit profiles / variety codes", "fruit-profiles", ["Variety Code(s)", "Canonical Variety", "Commodity", "Production Type", "Aliases", "Color", "Color Status", "Active"], rows, canEdit, ct);
    }

    private async Task<MasterDataPageViewModel> GradesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Grades.AsNoTracking().OrderBy(x => x.Id).Select(x => new MasterDataEditItem(x.Id, new[] { x.Code, x.Name, YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return Page("Grades", "grades", ["Code", "Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> DefectsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.DefectTypes.AsNoTracking().OrderBy(x => x.Name).Select(x => new MasterDataEditItem(x.Id, new[] { x.Name, YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return Page("Defects", "defects", ["Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> SampleTypesPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.SampleTypes.AsNoTracking().OrderBy(x => x.Id).Select(x => new MasterDataEditItem(x.Id, new[] { x.Name, YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return Page("Sample types", "sample-types", ["Name", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> StarchPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.StarchScaleValues.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new MasterDataEditItem(x.Id, new[] { x.Value.ToString("0.0"), x.SortOrder.ToString(), YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return Page("Starch scale values", "starch-scale-values", ["Value", "Display Order", "Active"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> SizeThresholdsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.FruitSizeConversionThresholds.AsNoTracking().OrderBy(x => x.FruitType).ThenByDescending(x => x.MinimumWeightGrams).Select(x => new MasterDataEditItem(x.Id, new[] { x.FruitType, x.SizeCategory.ToString(), x.MinimumWeightGrams.ToString("0.0000"), YesNo(x.IsActive) }, x.IsActive, null)).ToListAsync(ct);
        return await PageWithCommodityOptions("Size thresholds", "size-thresholds", ["Commodity", "Size", "Minimum Weight (g)", "Active"], rows, canEdit, ct);
    }

    private async Task<MasterDataPageViewModel> TreatmentChemicalsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.TreatmentChemicals.AsNoTracking()
            .OrderBy(x => x.Crop)
            .ThenBy(x => x.CommonName ?? x.ProductName)
            .ThenBy(x => x.ProductName)
            .Select(x => new MasterDataEditItem(x.Id, new[]
            {
                x.CommonName ?? "",
                x.ProductName,
                x.Crop,
                x.ApplicationLevel,
                x.Volume.ToString("0.00"),
                x.Unit,
                x.UnitPrice.ToString("0.00"),
                x.Currency,
                x.IsActive ? "Active" : "Inactive"
            }, x.IsActive, null))
            .ToListAsync(ct);
        return Page("Treatment Chemicals", "treatment-chemicals", ["Common Name", "Product Name", "Crop", "Application Level", "Volume", "Unit", "Unit Price", "Currency", "Status"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> ProcessorsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.Processors.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.Name, x.Code ?? "", x.Notes ?? "", x.IsActive ? "Active" : "Inactive" }, x.IsActive, null))
            .ToListAsync(ct);
        return Page("Processors", "processors", ["Name", "Code / Short Name", "Notes", "Status"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> GrowerLotsPage(bool canEdit, CancellationToken ct)
    {
        var rows = await dbContext.GrowerLots.AsNoTracking()
            .OrderBy(x => x.LotNumber)
            .ThenBy(x => x.Grower)
            .Select(x => new MasterDataEditItem(x.Id, new[] { x.LotNumber, x.Grower, x.PoolStart ?? "", x.IsActive ? "Active" : "Inactive" }, x.IsActive, null))
            .ToListAsync(ct);
        return Page("Grower Lots", "grower-lots", ["Grower Number", "Grower Name", "Pool Start", "Status"], rows, canEdit);
    }

    private async Task<MasterDataPageViewModel> OrchardBlocksPage(bool canEdit, CancellationToken ct)
    {
        var sampleCounts = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.CanonicalOrchardBlockId != null && x.SampleType.Name == "Field Sample" && !x.IsDeleted)
            .GroupBy(x => x.CanonicalOrchardBlockId!.Value)
            .Select(x => new
            {
                BlockId = x.Key,
                Count = x.Count(),
                First = x.Min(y => y.SampleTakenAt),
                Latest = x.Max(y => y.SampleTakenAt)
            })
            .ToDictionaryAsync(x => x.BlockId, ct);
        var rows = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Include(x => x.Aliases)
            .OrderBy(x => x.OrchardName)
            .ThenBy(x => x.CanonicalBlockName)
            .ToListAsync(ct);

        var items = rows.Select(block =>
        {
            sampleCounts.TryGetValue(block.Id, out var count);
            return new MasterDataEditItem(
                block.Id,
                [
                    block.OrchardName,
                    block.CanonicalBlockName,
                    string.Join(", ", block.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).OrderBy(x => x)),
                    count?.Count.ToString() ?? "0",
                    count is null ? "" : BusinessTime.FormatPacific(count.First, "d", includeZone: false),
                    count is null ? "" : BusinessTime.FormatPacific(count.Latest, "d", includeZone: false),
                    YesNo(block.IsActive)
                ],
                block.IsActive,
                null);
        }).ToList();
        return Page("Orchard Blocks", "orchard-blocks", ["Orchard / Grower", "Canonical Block", "Aliases", "Samples", "First Sample", "Latest Sample", "Active"], items, canEdit);
    }

    private async Task<MasterDataPageViewModel> CanonicalGrowersPage(bool canEdit, CancellationToken ct)
    {
        var growerService = canonicalGrowerService ?? new CanonicalGrowerService(dbContext);
        await growerService.EnsureSeedMappingsAsync(ct);
        var growers = await dbContext.CanonicalGrowers.AsNoTracking()
            .Include(x => x.Aliases)
            .Include(x => x.GrowerNumbers)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);
        var receiptSummaries = await dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.GrowerName, x.GrowerNumber, x.CropYear })
            .ToListAsync(ct);
        var resolver = await growerService.LoadResolutionSetAsync(ct);
        var reviewedGrowers = reviewedGrowerLotPolicy is null
            ? null
            : await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(ct);
        var counts = receiptSummaries
            .Select(x => new { Identity = resolver.Resolve(x.GrowerName, x.GrowerNumber), x.CropYear })
            .GroupBy(x => x.Identity.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new
                {
                    Receipts = x.Count(),
                    CropYears = string.Join(", ", x.Select(y => y.CropYear).Distinct().OrderBy(y => y))
                },
                StringComparer.OrdinalIgnoreCase);

        var rows = growers.Select(grower =>
        {
            counts.TryGetValue(grower.NormalizedKey, out var count);
            var activeNumbers = grower.GrowerNumbers.Where(x => x.IsActive).Select(x => x.GrowerNumber).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var isCurrentReviewedGrower = reviewedGrowers is null
                ? grower.IsActive
                : grower.IsActive
                    && activeNumbers.Count == 1
                    && reviewedGrowers.TryGetValue(CanonicalGrowerService.NormalizeGrowerNumber(activeNumbers[0]), out var reviewed)
                    && grower.DisplayName.Equals(reviewed.GrowerName, StringComparison.Ordinal);
            return new MasterDataEditItem(
                grower.Id,
                [
                    grower.DisplayName,
                    string.Join(", ", activeNumbers),
                    string.Join(", ", grower.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
                    count?.Receipts.ToString() ?? "0",
                    count?.CropYears ?? "",
                    isCurrentReviewedGrower ? "Active" : "Inactive / historical"
                ],
                isCurrentReviewedGrower,
                null);
        }).ToList();

        return Page("Canonical Growers", "canonical-growers", ["Canonical Grower", "Grower Numbers", "Aliases / Source Names", "Receipts", "Crop Years", "Active"], rows, canEdit)
            with
        { UnmappedGrowers = await BuildUnmappedGrowerSourcesAsync(ct) };
    }

    private async Task<IReadOnlyList<UnmappedGrowerSourceViewModel>> BuildUnmappedGrowerSourcesAsync(CancellationToken ct)
    {
        var growerService = canonicalGrowerService ?? new CanonicalGrowerService(dbContext);
        var resolver = await growerService.LoadResolutionSetAsync(ct);
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.GrowerName,
                x.GrowerNumber,
                Facility = x.Warehouse.Code,
                x.CropYear,
                x.Id,
                x.LotCode,
                x.BinCount
            })
            .ToListAsync(ct);

        return receipts
            .Where(x => !resolver.Resolve(x.GrowerName, x.GrowerNumber).IsMapped)
            .GroupBy(x => new
            {
                GrowerKey = CanonicalGrowerService.NormalizeGrowerKey(x.GrowerName),
                NumberKey = CanonicalGrowerService.NormalizeGrowerNumber(x.GrowerNumber),
                x.Facility
            })
            .Select(group => new UnmappedGrowerSourceViewModel
            {
                SourceGrowerName = group.Select(x => x.GrowerName).FirstOrDefault(x => !Blank(x)) ?? "",
                GrowerNumber = group.Select(x => x.GrowerNumber ?? "").FirstOrDefault(x => !Blank(x)) ?? "",
                Facility = group.Key.Facility,
                CropYears = group.Select(x => x.CropYear).Distinct().OrderBy(x => x).ToList(),
                ReceiptCount = group.Select(x => x.Id).Distinct().Count(),
                LotCount = group.Select(x => x.LotCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                BinsReceived = group.Sum(x => x.BinCount)
            })
            .OrderByDescending(x => x.ReceiptCount)
            .ThenBy(x => x.SourceGrowerName)
            .ToList();
    }

    private async Task<UnmappedGrowerSourceViewModel> BuildUnmappedSourceSummaryAsync(string sourceGrowerName, string growerNumber, string facility, int? cropYear, CancellationToken ct)
    {
        var query = dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted && x.GrowerName == sourceGrowerName);
        if (!Blank(growerNumber))
        {
            query = query.Where(x => x.GrowerNumber == growerNumber);
        }

        if (!Blank(facility))
        {
            query = query.Where(x => x.Warehouse.Code == facility);
        }

        if (cropYear is not null)
        {
            query = query.Where(x => x.CropYear == cropYear);
        }

        var rows = await query.Select(x => new { x.Id, x.GrowerName, x.GrowerNumber, Facility = x.Warehouse.Code, x.CropYear, x.LotCode, x.BinCount }).ToListAsync(ct);
        return new UnmappedGrowerSourceViewModel
        {
            SourceGrowerName = sourceGrowerName,
            GrowerNumber = growerNumber,
            Facility = facility,
            CropYears = rows.Select(x => x.CropYear).DefaultIfEmpty(cropYear ?? 0).Where(x => x > 0).Distinct().OrderBy(x => x).ToList(),
            ReceiptCount = rows.Select(x => x.Id).Distinct().Count(),
            LotCount = rows.Select(x => x.LotCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            BinsReceived = rows.Sum(x => x.BinCount)
        };
    }

    private async Task<IReadOnlyList<CanonicalGrowerOptionViewModel>> GetCanonicalGrowerOptionsAsync(CancellationToken ct)
    {
        var growers = await dbContext.CanonicalGrowers.AsNoTracking()
            .Include(x => x.Aliases)
            .Include(x => x.GrowerNumbers)
            .Where(x => x.IsActive && x.MergedIntoCanonicalGrowerId == null)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);

        return growers
            .Select(x => new CanonicalGrowerOptionViewModel
            {
                Id = x.Id,
                DisplayName = x.DisplayName,
                Aliases = string.Join(", ", x.Aliases.Where(a => a.IsActive).Select(a => a.AliasName).OrderBy(a => a)),
                GrowerNumbers = string.Join(", ", x.GrowerNumbers.Where(n => n.IsActive).Select(n => n.GrowerNumber).OrderBy(n => n))
            })
            .ToList();
    }

    private static IReadOnlyList<CanonicalGrowerOptionViewModel> BuildSuggestedGrowers(GrowerMappingForm form, IReadOnlyList<CanonicalGrowerOptionViewModel> growers)
    {
        var sourceKey = CanonicalGrowerService.NormalizeGrowerKey(form.SourceGrowerName);
        var sourceNumber = CanonicalGrowerService.NormalizeGrowerNumber(form.GrowerNumber);
        return growers
            .Select(grower =>
            {
                var nameKey = CanonicalGrowerService.NormalizeGrowerKey(grower.DisplayName);
                var aliasKeys = ParseLines(grower.Aliases).Select(CanonicalGrowerService.NormalizeGrowerKey).ToList();
                var numberKeys = ParseLines(grower.GrowerNumbers).Select(CanonicalGrowerService.NormalizeGrowerNumber).ToList();
                var reason = "";
                if (sourceNumber.Length > 0 && numberKeys.Contains(sourceNumber, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "Exact grower-number match";
                }
                else if (nameKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) || aliasKeys.Contains(sourceKey, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "Exact normalized name or alias match";
                }
                else if (sourceKey.EndsWith("_NON_CHILEAN", StringComparison.OrdinalIgnoreCase)
                    && nameKey.Equals(sourceKey[..^"_NON_CHILEAN".Length], StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Name matches after Non Chilean suffix";
                }

                grower.IsSuggested = reason.Length > 0;
                grower.SuggestionReason = reason;
                return grower;
            })
            .Where(x => x.IsSuggested)
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    private static string CleanSuggestedCanonicalName(string value)
    {
        var name = value.Trim();
        if (name.EndsWith(" Non Chilean", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^" Non Chilean".Length].Trim();
        }

        return name;
    }

    private async Task<MasterDataEditForm?> GetCanonicalGrowerEditFormAsync(string type, int id, CancellationToken ct)
    {
        var grower = await dbContext.CanonicalGrowers.AsNoTracking()
            .Include(x => x.Aliases)
            .Include(x => x.GrowerNumbers)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (grower is null) return null;
        return new MasterDataEditForm
        {
            Type = type,
            Id = grower.Id,
            Name = grower.DisplayName,
            IsActive = grower.IsActive,
            GrowerAliases = string.Join(Environment.NewLine, grower.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
            GrowerNumbers = string.Join(Environment.NewLine, grower.GrowerNumbers.Where(x => x.IsActive).Select(x => x.GrowerNumber).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        };
    }

    private async Task<string?> SaveCanonicalGrower(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name)) return "Canonical grower name is required.";
        var numberValues = ParseLines(form.GrowerNumbers).ToList();
        var numberKeys = numberValues.Select(CanonicalGrowerService.NormalizeGrowerNumber).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (reviewedGrowerLotPolicy is not null)
        {
            var reviewed = await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(ct);
            if (form.IsActive)
            {
                if (numberKeys.Count != 1 || !reviewed.TryGetValue(numberKeys[0], out var reviewedRow))
                    return "Active Growers must use exactly one active number from the reviewed Grower master.";
                if (!form.Name.Trim().Equals(reviewedRow.GrowerName, StringComparison.Ordinal)
                    || !numberValues.Single().Trim().Equals(reviewedRow.GrowerNumber, StringComparison.Ordinal))
                    return "The active Grower name and number must exactly match the reviewed Grower master.";
            }
            else if (numberKeys.Any(reviewed.ContainsKey))
            {
                return "An active reviewed Grower cannot be made inactive manually.";
            }
        }
        var normalizedKey = CanonicalGrowerService.NormalizeGrowerKey(form.Name);
        if (await dbContext.CanonicalGrowers.AnyAsync(x => x.NormalizedKey == normalizedKey && x.Id != (form.Id ?? 0), ct))
        {
            return "A canonical grower with that normalized name already exists.";
        }

        var aliasNames = ParseLines(form.GrowerAliases).Append(form.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var aliasKeys = aliasNames.Select(CanonicalGrowerService.NormalizeGrowerKey).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existingAliasConflict = await dbContext.CanonicalGrowerAliases
            .Include(x => x.CanonicalGrower)
            .Where(x => x.IsActive && aliasKeys.Contains(x.NormalizedAliasKey) && x.CanonicalGrowerId != (form.Id ?? 0))
            .Select(x => x.CanonicalGrower.DisplayName)
            .Distinct()
            .ToListAsync(ct);
        if (existingAliasConflict.Count > 0)
        {
            return $"Alias already belongs to another canonical grower: {string.Join(", ", existingAliasConflict)}.";
        }

        var numberConflicts = await dbContext.CanonicalGrowerNumbers
            .Include(x => x.CanonicalGrower)
            .Where(x => x.IsActive && numberKeys.Contains(x.NormalizedGrowerNumber) && x.CanonicalGrowerId != (form.Id ?? 0))
            .Select(x => x.CanonicalGrower.DisplayName)
            .Distinct()
            .ToListAsync(ct);
        if (numberConflicts.Count > 0)
        {
            return $"Grower number already belongs to another canonical grower: {string.Join(", ", numberConflicts)}.";
        }

        var entity = form.Id is null
            ? new CanonicalGrower { DisplayName = "", NormalizedKey = "" }
            : await dbContext.CanonicalGrowers.Include(x => x.Aliases).Include(x => x.GrowerNumbers).SingleOrDefaultAsync(x => x.Id == form.Id.Value, ct);
        if (entity is null) return "Canonical grower not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
        entity.DisplayName = form.Name.Trim();
        entity.NormalizedKey = normalizedKey;
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (form.Id is null)
        {
            entity.CreatedAt = entity.UpdatedAt;
            dbContext.CanonicalGrowers.Add(entity);
        }

        ReplaceCanonicalGrowerAliases(entity, aliasNames);
        ReplaceCanonicalGrowerNumbers(entity, numberValues);

        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(form.Id is null ? "create" : "update", "canonical-growers", entity.Id.ToString(), by, before, JsonSerializer.Serialize(new
        {
            entity.Id,
            entity.DisplayName,
            entity.NormalizedKey,
            Aliases = entity.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).OrderBy(x => x),
            GrowerNumbers = entity.GrowerNumbers.Where(x => x.IsActive).Select(x => x.GrowerNumber).OrderBy(x => x),
            entity.IsActive
        }), ct);
        await dbContext.SaveChangesAsync(ct);
        (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).InvalidateResolutionSet();
        return null;
    }

    private async Task<string?> SaveGrowerLot(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name) || Blank(form.Code)) return "Grower and Lot # are required.";
        var grower = form.Name.Trim();
        var lotNumber = form.Code.Trim();
        var entity = form.Id is null ? new GrowerLot { Grower = "", LotNumber = "" } : await dbContext.GrowerLots.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Grower lot not found.";
        if (reviewedGrowerLotPolicy is not null)
        {
            var reviewed = await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(ct);
            var submittedNumber = CanonicalGrowerService.NormalizeGrowerNumber(lotNumber);
            if (form.Id is null)
            {
                if (!form.IsActive || !reviewed.TryGetValue(submittedNumber, out var newReviewed))
                    return "A new active Grower Lot must use a number from the reviewed Grower master.";
                if (await dbContext.GrowerLots.AsNoTracking().AnyAsync(x => x.LotNumber == newReviewed.GrowerNumber, ct))
                    return "That reviewed Grower Number already has a Grower Lot. Use the alignment sync instead of creating another row.";
                grower = newReviewed.GrowerName;
                lotNumber = newReviewed.GrowerNumber;
            }
            else
            {
                var currentNumber = CanonicalGrowerService.NormalizeGrowerNumber(entity.LotNumber);
                if (reviewed.TryGetValue(currentNumber, out var currentReviewed))
                {
                    if (!form.IsActive) return "Current Grower Lots are controlled by the reviewed Grower master and cannot be deactivated manually.";
                    if (!submittedNumber.Equals(currentNumber, StringComparison.OrdinalIgnoreCase))
                        return "The Grower Number of a current reviewed Grower Lot cannot be changed manually.";
                    grower = currentReviewed.GrowerName;
                    lotNumber = currentReviewed.GrowerNumber;
                }
                else
                {
                    if (form.IsActive) return "Legacy Grower Lots cannot be activated manually; use the reviewed alignment sync after resolving the Grower identity.";
                    if (!grower.Equals(entity.Grower, StringComparison.Ordinal) || !lotNumber.Equals(entity.LotNumber, StringComparison.Ordinal))
                        return "Historical Grower Lot identity cannot be renamed or renumbered. Notes and PoolStart may still be maintained.";
                    grower = entity.Grower;
                    lotNumber = entity.LotNumber;
                }
            }
        }
        var duplicateNumber = (await dbContext.GrowerLots.AsNoTracking().Where(x => x.Id != (form.Id ?? 0)).Select(x => x.LotNumber).ToListAsync(ct))
            .Any(x => CanonicalGrowerService.NormalizeGrowerNumber(x).Equals(CanonicalGrowerService.NormalizeGrowerNumber(lotNumber), StringComparison.OrdinalIgnoreCase));
        if (duplicateNumber)
            return "Grower Number must identify exactly one Grower Lot.";
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

    private async Task<MasterDataEditForm?> GetOrchardBlockEditFormAsync(string type, int id, CancellationToken ct)
    {
        var block = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (block is null)
        {
            return null;
        }

        return new MasterDataEditForm
        {
            Type = type,
            Id = block.Id,
            Name = block.OrchardName,
            Code = block.CanonicalBlockName,
            Description = block.Notes,
            BlockAliases = string.Join(Environment.NewLine, block.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).OrderBy(x => x)),
            IsActive = block.IsActive
        };
    }

    private async Task<string?> SaveOrchardBlock(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name) || Blank(form.Code))
        {
            return "Orchard and canonical block are required.";
        }

        var orchardName = form.Name.Trim();
        var blockName = form.Code.Trim();
        var orchardIdentity = OrchardIdentityClassifier.Classify(orchardName, OrchardIdentitySource.AmbiguousOrchardOrGrower);
        if (orchardIdentity.Kind == OrchardIdentityKind.GrowerNumber)
        {
            return $"{orchardIdentity.Value} looks like a four-digit grower number, not an orchard. Enter the orchard name separately.";
        }

        var orchardKey = OrchardBlockMatcher.Normalize(orchardName);
        var blockKey = OrchardBlockMatcher.Normalize(blockName);
        if (await dbContext.CanonicalOrchardBlocks.AnyAsync(x => x.NormalizedOrchardKey == orchardKey && x.NormalizedBlockKey == blockKey && x.Id != (form.Id ?? 0), ct))
        {
            return "That canonical block already exists for this orchard.";
        }

        var entity = form.Id is null
            ? new CanonicalOrchardBlock { OrchardName = "", CanonicalBlockName = "" }
            : await dbContext.CanonicalOrchardBlocks.Include(x => x.Aliases).SingleOrDefaultAsync(x => x.Id == form.Id.Value, ct);
        if (entity is null)
        {
            return "Orchard block not found.";
        }

        var before = form.Id is null ? null : JsonSerializer.Serialize(entity, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
        var canonicalOrchard = await dbContext.CanonicalOrchards.SingleOrDefaultAsync(x => x.NormalizedOrchardKey == orchardKey, ct);
        if (canonicalOrchard is null)
        {
            canonicalOrchard = new CanonicalOrchard
            {
                OrchardName = orchardName,
                NormalizedOrchardKey = orchardKey,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.CanonicalOrchards.Add(canonicalOrchard);
        }

        entity.CanonicalOrchard = canonicalOrchard;
        entity.OrchardName = orchardName;
        entity.CanonicalBlockName = blockName;
        entity.NormalizedOrchardKey = orchardKey;
        entity.NormalizedBlockKey = blockKey;
        entity.Notes = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (form.Id is null)
        {
            entity.CreatedAt = entity.UpdatedAt;
            dbContext.CanonicalOrchardBlocks.Add(entity);
        }

        var aliasNames = SplitLines(form.BlockAliases)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var alias in entity.Aliases)
        {
            alias.IsActive = aliasNames.Any(x => string.Equals(OrchardBlockMatcher.Normalize(x), alias.NormalizedAliasKey, StringComparison.Ordinal));
            alias.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var aliasName in aliasNames)
        {
            var aliasKey = OrchardBlockMatcher.Normalize(aliasName);
            if (string.Equals(aliasKey, blockKey, StringComparison.Ordinal))
            {
                continue;
            }

            var alias = entity.Aliases.SingleOrDefault(x => x.NormalizedAliasKey == aliasKey);
            if (alias is null)
            {
                entity.Aliases.Add(new OrchardBlockAlias
                {
                    AliasName = aliasName,
                    NormalizedAliasKey = aliasKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                alias.AliasName = aliasName;
                alias.IsActive = true;
                alias.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(form.Id is null ? "create" : "update", "orchard-blocks", entity.Id.ToString(), by, before, JsonSerializer.Serialize(new
        {
            entity.Id,
            entity.OrchardName,
            entity.CanonicalBlockName,
            entity.NormalizedOrchardKey,
            entity.NormalizedBlockKey,
            Aliases = entity.Aliases.Where(x => x.IsActive).Select(x => x.AliasName).OrderBy(x => x),
            entity.IsActive
        }), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<GrowerLotImportPreviewViewModel> BuildGrowerLotImportPreviewAsync(string csvText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            return new GrowerLotImportPreviewViewModel
            {
                CsvText = "",
                Rows = [new(0, "", "", "", "Invalid", "Upload a CSV file before previewing import.", false)],
                InvalidCount = 1
            };
        }

        var parsedRows = ParseCsv(csvText).ToList();
        if (parsedRows.Count < 2)
        {
            return new GrowerLotImportPreviewViewModel
            {
                CsvText = csvText,
                Rows = [new(0, "", "", "", "Invalid", "CSV must include a header row and at least one data row.", false)],
                InvalidCount = 1
            };
        }

        var headers = parsedRows[0].Select(NormalizeHeader).ToList();
        var growerIndex = FindHeader(headers, ["grower", "growername"]);
        var lotIndex = FindHeader(headers, ["#", "grower#", "growernumber", "lot#", "lotnumber"]);
        var poolIndex = FindHeader(headers, ["poolstarts", "poolstart", "poolcode"]);
        if (growerIndex < 0 || lotIndex < 0 || poolIndex < 0)
        {
            return new GrowerLotImportPreviewViewModel
            {
                CsvText = csvText,
                Rows = [new(0, "", "", "", "Invalid", "CSV headers must include Grower, Lot #, and Pool Start columns. Supported aliases include GrowerName, #, Grower Number, Lot Number, POOL Starts, and PoolCode.", false)],
                InvalidCount = 1
            };
        }

        var reviewedGrowers = reviewedGrowerLotPolicy is null
            ? null
            : await reviewedGrowerLotPolicy.GetActiveReviewedGrowersAsync(ct);
        var existingByLot = (await dbContext.GrowerLots.AsNoTracking().ToListAsync(ct))
            .GroupBy(x => reviewedGrowers is null ? x.LotNumber.Trim() : CanonicalGrowerService.NormalizeGrowerNumber(x.LotNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var seenLots = new Dictionary<string, GrowerLotImportPreviewRow>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<GrowerLotImportPreviewRow>();

        for (var i = 1; i < parsedRows.Count; i++)
        {
            var raw = parsedRows[i];
            var rowNumber = i + 1;
            var grower = GetCell(raw, growerIndex).Trim();
            var lotNumber = GetCell(raw, lotIndex).Trim();
            var poolStart = GetCell(raw, poolIndex).Trim();
            var isInactive = grower.StartsWith("INACTIVE", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(grower) && string.IsNullOrWhiteSpace(lotNumber) && string.IsNullOrWhiteSpace(poolStart))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(grower) || string.IsNullOrWhiteSpace(lotNumber))
            {
                rows.Add(new(rowNumber, grower, lotNumber, poolStart, "Invalid", "Grower and Lot # are required.", isInactive));
                continue;
            }

            if (reviewedGrowers is not null)
            {
                var normalizedNumber = CanonicalGrowerService.NormalizeGrowerNumber(lotNumber);
                if (isInactive || !reviewedGrowers.TryGetValue(normalizedNumber, out var reviewed))
                {
                    rows.Add(new(rowNumber, grower, lotNumber, poolStart, "Invalid", "Active Grower Lot imports may contain only active Grower Numbers from the reviewed Grower master.", isInactive));
                    continue;
                }
                grower = reviewed.GrowerName;
                lotNumber = reviewed.GrowerNumber;
                isInactive = false;
            }

            if (seenLots.TryGetValue(lotNumber, out var firstSeen)
                && (!string.Equals(firstSeen.Grower, grower, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(firstSeen.PoolStart, poolStart, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new(rowNumber, grower, lotNumber, poolStart, "Conflict", $"Duplicate Lot # {lotNumber} conflicts with row {firstSeen.RowNumber}.", isInactive));
                continue;
            }

            seenLots.TryAdd(lotNumber, new(rowNumber, grower, lotNumber, poolStart, "Seen", "", isInactive));

            var lotKey = reviewedGrowers is null ? lotNumber : CanonicalGrowerService.NormalizeGrowerNumber(lotNumber);
            if (!existingByLot.TryGetValue(lotKey, out var matches))
            {
                var newPoolStart = reviewedGrowers is null ? poolStart : "";
                rows.Add(new(rowNumber, grower, lotNumber, newPoolStart, "Add", reviewedGrowers is null ? (isInactive ? "New inactive lot." : "New active lot.") : "New reviewed Grower Lot; PoolStart remains unset until explicitly maintained.", isInactive));
                continue;
            }

            if (matches.Count > 1)
            {
                rows.Add(new(rowNumber, grower, lotNumber, poolStart, "Conflict", $"Existing master data has multiple records for Lot # {lotNumber}. Resolve duplicates before importing.", isInactive));
                continue;
            }

            var existing = matches[0];
            var active = !isInactive;
            var nameMatches = string.Equals(existing.Grower, grower, reviewedGrowers is null ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (nameMatches
                && (reviewedGrowers is not null || string.Equals(existing.PoolStart ?? "", poolStart, StringComparison.OrdinalIgnoreCase))
                && existing.IsActive == active)
            {
                rows.Add(new(rowNumber, grower, lotNumber, reviewedGrowers is null ? poolStart : existing.PoolStart ?? "", "Unchanged", "No changes.", isInactive));
            }
            else
            {
                rows.Add(new(rowNumber, grower, lotNumber, reviewedGrowers is null ? poolStart : existing.PoolStart ?? "", "Update", reviewedGrowers is null ? "Existing Lot # will be updated. Notes are preserved." : "Current name/status will align to the reviewed Grower master. Existing ID, PoolStart, notes, and references are preserved.", isInactive));
            }
        }

        return new GrowerLotImportPreviewViewModel
        {
            CsvText = csvText,
            Rows = rows,
            AddCount = rows.Count(x => x.Action == "Add"),
            UpdateCount = rows.Count(x => x.Action == "Update"),
            UnchangedCount = rows.Count(x => x.Action == "Unchanged"),
            ConflictCount = rows.Count(x => x.Action == "Conflict"),
            InvalidCount = rows.Count(x => x.Action == "Invalid"),
            InactiveCount = rows.Count(x => x.IsInactive)
        };
    }

    private static async Task<string> ReadCsvTextAsync(GrowerLotImportForm form, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(form.CsvText))
        {
            return form.CsvText;
        }

        if (form.CsvFile is null || form.CsvFile.Length == 0)
        {
            return "";
        }

        using var reader = new StreamReader(form.CsvFile.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

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
                cell.Clear();
                yield return row;
                row = [];
                continue;
            }

            cell.Append(ch);
        }

        row.Add(cell.ToString());
        if (row.Any(x => !string.IsNullOrWhiteSpace(x)) || text.EndsWith(",", StringComparison.Ordinal))
        {
            yield return row;
        }
    }

    private static int FindHeader(IReadOnlyList<string> headers, IReadOnlyList<string> aliases)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (aliases.Contains(headers[i], StringComparer.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : "";

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
        var warehouse = await dbContext.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.WarehouseId.Value, ct);
        if (warehouse is null) return "Warehouse not found.";
        var entity = form.Id is null ? new Room { Code = "", Name = "" } : await dbContext.Rooms.Include(x => x.EndOfDayFillReportGroup).SingleOrDefaultAsync(x => x.Id == form.Id.Value, ct);
        if (entity is null) return "Room not found.";
        EndOfDayFillReportGroup? reportGroup = null;
        if (form.EndOfDayFillReportGroupId is int reportGroupId)
        {
            reportGroup = await dbContext.EndOfDayFillReportGroups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reportGroupId, ct);
            if (reportGroup is null) return "The selected End of Day Fill report does not exist.";
            var preservesCurrentInactiveAssignment = form.Id is not null && entity.EndOfDayFillReportGroupId == reportGroupId;
            if (!reportGroup.IsActive && !preservesCurrentInactiveAssignment)
                return "The selected End of Day Fill report is inactive and cannot be newly assigned.";
            if (reportGroup.WarehouseId != warehouse.Id)
                return $"{endOfDayFillWarehouseLabelResolver.Resolve(warehouse.Id, warehouse.Code, warehouse.Name)} rooms can only be assigned to an End of Day Fill report for that exact warehouse.";
        }
        var action = form.Id is null ? "create" : "update";
        var previousGroupId = entity.EndOfDayFillReportGroupId;
        var previousGroupName = entity.EndOfDayFillReportGroup?.Name;
        var before = form.Id is null ? null : JsonSerializer.Serialize(new
        {
            RoomId = entity.Id,
            entity.Code,
            entity.WarehouseId,
            entity.Name,
            entity.CompuTechRoomCode,
            entity.CapacityBins,
            entity.IsActive,
            EndOfDayFillReportGroupId = previousGroupId,
            EndOfDayFillReportGroup = previousGroupName
        });
        entity.WarehouseId = form.WarehouseId.Value;
        entity.Code = form.Code.Trim();
        entity.Name = form.Name.Trim();
        entity.CompuTechRoomCode = Blank(form.CompuTechCode) ? null : form.CompuTechCode!.Trim();
        entity.CapacityBins = form.CapacityBins;
        entity.IsActive = form.IsActive;
        entity.EndOfDayFillReportGroupId = form.EndOfDayFillReportGroupId;
        if (form.Id is null) dbContext.Rooms.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(action, "rooms", entity.Id.ToString(), by, before, JsonSerializer.Serialize(new
        {
            RoomId = entity.Id,
            entity.Code,
            entity.WarehouseId,
            entity.Name,
            entity.CompuTechRoomCode,
            entity.CapacityBins,
            entity.IsActive,
            PreviousEndOfDayFillReportGroupId = previousGroupId,
            PreviousEndOfDayFillReportGroup = previousGroupName,
            EndOfDayFillReportGroupId = reportGroup?.Id,
            EndOfDayFillReportGroup = reportGroup?.Name
        }), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<MasterDataEditForm?> WithEndOfDayFillReportGroupsAsync(MasterDataEditForm? form, CancellationToken ct)
    {
        if (form is null) return null;
        var currentGroupId = form.EndOfDayFillReportGroupId;
        var groups = await dbContext.EndOfDayFillReportGroups.AsNoTracking()
            .Include(x => x.Warehouse)
            .Where(x => x.IsActive || x.Id == currentGroupId)
            .OrderBy(x => x.Warehouse.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        form.EndOfDayFillReportGroups = groups
            .Select(x => new EndOfDayFillGroupOption(
                x.Id,
                x.Name,
                x.Facility,
                x.IsActive,
                x.Id == currentGroupId,
                x.WarehouseId,
                endOfDayFillWarehouseLabelResolver.Resolve(x.WarehouseId, x.Warehouse.Code, x.Warehouse.Name)))
            .ToList();
        return form;
    }

    private async Task<string?> SaveFruitProfile(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Code) || Blank(form.Name) || Blank(form.FruitType) || Blank(form.ProductionType)) return "Variety code, name, commodity, and production type are required.";
        if (!IsValidProductionType(form.ProductionType)) return "Production type must be Conventional or Organic.";
        if (!form.ResetVarietyColor && !Blank(form.VarietyHexColor) && !VarietyColorService.IsValidHexColor(VarietyColorService.NormalizeHex(form.VarietyHexColor))) return "Enter a valid hex color such as #2F80ED.";
        var normalizedCode = form.Code.Trim().ToUpper();
        if (await dbContext.FruitProfiles.AnyAsync(x => x.VarietyCode.ToUpper() == normalizedCode && x.Id != (form.Id ?? 0), ct)) return "Variety code must be unique.";
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
        return await SaveFruitProfileColorAsync(entity, form, by, ct);
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

    private async Task<string?> SaveTreatmentChemical(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.ProductName) || Blank(form.Crop) || Blank(form.ApplicationLevel) || form.Volume is null || Blank(form.Unit)
            || form.UnitPrice is null || Blank(form.Currency))
            return "Product name, crop, application level, volume, unit, unit price, and currency are required.";
        if (form.ProductName.Trim().Length > 200 || form.CommonName?.Trim().Length > 200)
            return "Product and common names cannot exceed 200 characters.";
        if (form.Volume <= 0) return "Volume must be greater than zero.";
        if (form.UnitPrice < 0) return "Unit price cannot be negative.";
        var crop = form.Crop.Trim() switch { var x when x.Equals("Apple", StringComparison.OrdinalIgnoreCase) || x.Equals("Apples", StringComparison.OrdinalIgnoreCase) => "Apples", var x when x.Equals("Pear", StringComparison.OrdinalIgnoreCase) || x.Equals("Pears", StringComparison.OrdinalIgnoreCase) => "Pears", _ => "" };
        if (crop.Length == 0) return "Crop must be Apples or Pears.";
        var applicationLevel = form.ApplicationLevel.Trim();
        if (!TreatmentApplicationLevels.IsValid(applicationLevel)) return "Application level must be Room or Receiving.";
        var currency = form.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3) return "Currency must be a three-letter code such as USD.";
        if (await dbContext.TreatmentChemicals.AnyAsync(x => x.ProductName == form.ProductName.Trim() && x.Id != (form.Id ?? 0), ct))
            return "Official product name must be unique.";
        var actor = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == by, ct);
        var entity = form.Id is null
            ? new TreatmentChemical { ProductName = "", Crop = "", Unit = "", Currency = "", CreatedAt = BusinessTime.UtcNow, UpdatedAt = BusinessTime.UtcNow, CreatedByUserId = actor?.Id }
            : await dbContext.TreatmentChemicals.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Treatment chemical was not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(entity);
        entity.ProductName = form.ProductName.Trim();
        entity.CommonName = Blank(form.CommonName) ? null : form.CommonName!.Trim();
        entity.Crop = crop;
        entity.ApplicationLevel = applicationLevel;
        entity.Volume = form.Volume.Value;
        entity.Unit = form.Unit.Trim().ToUpperInvariant();
        entity.UnitPrice = form.UnitPrice.Value;
        entity.Currency = currency;
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = BusinessTime.UtcNow;
        entity.UpdatedByUserId = actor?.Id;
        if (form.Id is null) dbContext.TreatmentChemicals.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(form.Id is null ? "create" : "update", "treatment-chemicals", entity.Id.ToString(), by, before, JsonSerializer.Serialize(entity), ct);
        await dbContext.SaveChangesAsync(ct);
        return null;
    }

    private async Task<string?> SaveProcessor(MasterDataEditForm form, string by, CancellationToken ct)
    {
        if (Blank(form.Name)) return "Processor name is required.";
        if (form.Name.Trim().Length > 200 || form.Code.Trim().Length > 50 || form.Description?.Trim().Length > 1000)
            return "Processor name, code, or notes are too long.";
        var actor = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == by, ct);
        var entity = form.Id is null
            ? new Processor { Name = "", CreatedAt = BusinessTime.UtcNow, UpdatedAt = BusinessTime.UtcNow, CreatedByUserId = actor?.Id }
            : await dbContext.Processors.FindAsync([form.Id.Value], ct);
        if (entity is null) return "Processor was not found.";
        var before = form.Id is null ? null : JsonSerializer.Serialize(new { entity.Name, entity.Code, entity.Notes, entity.IsActive });
        entity.Name = form.Name.Trim();
        entity.Code = Blank(form.Code) ? null : form.Code.Trim();
        entity.Notes = Blank(form.Description) ? null : form.Description!.Trim();
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = BusinessTime.UtcNow;
        entity.UpdatedByUserId = actor?.Id;
        if (form.Id is null) dbContext.Processors.Add(entity);
        await dbContext.SaveChangesAsync(ct);
        await AddAuditAsync(form.Id is null ? "ProcessorCreated" : "ProcessorUpdated", "Processor", entity.Id.ToString(), by, before, JsonSerializer.Serialize(new { entity.Name, entity.Code, entity.IsActive }), ct);
        await dbContext.SaveChangesAsync(ct);
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

    private async Task<MasterDataEditForm?> WithFruitProfileColorAsync(MasterDataEditForm? form, CancellationToken ct)
    {
        if (form is null) return null;
        var profile = new FruitProfile
        {
            Name = form.Name,
            VarietyCode = form.Code,
            FruitType = form.FruitType,
            ProductionType = form.ProductionType,
            IsOrganic = NormalizeProductionType(form.ProductionType) == "Organic",
            IsActive = form.IsActive
        };
        var identity = VarietyColorService.IdentityFromProfile(profile);
        var colorMap = await varietyColorService.GetResolvedColorsAsync([identity.Key], ct);
        colorMap.TryGetValue(identity.Key, out var color);
        form.VarietyColorKey = identity.Key;
        form.CanonicalVarietyName = color?.VarietyName ?? identity.Name;
        form.VarietyAliases = VarietyColorService.AliasesForIdentity(identity);
        form.VarietyFallbackColor = VarietyColorService.FallbackColor(identity.Key);
        form.VarietyHexColor = color?.HexColor ?? form.VarietyFallbackColor;
        form.VarietyColorIsConfigured = color?.IsConfigured == true;
        return form;
    }

    private async Task<string?> SaveFruitProfileColorAsync(FruitProfile profile, MasterDataEditForm form, string by, CancellationToken ct)
    {
        var identity = VarietyColorService.IdentityFromProfile(profile);
        var fallback = VarietyColorService.FallbackColor(identity.Key);
        if (form.ResetVarietyColor)
        {
            return await varietyColorService.ResetAsync(new VarietyColorForm { VarietyKey = identity.Key, VarietyName = identity.Name }, by, ct);
        }

        if (Blank(form.VarietyHexColor))
        {
            return null;
        }

        var color = VarietyColorService.NormalizeHex(form.VarietyHexColor);
        if (!VarietyColorService.IsValidHexColor(color))
        {
            return "Enter a valid hex color such as #2F80ED.";
        }

        if (!form.VarietyColorIsConfigured && color.Equals(fallback, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await varietyColorService.SaveAsync(new VarietyColorForm { VarietyKey = identity.Key, VarietyName = identity.Name, HexColor = color }, by, ct);
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

    private static IReadOnlyList<string> ParseLines(string? value) =>
        (value ?? "")
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !Blank(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void ReplaceCanonicalGrowerAliases(CanonicalGrower grower, IReadOnlyList<string> aliasNames)
    {
        foreach (var alias in grower.Aliases)
        {
            alias.IsActive = false;
            alias.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var aliasName in aliasNames)
        {
            var key = CanonicalGrowerService.NormalizeGrowerKey(aliasName);
            if (key.Length == 0) continue;
            var alias = grower.Aliases.FirstOrDefault(x => x.NormalizedAliasKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (alias is null)
            {
                grower.Aliases.Add(new CanonicalGrowerAlias
                {
                    AliasName = aliasName.Trim(),
                    NormalizedAliasKey = key,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                });
            }
            else
            {
                alias.AliasName = aliasName.Trim();
                alias.IsActive = true;
                alias.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static void ReplaceCanonicalGrowerNumbers(CanonicalGrower grower, IReadOnlyList<string> numberValues)
    {
        foreach (var number in grower.GrowerNumbers)
        {
            number.IsActive = false;
            number.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var numberValue in numberValues)
        {
            var key = CanonicalGrowerService.NormalizeGrowerNumber(numberValue);
            if (key.Length == 0) continue;
            var number = grower.GrowerNumbers.FirstOrDefault(x => x.NormalizedGrowerNumber.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (number is null)
            {
                grower.GrowerNumbers.Add(new CanonicalGrowerNumber
                {
                    GrowerNumber = numberValue.Trim(),
                    NormalizedGrowerNumber = key,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                });
            }
            else
            {
                number.GrowerNumber = numberValue.Trim();
                number.IsActive = true;
                number.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static IEnumerable<string> SplitLines(string? value) =>
        (value ?? "").Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static IReadOnlyList<(string Label, string Href)> MasterDataLinks() =>
    [
        ("Warehouses", "/MasterData/warehouses"),
        ("Rooms", "/MasterData/rooms"),
        ("Fruit profiles / variety codes", "/MasterData/fruit-profiles"),
        ("Grades", "/MasterData/grades"),
        ("Defects", "/MasterData/defects"),
        ("Sample types", "/MasterData/sample-types"),
        ("Canonical Growers", "/MasterData/canonical-growers"),
        ("Orchard Blocks", "/MasterData/orchard-blocks"),
        ("Grower Lots", "/MasterData/grower-lots"),
        ("Starch scale values", "/MasterData/starch-scale-values"),
        ("Size thresholds", "/MasterData/size-thresholds"),
        ("Treatment Chemicals", "/MasterData/treatment-chemicals"),
        ("Processors", "/MasterData/processors")
    ];
}
