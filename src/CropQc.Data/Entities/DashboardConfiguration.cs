namespace CropQc.Data.Entities;

public sealed class DashboardConfiguration
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required string Description { get; set; }
    public string ValueType { get; set; } = "String";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
