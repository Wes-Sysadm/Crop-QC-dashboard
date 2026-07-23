namespace CropQc.Data.Entities;

public sealed class FieldSampleDeletionAudit
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public long DeletedFieldSampleId { get; set; }
    public required string IdentifyingFieldsJson { get; set; }
    public required string DependencyCountsJson { get; set; }
    public required string DeletedByEmail { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public required string DeletedAtPacific { get; set; }
    public required string Reason { get; set; }
    public long BackupRunId { get; set; }
    public required string Result { get; set; }
}
