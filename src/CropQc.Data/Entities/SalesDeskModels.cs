namespace CropQc.Data.Entities;

public sealed class SalesDesk
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<ActualRun> ActualRuns { get; } = new List<ActualRun>();
    public ICollection<ActualRunSalesDeskCorrection> PreviousCorrections { get; } = new List<ActualRunSalesDeskCorrection>();
    public ICollection<ActualRunSalesDeskCorrection> NewCorrections { get; } = new List<ActualRunSalesDeskCorrection>();
}

public sealed class ActualRunSalesDeskCorrection
{
    public long Id { get; set; }
    public long ActualRunId { get; set; }
    public ActualRun ActualRun { get; set; } = null!;
    public required string OperationKey { get; set; }
    public long ExpectedConcurrencyVersion { get; set; }
    public int? PreviousSalesDeskId { get; set; }
    public SalesDesk? PreviousSalesDesk { get; set; }
    public string? PreviousSalesDeskNameSnapshot { get; set; }
    public int NewSalesDeskId { get; set; }
    public SalesDesk NewSalesDesk { get; set; } = null!;
    public required string NewSalesDeskNameSnapshot { get; set; }
    public required string Reason { get; set; }
    public int CorrectedByUserId { get; set; }
    public User CorrectedByUser { get; set; } = null!;
    public DateTimeOffset CorrectedAt { get; set; }
}
