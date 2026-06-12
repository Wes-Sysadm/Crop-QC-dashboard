namespace CropQc.QcStation.Api;

public sealed record QcStationSampleListItem(
    long SampleId,
    long ReceiptId,
    string DisplayReceiptId,
    string WarehouseCode,
    string RoomCode,
    string GrowerName,
    string LotCode,
    string VarietyCode,
    string Status,
    string StarchStatus,
    string EmailStatus,
    int CompletedPressureRows,
    DateTimeOffset SampleTakenAt);

public sealed record QcStationSampleDetail(
    long SampleId,
    long ReceiptId,
    string DisplayReceiptId,
    string OriginalReceiptId,
    string GrowerName,
    string LotCode,
    string VarietyName,
    string VarietyCode,
    string WarehouseCode,
    string RoomCode,
    string Status,
    string StarchStatus,
    string EmailStatus,
    DateTimeOffset SampleTakenAt,
    int TargetSampleSize,
    IReadOnlyList<QcStationFruitReading> FruitReadings);

public sealed record QcStationFruitReading(
    long Id,
    long QcSampleId,
    int RowNumber,
    decimal? Pressure1Lbs,
    string? Pressure1Source,
    decimal? Pressure2Lbs,
    string? Pressure2Source,
    decimal? PressureAverageLbs,
    decimal? WeightGrams,
    int? GradeId,
    string? Grade,
    int? StarchScaleValueId,
    string? Starch,
    int? SizeCategory,
    string SizeStatus,
    bool IsCompleted,
    IReadOnlyList<string> Defects);

public sealed record QcStationPressureRowUpdate(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs);

public sealed record QcStationPressureUpdateRequest(
    IReadOnlyList<QcStationPressureRowUpdate> Rows);
