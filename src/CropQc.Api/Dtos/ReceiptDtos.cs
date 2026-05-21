namespace CropQc.Api.Dtos;

public sealed record CreateReceiptRequest(
    int CropYear,
    DateTimeOffset ReceivedAt,
    string CompuTechReceiptId,
    int WarehouseId,
    int RoomId,
    int FruitProfileId,
    string GrowerName,
    string LotCode,
    int BinCount);

public sealed record UpdateReceiptRequest(
    int CropYear,
    DateTimeOffset ReceivedAt,
    int WarehouseId,
    int RoomId,
    int FruitProfileId,
    string GrowerName,
    string LotCode,
    int BinCount,
    string Reason);

public sealed record ReceiptSearchRequest(
    int? CropYear,
    string? ReceiptId,
    string? Grower,
    string? Lot,
    int? WarehouseId,
    int? RoomId,
    int? FruitProfileId);

public sealed record ReceiptDto(
    long Id,
    int CropYear,
    DateTimeOffset ReceivedAt,
    string CompuTechReceiptId,
    int WarehouseId,
    int RoomId,
    int FruitProfileId,
    string GrowerName,
    string LotCode,
    int BinCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
