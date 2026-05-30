namespace CropQc.Data.Entities;

public sealed class Receipt
{
    public long Id { get; set; }
    public int CropYear { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string CompuTechReceiptId { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int FruitProfileId { get; set; }
    public FruitProfile FruitProfile { get; set; } = null!;
    public required string GrowerName { get; set; }
    public required string LotCode { get; set; }
    public int BinCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<QcSample> Samples { get; } = new List<QcSample>();
    public ICollection<QcPhoto> Photos { get; } = new List<QcPhoto>();
    public ICollection<QcSummaryEmailLog> SummaryEmailLogs { get; } = new List<QcSummaryEmailLog>();
}

public sealed class QcSample
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public int SampleTypeId { get; set; }
    public SampleType SampleType { get; set; } = null!;
    public int SampleSequenceNumber { get; set; } = 1;
    public required string Status { get; set; }
    public required string StarchStatus { get; set; }
    public required string PhotoStatus { get; set; }
    public required string EmailStatus { get; set; }
    public int? TakenByUserId { get; set; }
    public User? TakenByUser { get; set; }
    public int? QcStationId { get; set; }
    public QcStation? QcStation { get; set; }
    public int? ActualSampleSize { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset SampleTakenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<QcFruitReading> FruitReadings { get; } = new List<QcFruitReading>();
    public ICollection<QcPhoto> Photos { get; } = new List<QcPhoto>();

    public string GetDisplayReceiptId() =>
        SampleSequenceNumber <= 1
            ? Receipt.CompuTechReceiptId
            : $"{Receipt.CompuTechReceiptId}({SampleSequenceNumber})";
}

public sealed class QcFruitReading
{
    public long Id { get; set; }
    public long QcSampleId { get; set; }
    public QcSample QcSample { get; set; } = null!;
    public int RowNumber { get; set; }
    public decimal? Pressure1Lbs { get; set; }
    public string? Pressure1Source { get; set; }
    public decimal? Pressure2Lbs { get; set; }
    public string? Pressure2Source { get; set; }
    public decimal? WeightGrams { get; set; }
    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public int? StarchScaleValueId { get; set; }
    public StarchScaleValue? StarchScaleValue { get; set; }
    public int? SizeCategory { get; set; }
    public required string SizeStatus { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<QcFruitDefect> Defects { get; } = new List<QcFruitDefect>();
}

public sealed class QcFruitDefect
{
    public long Id { get; set; }
    public long QcFruitReadingId { get; set; }
    public QcFruitReading QcFruitReading { get; set; } = null!;
    public int DefectTypeId { get; set; }
    public DefectType DefectType { get; set; } = null!;
    public string? Notes { get; set; }
}

public sealed class QcPhoto
{
    public long Id { get; set; }
    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? QcSampleId { get; set; }
    public QcSample? QcSample { get; set; }
    public required string PhotoType { get; set; }
    public required string PhotoSource { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string StorageProvider { get; set; } = "Legacy";
    public string? DriveId { get; set; }
    public string? FileId { get; set; }
    public string? FolderId { get; set; }
    public required string SharePointDriveId { get; set; }
    public required string SharePointItemId { get; set; }
    public string? WebUrl { get; set; }
    public int? CapturedByUserId { get; set; }
    public User? CapturedByUser { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
}

public sealed class QcSummaryEmailLog
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public long? QcSampleId { get; set; }
    public QcSample? QcSample { get; set; }
    public required string FromAddress { get; set; }
    public required string ToAddress { get; set; }
    public string? ReplyToAddress { get; set; }
    public required string Subject { get; set; }
    public required string Status { get; set; }
    public string? MessageId { get; set; }
    public int? SentByUserId { get; set; }
    public User? SentByUser { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public bool IsResend { get; set; }
    public string? ResendReason { get; set; }
    public bool IsOverride { get; set; }
    public string? OverrideReason { get; set; }
    public string? MissingItemsSnapshot { get; set; }
    public string? EmailBodySnapshot { get; set; }
    public string? ReportSnapshotReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class QcStation
{
    public int Id { get; set; }
    public required string StationCode { get; set; }
    public required string Name { get; set; }
    public string StationName { get; set; } = "";
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public string? WarehouseCode { get; set; }
    public string? Description { get; set; }
    public string? DeviceIdentifier { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ApiKeyHash { get; set; }
    public string? ApiKeyLastFour { get; set; }
    public DateTimeOffset? ApiKeyCreatedAt { get; set; }
    public DateTimeOffset? ApiKeyRotatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? LastSeenIp { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public string? Notes { get; set; }
    public ICollection<QcSample> Samples { get; } = new List<QcSample>();
}

public sealed class OfflineSyncItem
{
    public long Id { get; set; }
    public int? QcStationId { get; set; }
    public QcStation? QcStation { get; set; }
    public required string EntityName { get; set; }
    public required string LocalEntityId { get; set; }
    public long? ServerEntityId { get; set; }
    public required string SyncStatus { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}
