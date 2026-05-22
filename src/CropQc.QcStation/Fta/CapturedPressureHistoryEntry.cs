namespace CropQc.QcStation.Fta;

public sealed record CapturedPressureHistoryEntry(
    DateTimeOffset CapturedAt,
    decimal PressureValueLbs,
    PressureReadingSource Source,
    int FruitNumber,
    string TargetSlot);
