using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface ICommercialPackAdminService
{
    Task<CommercialPackAdminPageViewModel> GetPageAsync(int? editPlanId, int? editPackId, CancellationToken cancellationToken);
    Task<string?> SavePlanAsync(CommercialPackPlanForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> SavePackAsync(CommercialPackDefinitionForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> SavePlanItemAsync(CommercialPackPlanItemForm form, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> RemovePlanItemAsync(int planId, int packId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> DeactivatePlanAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> DeactivatePackAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class CommercialPackAdminService(
    CropQcDbContext dbContext,
    IBusinessTimeService businessTime) : ICommercialPackAdminService
{
    public async Task<CommercialPackAdminPageViewModel> GetPageAsync(
        int? editPlanId,
        int? editPackId,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.CommercialPackPlans.AsNoTracking()
            .Include(x => x.UpdatedByUser)
            .OrderBy(x => x.Commodity).ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var packs = await dbContext.CommercialPackDefinitions.AsNoTracking()
            .Include(x => x.EligibleSizes)
            .Include(x => x.FruitProfileRestrictions).ThenInclude(x => x.FruitProfile)
            .Include(x => x.UpdatedByUser)
            .OrderBy(x => x.Commodity).ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var planItems = await dbContext.CommercialPackPlanItems.AsNoTracking()
            .Include(x => x.CommercialPackPlan)
            .Include(x => x.CommercialPackDefinition)
            .OrderBy(x => x.CommercialPackPlan.DisplayName).ThenBy(x => x.Priority)
            .ToListAsync(cancellationToken);
        var profiles = await dbContext.FruitProfiles.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.FruitType).ThenBy(x => x.Name)
            .Select(x => new CommercialPackFruitProfileOption(x.Id, x.Name, x.FruitType))
            .ToListAsync(cancellationToken);

        var planToEdit = editPlanId is int planId ? plans.SingleOrDefault(x => x.Id == planId) : null;
        var packToEdit = editPackId is int packId ? packs.SingleOrDefault(x => x.Id == packId) : null;
        return new CommercialPackAdminPageViewModel
        {
            Plans = plans.Select(x => new CommercialPackPlanAdminRow(
                x.Id, x.Code, x.DisplayName, x.Commodity, x.PlanType,
                x.EffectiveCropYearStart, x.EffectiveCropYearEnd, x.IsActive,
                x.UpdatedAt, x.UpdatedByUser?.DisplayName ?? "")).ToList(),
            Packs = packs.Select(x => new CommercialPackDefinitionAdminRow(
                x.Id, x.Code, x.DisplayName, x.Commodity, x.PackType, x.PackageWeightPounds,
                x.AllowsMixedSizes, x.MixRule,
                string.Join(", ", x.EligibleSizes.OrderBy(y => y.Priority).Select(y =>
                    y.TargetPercent is null ? y.SizeCategory.ToString() : $"{y.SizeCategory} ({y.TargetPercent:0.##}%)")),
                x.FruitProfileRestrictions.Count == 0
                    ? "All varieties"
                    : string.Join(", ", x.FruitProfileRestrictions.Select(y => y.FruitProfile.Name).OrderBy(y => y)),
                x.IsActive, x.UpdatedAt, x.UpdatedByUser?.DisplayName ?? "")).ToList(),
            PlanItems = planItems.Select(x => new CommercialPackPlanItemAdminRow(
                x.CommercialPackPlanId, x.CommercialPackPlan.DisplayName,
                x.CommercialPackDefinitionId, x.CommercialPackDefinition.DisplayName,
                x.Priority)).ToList(),
            FruitProfiles = profiles,
            PlanForm = planToEdit is null
                ? new CommercialPackPlanForm()
                : new CommercialPackPlanForm
                {
                    Id = planToEdit.Id,
                    Code = planToEdit.Code,
                    DisplayName = planToEdit.DisplayName,
                    Commodity = planToEdit.Commodity,
                    PlanType = planToEdit.PlanType,
                    EffectiveCropYearStart = planToEdit.EffectiveCropYearStart,
                    EffectiveCropYearEnd = planToEdit.EffectiveCropYearEnd,
                    IsActive = planToEdit.IsActive
                },
            PackForm = packToEdit is null
                ? EmptyPackForm()
                : new CommercialPackDefinitionForm
                {
                    Id = packToEdit.Id,
                    Code = packToEdit.Code,
                    DisplayName = packToEdit.DisplayName,
                    Commodity = packToEdit.Commodity,
                    PackType = packToEdit.PackType,
                    PackageWeightPounds = packToEdit.PackageWeightPounds,
                    AllowsMixedSizes = packToEdit.AllowsMixedSizes,
                    MixRule = packToEdit.MixRule,
                    EffectiveCropYearStart = packToEdit.EffectiveCropYearStart,
                    EffectiveCropYearEnd = packToEdit.EffectiveCropYearEnd,
                    IsActive = packToEdit.IsActive,
                    FruitProfileIds = packToEdit.FruitProfileRestrictions.Select(x => x.FruitProfileId).ToList(),
                    EligibleSizes = packToEdit.EligibleSizes.OrderBy(x => x.Priority).Select(x => new CommercialPackEligibleSizeForm
                    {
                        SizeCategory = x.SizeCategory,
                        Priority = x.Priority,
                        TargetPercent = x.TargetPercent,
                        MinimumPercent = x.MinimumPercent,
                        MaximumPercent = x.MaximumPercent
                    }).Concat(Enumerable.Range(0, 2).Select(_ => new CommercialPackEligibleSizeForm())).ToList()
                }
        };
    }

    public async Task<string?> SavePlanAsync(CommercialPackPlanForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(form.Code);
        var name = form.DisplayName?.Trim() ?? "";
        var commodity = form.Commodity?.Trim() ?? "";
        if (code.Length == 0 || code.Length > 50) return "Plan code is required and must be 50 characters or fewer.";
        if (name.Length == 0 || name.Length > 150) return "Plan name is required and must be 150 characters or fewer.";
        if (commodity.Length == 0 || commodity.Length > 50) return "Commodity is required.";
        if (!CommercialPackPlanTypes.All.Contains(form.PlanType)) return "Select a supported pack-plan type.";
        if (InvalidCropYearRange(form.EffectiveCropYearStart, form.EffectiveCropYearEnd)) return "Effective crop-year range is invalid.";
        if (await dbContext.CommercialPackPlans.AnyAsync(x => x.Code == code && x.Id != form.Id, cancellationToken))
        {
            return $"Pack plan code {code} already exists.";
        }

        var now = businessTime.UtcNow;
        var userId = await UserIdAsync(user, cancellationToken);
        var entity = form.Id is int id
            ? await dbContext.CommercialPackPlans.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : null;
        if (form.Id is not null && entity is null) return "Pack plan was not found.";
        var before = entity is null ? null : JsonSerializer.Serialize(PlanAudit(entity));
        if (entity is null)
        {
            entity = new CommercialPackPlan
            {
                Code = code,
                DisplayName = name,
                Commodity = commodity,
                PlanType = form.PlanType,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.CommercialPackPlans.Add(entity);
        }
        entity.Code = code;
        entity.DisplayName = name;
        entity.Commodity = commodity;
        entity.PlanType = form.PlanType;
        entity.EffectiveCropYearStart = form.EffectiveCropYearStart;
        entity.EffectiveCropYearEnd = form.EffectiveCropYearEnd;
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(form.Id is null ? "Create" : "Update", nameof(CommercialPackPlan), entity.Id, userId, before, JsonSerializer.Serialize(PlanAudit(entity)), cancellationToken);
        return null;
    }

    public async Task<string?> SavePackAsync(CommercialPackDefinitionForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(form.Code);
        var name = form.DisplayName?.Trim() ?? "";
        var commodity = form.Commodity?.Trim() ?? "";
        var sizes = form.EligibleSizes.Where(x => x.SizeCategory is > 0)
            .GroupBy(x => x.SizeCategory!.Value)
            .Select(x => x.First())
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.SizeCategory)
            .ToList();
        if (code.Length == 0 || code.Length > 50) return "Pack code is required and must be 50 characters or fewer.";
        if (name.Length == 0 || name.Length > 150) return "Pack name is required and must be 150 characters or fewer.";
        if (commodity.Length == 0 || commodity.Length > 50) return "Commodity is required.";
        if (!CommercialPackTypes.All.Contains(form.PackType)) return "Select Standard, Euro, or Other pack type.";
        if (form.PackageWeightPounds <= 0) return "Package weight must be greater than zero.";
        if (!CommercialPackMixRules.All.Contains(form.MixRule)) return "Select a supported allocation rule.";
        if (sizes.Count == 0) return "At least one eligible fruit size is required.";
        if (!form.AllowsMixedSizes && sizes.Count != 1) return "A single-size pack must have exactly one eligible fruit size.";
        if (form.AllowsMixedSizes && sizes.Count < 2) return "A mixed-size pack must have at least two eligible fruit sizes.";
        if (form.PackType == CommercialPackTypes.Euro && sizes.Count != 2) return "A Euro pack must have exactly two configured eligible fruit sizes.";
        if (!form.AllowsMixedSizes && form.MixRule != CommercialPackMixRules.SingleSize) return "A single-size pack must use the SingleSize allocation rule.";
        if (form.MixRule == CommercialPackMixRules.SingleSize && form.AllowsMixedSizes) return "SingleSize cannot be used for a mixed-size pack.";
        if (form.MixRule == CommercialPackMixRules.FixedPercentage
            && (sizes.Any(x => x.TargetPercent is null or <= 0)
                || decimal.Round(sizes.Sum(x => x.TargetPercent!.Value), 4) != 100m))
        {
            return "Fixed-percentage packs require a positive target for every eligible size totaling 100%.";
        }
        if (sizes.Any(x => InvalidPercentageRange(x.MinimumPercent, x.MaximumPercent))) return "Eligible-size minimum/maximum percentages are invalid.";
        if (form.MixRule == CommercialPackMixRules.FixedPercentage
            && sizes.Any(x => x.TargetPercent < (x.MinimumPercent ?? 0m)
                || x.TargetPercent > (x.MaximumPercent ?? 100m)))
        {
            return "Each fixed target percentage must be within its configured minimum and maximum.";
        }
        if (form.MixRule != CommercialPackMixRules.FixedPercentage
            && sizes.Any(x => x.MinimumPercent is not null || x.MaximumPercent is not null)
            && sizes.Count != 2)
        {
            return "Minimum/maximum mix limits currently require exactly two eligible sizes.";
        }
        if (InvalidCropYearRange(form.EffectiveCropYearStart, form.EffectiveCropYearEnd)) return "Effective crop-year range is invalid.";
        if (await dbContext.CommercialPackDefinitions.AnyAsync(x => x.Code == code && x.Id != form.Id, cancellationToken))
        {
            return $"Pack code {code} already exists.";
        }
        var validProfiles = await dbContext.FruitProfiles
            .Where(x => form.FruitProfileIds.Contains(x.Id) && x.FruitType == commodity)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validProfiles.Count != form.FruitProfileIds.Distinct().Count())
        {
            return "Every variety restriction must belong to the selected commodity.";
        }

        var now = businessTime.UtcNow;
        var userId = await UserIdAsync(user, cancellationToken);
        var entity = form.Id is int id
            ? await dbContext.CommercialPackDefinitions
                .Include(x => x.EligibleSizes)
                .Include(x => x.FruitProfileRestrictions)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : null;
        if (form.Id is not null && entity is null) return "Commercial pack was not found.";
        var before = entity is null ? null : JsonSerializer.Serialize(PackAudit(entity));
        if (entity is null)
        {
            entity = new CommercialPackDefinition
            {
                Code = code,
                DisplayName = name,
                Commodity = commodity,
                PackType = form.PackType,
                MixRule = form.MixRule,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.CommercialPackDefinitions.Add(entity);
        }
        entity.Code = code;
        entity.DisplayName = name;
        entity.Commodity = commodity;
        entity.PackType = form.PackType;
        entity.PackageWeightPounds = form.PackageWeightPounds;
        entity.AllowsMixedSizes = form.AllowsMixedSizes;
        entity.MixRule = form.MixRule;
        entity.EffectiveCropYearStart = form.EffectiveCropYearStart;
        entity.EffectiveCropYearEnd = form.EffectiveCropYearEnd;
        entity.IsActive = form.IsActive;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = userId;
        var requestedSizes = sizes.Select(x => x.SizeCategory!.Value).ToHashSet();
        foreach (var existing in entity.EligibleSizes.Where(x => !requestedSizes.Contains(x.SizeCategory)).ToList())
        {
            dbContext.CommercialPackEligibleSizes.Remove(existing);
        }
        foreach (var size in sizes)
        {
            var eligible = entity.EligibleSizes.SingleOrDefault(x => x.SizeCategory == size.SizeCategory)
                ?? new CommercialPackEligibleSize { SizeCategory = size.SizeCategory!.Value };
            if (eligible.Id == 0 && !entity.EligibleSizes.Contains(eligible))
            {
                entity.EligibleSizes.Add(eligible);
            }
            eligible.Priority = size.Priority;
            eligible.TargetPercent = size.TargetPercent;
            eligible.MinimumPercent = size.MinimumPercent;
            eligible.MaximumPercent = size.MaximumPercent;
        }
        var requestedProfiles = validProfiles.ToHashSet();
        foreach (var existing in entity.FruitProfileRestrictions.Where(x => !requestedProfiles.Contains(x.FruitProfileId)).ToList())
        {
            dbContext.CommercialPackFruitProfileRestrictions.Remove(existing);
        }
        var existingProfiles = entity.FruitProfileRestrictions.Select(x => x.FruitProfileId).ToHashSet();
        foreach (var profileId in requestedProfiles.Where(x => !existingProfiles.Contains(x)))
        {
            entity.FruitProfileRestrictions.Add(new CommercialPackFruitProfileRestriction { FruitProfileId = profileId });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(form.Id is null ? "Create" : "Update", nameof(CommercialPackDefinition), entity.Id, userId, before, JsonSerializer.Serialize(PackAudit(entity)), cancellationToken);
        return null;
    }

    public async Task<string?> SavePlanItemAsync(CommercialPackPlanItemForm form, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var plan = await dbContext.CommercialPackPlans.SingleOrDefaultAsync(x => x.Id == form.CommercialPackPlanId, cancellationToken);
        var pack = await dbContext.CommercialPackDefinitions.SingleOrDefaultAsync(x => x.Id == form.CommercialPackDefinitionId, cancellationToken);
        if (plan is null || pack is null) return "Select an existing pack plan and commercial pack.";
        if (!plan.IsActive || !pack.IsActive) return "Only active pack plans and commercial packs can be assigned.";
        if (!plan.Commodity.Equals("All", StringComparison.OrdinalIgnoreCase)
            && !plan.Commodity.Equals(pack.Commodity, StringComparison.OrdinalIgnoreCase))
        {
            return "Plan and pack commodities must match unless the plan commodity is All.";
        }
        var item = await dbContext.CommercialPackPlanItems.SingleOrDefaultAsync(
            x => x.CommercialPackPlanId == plan.Id && x.CommercialPackDefinitionId == pack.Id,
            cancellationToken);
        var before = item is null ? null : JsonSerializer.Serialize(new { item.Priority });
        if (item is null)
        {
            item = new CommercialPackPlanItem
            {
                CommercialPackPlanId = plan.Id,
                CommercialPackDefinitionId = pack.Id
            };
            dbContext.CommercialPackPlanItems.Add(item);
        }
        item.Priority = Math.Max(0, form.Priority);
        await dbContext.SaveChangesAsync(cancellationToken);
        var userId = await UserIdAsync(user, cancellationToken);
        await AuditAsync(before is null ? "AssignPack" : "UpdatePackPriority", nameof(CommercialPackPlan), plan.Id, userId, before, JsonSerializer.Serialize(new { PackId = pack.Id, item.Priority }), cancellationToken);
        return null;
    }

    public async Task<string?> RemovePlanItemAsync(int planId, int packId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await dbContext.CommercialPackPlanItems.SingleOrDefaultAsync(
            x => x.CommercialPackPlanId == planId && x.CommercialPackDefinitionId == packId,
            cancellationToken);
        if (item is null) return "Pack-plan assignment was not found.";
        dbContext.CommercialPackPlanItems.Remove(item);
        var userId = await UserIdAsync(user, cancellationToken);
        await AuditAsync("RemovePack", nameof(CommercialPackPlan), planId, userId, JsonSerializer.Serialize(new { PackId = packId, item.Priority }), null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public Task<string?> DeactivatePlanAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        DeactivateAsync<CommercialPackPlan>(id, user, cancellationToken);

    public Task<string?> DeactivatePackAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        DeactivateAsync<CommercialPackDefinition>(id, user, cancellationToken);

    private async Task<string?> DeactivateAsync<TEntity>(int id, ClaimsPrincipal user, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await dbContext.Set<TEntity>().FindAsync([id], cancellationToken);
        if (entity is null) return "Configuration record was not found.";
        var userId = await UserIdAsync(user, cancellationToken);
        if (entity is CommercialPackPlan plan)
        {
            plan.IsActive = false;
            plan.UpdatedAt = businessTime.UtcNow;
            plan.UpdatedByUserId = userId;
        }
        else if (entity is CommercialPackDefinition pack)
        {
            pack.IsActive = false;
            pack.UpdatedAt = businessTime.UtcNow;
            pack.UpdatedByUserId = userId;
        }
        await AuditAsync("Deactivate", typeof(TEntity).Name, id, userId, null, JsonSerializer.Serialize(new { IsActive = false }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<int?> UserIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task AuditAsync(string action, string entityName, int entityId, int? userId, string? before, string? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityId.ToString(),
            UserId = userId,
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "CropQc.Web",
            CreatedAt = businessTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CommercialPackDefinitionForm EmptyPackForm() =>
        new() { EligibleSizes = Enumerable.Range(0, 4).Select(_ => new CommercialPackEligibleSizeForm()).ToList() };

    private static string NormalizeCode(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static bool InvalidCropYearRange(int? start, int? end) =>
        start is < 2000 or > 2200 || end is < 2000 or > 2200 || start is not null && end is not null && start > end;
    private static bool InvalidPercentageRange(decimal? min, decimal? max) =>
        min is < 0 or > 100 || max is < 0 or > 100 || min is not null && max is not null && min > max;
    private static object PlanAudit(CommercialPackPlan x) => new { x.Code, x.DisplayName, x.Commodity, x.PlanType, x.EffectiveCropYearStart, x.EffectiveCropYearEnd, x.IsActive };
    private static object PackAudit(CommercialPackDefinition x) => new
    {
        x.Code,
        x.DisplayName,
        x.Commodity,
        x.PackType,
        x.PackageWeightPounds,
        x.AllowsMixedSizes,
        x.MixRule,
        x.EffectiveCropYearStart,
        x.EffectiveCropYearEnd,
        x.IsActive,
        EligibleSizes = x.EligibleSizes.Select(y => new { y.SizeCategory, y.Priority, y.TargetPercent, y.MinimumPercent, y.MaximumPercent }),
        FruitProfileIds = x.FruitProfileRestrictions.Select(y => y.FruitProfileId)
    };
}
