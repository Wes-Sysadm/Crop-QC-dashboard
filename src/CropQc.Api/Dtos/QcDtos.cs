namespace CropQc.Api.Dtos;

public sealed record CreateQcSampleRequest(
    int SampleTypeId,
    int? TakenByUserId,
    int? QcStationId,
    int? ActualSampleSize,
    DateTimeOffset? SampleTakenAt,
    string? Notes);

public sealed record UpdateQcSampleStatusesRequest(
    string Status,
    string StarchStatus,
    string PhotoStatus,
    string EmailStatus,
    string? Notes);

public sealed record QcSampleDto(
    long Id,
    long ReceiptId,
    int SampleTypeId,
    int SampleSequenceNumber,
    string DisplayReceiptId,
    string Status,
    string StarchStatus,
    string PhotoStatus,
    string EmailStatus,
    int? TakenByUserId,
    int? QcStationId,
    int? ActualSampleSize,
    string? Notes,
    DateTimeOffset SampleTakenAt);

public sealed record UpsertQcFruitReadingRequest(
    decimal? Pressure1Lbs,
    string? Pressure1Source,
    decimal? Pressure2Lbs,
    string? Pressure2Source,
    decimal? WeightGrams,
    int? GradeId,
    int? StarchScaleValueId,
    bool IsCompleted);

public sealed record QcFruitReadingDto(
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
    int? StarchScaleValueId,
    int? SizeCategory,
    string SizeStatus,
    bool IsCompleted);

public sealed record QcStationSampleListItemDto(
    long SampleId,
    long? ReceiptId,
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

public sealed record QcStationFruitReadingDto(
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

public sealed record QcStationSampleDetailDto(
    long SampleId,
    long? ReceiptId,
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
    IReadOnlyList<QcStationFruitReadingDto> FruitReadings);

public sealed record UpdateQcStationPressureRowRequest(
    int RowNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs);

public sealed record UpdateQcStationPressuresRequest(
    IReadOnlyList<UpdateQcStationPressureRowRequest>? Rows);

public sealed record CreateQcFruitDefectRequest(int DefectTypeId, string? Notes);
public sealed record QcFruitDefectDto(long Id, long QcFruitReadingId, int DefectTypeId, string? Notes);

public sealed record CreateQcPhotoRequest(
    long? ReceiptId,
    long? QcSampleId,
    string PhotoType,
    string PhotoSource,
    string FileName,
    string ContentType,
    long? FileSizeBytes,
    string SharePointDriveId,
    string SharePointItemId,
    string? WebUrl,
    int? CapturedByUserId,
    DateTimeOffset? CapturedAt);

public sealed record QcPhotoDto(
    long Id,
    long? ReceiptId,
    long? QcSampleId,
    string PhotoType,
    string PhotoSource,
    string FileName,
    string ContentType,
    long? FileSizeBytes,
    string SharePointDriveId,
    string SharePointItemId,
    string? WebUrl,
    int? CapturedByUserId,
    DateTimeOffset CapturedAt);

public sealed record PhotoStatusDetails(bool HasBinTruck, bool HasTopOfTruck, bool HasHectre, bool HasSampleBeforeCutting, bool HasCutFruit, bool HasFruitAfterStarch);
public sealed record QcSummaryReadinessDto(bool IsReady, IReadOnlyList<string> MissingItems, int CompletedFruitCount, int StarchMissingCount, PhotoStatusDetails PhotoStatus);

public sealed record CreateEmailLogRequest(
    long? QcSampleId,
    string? FromAddress,
    string? ToAddress,
    string? ReplyToAddress,
    string Subject,
    string Status,
    string? MessageId,
    int? SentByUserId,
    DateTimeOffset? SentAt,
    bool IsResend,
    string? ResendReason,
    bool IsOverride,
    string? OverrideReason,
    string? MissingItemsSnapshot,
    string? EmailBodySnapshot,
    string? ReportSnapshotReference);

public sealed record QcSummaryEmailLogDto(
    long Id,
    long? ReceiptId,
    long? QcSampleId,
    string FromAddress,
    string ToAddress,
    string? ReplyToAddress,
    string Subject,
    string Status,
    string? MessageId,
    int? SentByUserId,
    DateTimeOffset? SentAt,
    bool IsResend,
    string? ResendReason,
    bool IsOverride,
    string? OverrideReason,
    string? MissingItemsSnapshot,
    string? EmailBodySnapshot,
    string? ReportSnapshotReference,
    DateTimeOffset CreatedAt);
