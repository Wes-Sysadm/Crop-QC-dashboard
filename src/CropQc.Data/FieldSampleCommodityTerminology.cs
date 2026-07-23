namespace CropQc.Data;

public sealed record FieldSampleCommodityTerminology(
    string Commodity,
    string WholeSampleLabel,
    string CutFruitLabel,
    string CameraLabel);

public static class FieldSampleCommodityTerminologyService
{
    public static FieldSampleCommodityTerminology ForFruitType(string? fruitType) =>
        fruitType?.Trim().ToUpperInvariant() switch
        {
            "APPLE" => new("Apple", "Whole Apple Sample", "Cut Apple", "Apple camera"),
            "PEAR" => new("Pear", "Whole Pear Sample", "Cut Pear", "Pear camera"),
            _ => new("Fruit", "Whole Fruit Sample", "Cut Fruit", "Fruit camera")
        };
}
