namespace CropQc.Data.Entities;

// Durable intent/result receipt, not another physical inventory ledger.
public sealed class InventoryCommandRecord
{
    public required string OperationKey { get; set; }
    public required string IntentHash { get; set; }
    public required string IntentJson { get; set; }
    public required string ResultJson { get; set; }
    public string? ReversesOperationKey { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset CommittedAt { get; set; }
}
