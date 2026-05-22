namespace CropQc.QcStation.Fta;

public sealed record FruitPressureCaptureRow(
    int FruitNumber,
    decimal? Pressure1Lbs,
    decimal? Pressure2Lbs,
    decimal? AveragePressureLbs,
    string Status);
