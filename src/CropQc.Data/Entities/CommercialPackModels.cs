namespace CropQc.Data.Entities;

public static class CommercialPackPlanTypes
{
    public const string Standard = "Standard";
    public const string Euro = "Euro";
    public const string Mixed = "Mixed";
    public const string CustomerProgram = "CustomerProgram";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Standard, Euro, Mixed, CustomerProgram };
}

public static class CommercialPackMixRules
{
    public const string SingleSize = "SingleSize";
    public const string AnyMixture = "AnyMixture";
    public const string FixedPercentage = "FixedPercentage";
    public const string PrimaryThenSupplement = "PrimaryThenSupplement";
    public const string OptimizeUse = "OptimizeUse";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SingleSize,
            AnyMixture,
            FixedPercentage,
            PrimaryThenSupplement,
            OptimizeUse
        };
}

public static class CommercialPackTypes
{
    public const string Standard = "Standard";
    public const string Euro = "Euro";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Standard, Euro, Other };
}

public sealed class CommercialPackPlan
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public required string Commodity { get; set; }
    public required string PlanType { get; set; }
    public bool IsActive { get; set; } = true;
    public int? EffectiveCropYearStart { get; set; }
    public int? EffectiveCropYearEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<CommercialPackPlanItem> Items { get; } = new List<CommercialPackPlanItem>();
}

public sealed class CommercialPackDefinition
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public required string Commodity { get; set; }
    public required string PackType { get; set; }
    public decimal PackageWeightPounds { get; set; }
    public bool AllowsMixedSizes { get; set; }
    public required string MixRule { get; set; }
    public bool IsActive { get; set; } = true;
    public int? EffectiveCropYearStart { get; set; }
    public int? EffectiveCropYearEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<CommercialPackEligibleSize> EligibleSizes { get; } = new List<CommercialPackEligibleSize>();
    public ICollection<CommercialPackFruitProfileRestriction> FruitProfileRestrictions { get; } = new List<CommercialPackFruitProfileRestriction>();
    public ICollection<CommercialPackPlanItem> PlanItems { get; } = new List<CommercialPackPlanItem>();
}

public sealed class CommercialPackEligibleSize
{
    public int Id { get; set; }
    public int CommercialPackDefinitionId { get; set; }
    public CommercialPackDefinition CommercialPackDefinition { get; set; } = null!;
    public int SizeCategory { get; set; }
    public int Priority { get; set; }
    public decimal? TargetPercent { get; set; }
    public decimal? MinimumPercent { get; set; }
    public decimal? MaximumPercent { get; set; }
}

public sealed class CommercialPackFruitProfileRestriction
{
    public int CommercialPackDefinitionId { get; set; }
    public CommercialPackDefinition CommercialPackDefinition { get; set; } = null!;
    public int FruitProfileId { get; set; }
    public FruitProfile FruitProfile { get; set; } = null!;
}

public sealed class CommercialPackPlanItem
{
    public int CommercialPackPlanId { get; set; }
    public CommercialPackPlan CommercialPackPlan { get; set; } = null!;
    public int CommercialPackDefinitionId { get; set; }
    public CommercialPackDefinition CommercialPackDefinition { get; set; } = null!;
    public int Priority { get; set; }
}
