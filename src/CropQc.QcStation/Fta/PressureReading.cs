namespace CropQc.QcStation.Fta;

public sealed record PressureReading(
    Guid ReadingId,
    decimal ReadingValueLbs,
    PressureReadingSource Source,
    DateTimeOffset CapturedAt,
    string StationName,
    string Status,
    string? ErrorMessage,
    decimal? RawReadingValue = null,
    string? RawReadingUnit = null)
{
    public static PressureReading Success(decimal readingValueLbs, PressureReadingSource source, string stationName, decimal? rawReadingValue = null, string? rawReadingUnit = null) =>
        new(Guid.NewGuid(), readingValueLbs, source, DateTimeOffset.UtcNow, stationName, "Captured", null, rawReadingValue, rawReadingUnit);

    public static PressureReading Failed(PressureReadingSource source, string stationName, string errorMessage) =>
        new(Guid.NewGuid(), 0m, source, DateTimeOffset.UtcNow, stationName, "Error", errorMessage);
}
