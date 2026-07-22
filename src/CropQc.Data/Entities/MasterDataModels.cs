namespace CropQc.Data.Entities;

public sealed class Warehouse
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Room> Rooms { get; } = new List<Room>();
    public ICollection<Receipt> Receipts { get; } = new List<Receipt>();
}

public sealed class Room
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? SubLocation { get; set; }
    public string? CropQcRoomName { get; set; }
    public string? CompuTechRoomCode { get; set; }
    public string? DisplayName { get; set; }
    public int SortOrder { get; set; }
    public int CapacityBins { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class GrowerLot
{
    public int Id { get; set; }
    public required string Grower { get; set; }
    public required string LotNumber { get; set; }
    public string? PoolStart { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanonicalGrower
{
    public int Id { get; set; }
    public required string DisplayName { get; set; }
    public string NormalizedKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int? MergedIntoCanonicalGrowerId { get; set; }
    public CanonicalGrower? MergedIntoCanonicalGrower { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<CanonicalGrowerAlias> Aliases { get; } = new List<CanonicalGrowerAlias>();
    public ICollection<CanonicalGrowerNumber> GrowerNumbers { get; } = new List<CanonicalGrowerNumber>();
}

public sealed class CanonicalGrowerAlias
{
    public int Id { get; set; }
    public int CanonicalGrowerId { get; set; }
    public CanonicalGrower CanonicalGrower { get; set; } = null!;
    public required string AliasName { get; set; }
    public string NormalizedAliasKey { get; set; } = "";
    public string? SourceSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanonicalGrowerNumber
{
    public int Id { get; set; }
    public int CanonicalGrowerId { get; set; }
    public CanonicalGrower CanonicalGrower { get; set; } = null!;
    public required string GrowerNumber { get; set; }
    public string NormalizedGrowerNumber { get; set; } = "";
    public string? SourceSystem { get; set; }
    public string? Facility { get; set; }
    public int? CropYear { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanonicalOrchardBlock
{
    public int Id { get; set; }
    public int CanonicalOrchardId { get; set; }
    public CanonicalOrchard CanonicalOrchard { get; set; } = null!;
    public int? CanonicalGrowerId { get; set; }
    public CanonicalGrower? CanonicalGrower { get; set; }
    public required string OrchardName { get; set; }
    public required string CanonicalBlockName { get; set; }
    public string NormalizedOrchardKey { get; set; } = "";
    public string NormalizedBlockKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<OrchardBlockAlias> Aliases { get; } = new List<OrchardBlockAlias>();
    public ICollection<QcSample> FieldSamples { get; } = new List<QcSample>();
}

public sealed class CanonicalOrchard
{
    public int Id { get; set; }
    public required string OrchardName { get; set; }
    public string NormalizedOrchardKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<CanonicalOrchardBlock> Blocks { get; } = new List<CanonicalOrchardBlock>();
    public ICollection<OrchardReportRecipient> ReportRecipients { get; } = new List<OrchardReportRecipient>();
}

public sealed class OrchardReportRecipient
{
    public int Id { get; set; }
    public int CanonicalOrchardId { get; set; }
    public CanonicalOrchard CanonicalOrchard { get; set; } = null!;
    public required string EmailAddress { get; set; }
    public string NormalizedEmailAddress { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }
}

public sealed class OrchardBlockAlias
{
    public int Id { get; set; }
    public int CanonicalOrchardBlockId { get; set; }
    public CanonicalOrchardBlock CanonicalOrchardBlock { get; set; } = null!;
    public required string AliasName { get; set; }
    public string NormalizedAliasKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FruitProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string VarietyCode { get; set; }
    public required string FruitType { get; set; }
    public required string ProductionType { get; set; }
    public bool IsOrganic { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Receipt> Receipts { get; } = new List<Receipt>();
}

public sealed class VarietyColorConfiguration
{
    public int Id { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public required string VarietyKey { get; set; }
    public required string VarietyName { get; set; }
    public required string HexColor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class SampleType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<QcSample> Samples { get; } = new List<QcSample>();
}

public sealed class Grade
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class DefectType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StarchScale
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? FruitType { get; set; }
    public int? FruitProfileId { get; set; }
    public FruitProfile? FruitProfile { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<StarchScaleValue> Values { get; } = new List<StarchScaleValue>();
}

public sealed class StarchScaleValue
{
    public int Id { get; set; }
    public int StarchScaleId { get; set; }
    public StarchScale StarchScale { get; set; } = null!;
    public decimal Value { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FruitSizeConversionThreshold
{
    public int Id { get; set; }
    public required string FruitType { get; set; }
    public int SizeCategory { get; set; }
    public decimal MinimumWeightGrams { get; set; }
    public bool IsActive { get; set; } = true;
}
