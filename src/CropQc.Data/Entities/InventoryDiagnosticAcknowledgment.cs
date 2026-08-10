namespace CropQc.Data.Entities;

public sealed class InventoryDiagnosticAcknowledgment
{
    public long Id { get; set; }
    public required string DiagnosticKey { get; set; }
    public required string DiagnosticType { get; set; }
    public required string DiagnosticCode { get; set; }
    public required string DiagnosticMessage { get; set; }
    public long RoomInventoryAdjustmentId { get; set; }
    public RoomInventoryAdjustment RoomInventoryAdjustment { get; set; } = null!;
    public int InvariantVersion { get; set; }
    public required string Reason { get; set; }
    public required string DiagnosticSnapshotJson { get; set; }
    public int? DismissedByUserId { get; set; }
    public User? DismissedByUser { get; set; }
    public required string DismissedByEmail { get; set; }
    public DateTimeOffset DismissedAt { get; set; }
    public bool IsActive { get; set; }
    public int? RestoredByUserId { get; set; }
    public User? RestoredByUser { get; set; }
    public string? RestoredByEmail { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }
}
