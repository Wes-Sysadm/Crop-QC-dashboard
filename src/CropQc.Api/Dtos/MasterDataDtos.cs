namespace CropQc.Api.Dtos;

public sealed record WarehouseDto(int Id, string Code, string Name, bool IsActive);
public sealed record RoomDto(int Id, int WarehouseId, string Code, string Name, int CapacityBins, bool IsActive);
public sealed record FruitProfileDto(int Id, string Name, string? Description, string VarietyCode, string FruitType, string ProductionType, bool IsOrganic, bool IsActive);
public sealed record LookupDto(int Id, string Name, bool IsActive);
public sealed record GradeDto(int Id, string Code, string Name, bool IsActive);
public sealed record StarchScaleValueDto(int Id, int StarchScaleId, decimal Value, int SortOrder, bool IsActive);
public sealed record FruitSizeThresholdDto(int Id, string FruitType, int SizeCategory, decimal MinimumWeightGrams, bool IsActive);
