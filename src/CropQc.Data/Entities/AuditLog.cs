namespace CropQc.Data.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public required string EntityKey { get; set; }
    public string? BeforeValuesJson { get; set; }
    public string? AfterValuesJson { get; set; }
    public string? SourceApplication { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
