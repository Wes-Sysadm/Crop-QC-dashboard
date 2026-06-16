using CropQc.Api.Dtos;

namespace CropQc.Api.Services;

public sealed record ReadinessFruitRow(bool IsCompleted, bool HasPressure1, bool HasPressure2, bool HasWeight, bool HasGrade, bool HasStarch);

public sealed record ReadinessEvaluationInput(
    bool ReceiptExists,
    string? SampleTypeName,
    IReadOnlyCollection<ReadinessFruitRow> FruitRows,
    bool HasBinTruckPhoto,
    bool HasTopOfTruckPhoto,
    bool HasHectrePhoto,
    bool HasSampleBeforeCuttingPhoto,
    bool HasCutFruitPhoto,
    bool HasFruitAfterStarchPhoto);

public static class ReadinessEvaluator
{
    public static QcSummaryReadinessDto Evaluate(ReadinessEvaluationInput input)
    {
        var missingItems = new List<string>();

        if (!input.ReceiptExists)
        {
            missingItems.Add("Receipt is missing.");
        }

        var completedRows = input.FruitRows.Where(x => x.IsCompleted).ToList();
        if (completedRows.Count == 0)
        {
            missingItems.Add("At least one completed fruit row is required.");
        }

        var invalidCompletedRows = completedRows.Count(x => !x.HasPressure1 || !x.HasPressure2 || !x.HasWeight || !x.HasGrade);
        if (invalidCompletedRows > 0)
        {
            missingItems.Add("All completed fruit rows require Pressure 1, Pressure 2, weight, and grade.");
        }

        var starchMissingCount = completedRows.Count(x => !x.HasStarch);
        if (IsStarchRequiredForEmail(input.SampleTypeName) && starchMissingCount > 0)
        {
            missingItems.Add("Starch is required for all completed fruit rows.");
        }

        var requiredPhotos = GetRequiredPhotos(input);
        foreach (var missingPhoto in requiredPhotos.Where(x => !x.IsPresent))
        {
            missingItems.Add($"Missing required photo: {missingPhoto.Name}");
        }

        var photoStatus = new PhotoStatusDetails(
            input.HasBinTruckPhoto,
            input.HasTopOfTruckPhoto,
            input.HasHectrePhoto,
            input.HasSampleBeforeCuttingPhoto,
            input.HasCutFruitPhoto,
            input.HasFruitAfterStarchPhoto);

        return new QcSummaryReadinessDto(
            missingItems.Count == 0,
            missingItems,
            completedRows.Count,
            starchMissingCount,
            photoStatus);
    }

    private static IReadOnlyList<(string Name, bool IsPresent)> GetRequiredPhotos(ReadinessEvaluationInput input)
    {
        var normalized = input.SampleTypeName ?? string.Empty;
        if (normalized.Contains("receiving", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Truck photo", input.HasBinTruckPhoto),
                ("Top of truck", input.HasTopOfTruckPhoto),
                ("Hectre", input.HasHectrePhoto),
                ("Whole sample", input.HasSampleBeforeCuttingPhoto),
                ("Cut apples", input.HasCutFruitPhoto),
                ("Starch apples", input.HasFruitAfterStarchPhoto)
            ];
        }

        if (normalized.Contains("transfer", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Truck photo", input.HasBinTruckPhoto),
                ("Top of truck", input.HasTopOfTruckPhoto),
                ("Whole sample", input.HasSampleBeforeCuttingPhoto),
                ("Cut apples", input.HasCutFruitPhoto)
            ];
        }

        return
        [
            ("Whole sample", input.HasSampleBeforeCuttingPhoto),
            ("Cut apples", input.HasCutFruitPhoto)
        ];
    }

    private static bool IsStarchRequiredForEmail(string? sampleTypeName)
    {
        var normalized = sampleTypeName ?? string.Empty;
        return normalized.Contains("receiving", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("truck", StringComparison.OrdinalIgnoreCase);
    }
}
