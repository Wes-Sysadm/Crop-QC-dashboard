namespace CropQc.Web.Models;

public sealed class CommercialPackAdminPageViewModel
{
    public IReadOnlyList<CommercialPackPlanAdminRow> Plans { get; set; } = [];
    public IReadOnlyList<CommercialPackDefinitionAdminRow> Packs { get; set; } = [];
    public IReadOnlyList<CommercialPackPlanItemAdminRow> PlanItems { get; set; } = [];
    public IReadOnlyList<CommercialPackFruitProfileOption> FruitProfiles { get; set; } = [];
    public CommercialPackPlanForm PlanForm { get; set; } = new();
    public CommercialPackDefinitionForm PackForm { get; set; } = new();
    public CommercialPackPlanItemForm PlanItemForm { get; set; } = new();
}

public sealed record CommercialPackPlanAdminRow(
    int Id,
    string Code,
    string DisplayName,
    string Commodity,
    string PlanType,
    int? EffectiveCropYearStart,
    int? EffectiveCropYearEnd,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record CommercialPackDefinitionAdminRow(
    int Id,
    string Code,
    string DisplayName,
    string Commodity,
    string PackType,
    decimal PackageWeightPounds,
    bool AllowsMixedSizes,
    string MixRule,
    string EligibleSizes,
    string FruitProfiles,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record CommercialPackPlanItemAdminRow(
    int PlanId,
    string Plan,
    int PackId,
    string Pack,
    int Priority);

public sealed record CommercialPackFruitProfileOption(int Id, string Label, string Commodity);

public sealed class CommercialPackPlanForm
{
    public int? Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Commodity { get; set; } = "";
    public string PlanType { get; set; } = "";
    public int? EffectiveCropYearStart { get; set; }
    public int? EffectiveCropYearEnd { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CommercialPackDefinitionForm
{
    public int? Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Commodity { get; set; } = "";
    public string PackType { get; set; } = "";
    public decimal PackageWeightPounds { get; set; } = 40m;
    public bool AllowsMixedSizes { get; set; }
    public string MixRule { get; set; } = "";
    public int? EffectiveCropYearStart { get; set; }
    public int? EffectiveCropYearEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CommercialPackEligibleSizeForm> EligibleSizes { get; set; } = [];
    public List<int> FruitProfileIds { get; set; } = [];
}

public sealed class CommercialPackEligibleSizeForm
{
    public int? SizeCategory { get; set; }
    public int Priority { get; set; }
    public decimal? TargetPercent { get; set; }
    public decimal? MinimumPercent { get; set; }
    public decimal? MaximumPercent { get; set; }
}

public sealed class CommercialPackPlanItemForm
{
    public int CommercialPackPlanId { get; set; }
    public int CommercialPackDefinitionId { get; set; }
    public int Priority { get; set; }
}
