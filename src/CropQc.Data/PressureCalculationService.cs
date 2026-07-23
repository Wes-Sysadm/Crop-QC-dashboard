namespace CropQc.Data;

public static class PressureCalculationService
{
    public static IReadOnlyList<decimal> ValidSideReadings(
        IEnumerable<(decimal? Pressure1Lbs, decimal? Pressure2Lbs)> rows) =>
        rows.SelectMany(row => new[] { row.Pressure1Lbs, row.Pressure2Lbs })
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();

    public static decimal? CalculateOverallAverage(
        IEnumerable<(decimal? Pressure1Lbs, decimal? Pressure2Lbs)> rows)
    {
        var readings = ValidSideReadings(rows);
        return readings.Count == 0 ? null : decimal.Round(readings.Average(), 2);
    }
}
