using CropQc.Api.Dtos;

namespace CropQc.Api.Services;

public sealed record ReadinessFruitRow(bool IsCompleted, bool HasPressure1, bool HasPressure2, bool HasWeight, bool HasGrade, bool HasStarch);

public sealed record ReadinessEvaluationInput(
    bool ReceiptExists,
    IReadOnlyCollection<ReadinessFruitRow> FruitRows,
    bool HasBinTruckPhoto,
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
        if (starchMissingCount > 0)
        {
            missingItems.Add("Starch is required for all completed fruit rows.");
        }

        if (!input.HasBinTruckPhoto)
        {
            missingItems.Add("At least one bin/truck photo is required on the receipt.");
        }

        if (!input.HasSampleBeforeCuttingPhoto)
        {
            missingItems.Add("Sample before cutting photo is required on the sample.");
        }

        if (!input.HasCutFruitPhoto)
        {
            missingItems.Add("Cut fruit photo is required on the sample.");
        }

        if (!input.HasFruitAfterStarchPhoto)
        {
            missingItems.Add("Fruit after starch photo is required on the sample.");
        }

        var photoStatus = new PhotoStatusDetails(
            input.HasBinTruckPhoto,
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
}
