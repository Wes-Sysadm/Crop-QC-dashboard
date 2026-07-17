using CropQc.Data.Entities;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public sealed record QcPhotoRequirement(string Key, string FriendlyName, string PhotoType, bool ReceiptLevel, bool IsRequired = true);

public interface IQcPhotoRequirementPolicy
{
    IReadOnlyList<QcPhotoRequirement> GetRequirements(string? sampleTypeName);
    IReadOnlyList<QcPhotoRequirement> GetAvailablePhotoTypes(string? sampleTypeName);
    IReadOnlyList<ReadinessChecklistItem> BuildChecklist(string? sampleTypeName, IReadOnlyCollection<string> receiptPhotoTypes, IReadOnlyCollection<string> samplePhotoTypes);
    IReadOnlyList<string> MissingRequiredPhotos(string? sampleTypeName, IReadOnlyCollection<string> receiptPhotoTypes, IReadOnlyCollection<string> samplePhotoTypes);
}

public sealed class QcPhotoRequirementPolicy : IQcPhotoRequirementPolicy
{
    public static readonly QcPhotoRequirement TruckPhoto = new("TruckPhoto", "Truck photo", "BinTruck", ReceiptLevel: true);
    public static readonly QcPhotoRequirement TopOfTruck = new("TopOfTruck", "Top of truck", "TopOfTruck", ReceiptLevel: true);
    public static readonly QcPhotoRequirement Hectre = new("Hectre", "Hectre", "Hectre", ReceiptLevel: false);
    public static readonly QcPhotoRequirement WholeSample = new("WholeSample", "Whole sample", "SampleBeforeCutting", ReceiptLevel: false);
    public static readonly QcPhotoRequirement CutApples = new("CutApples", "Cut apples", "CutFruit", ReceiptLevel: false);
    public static readonly QcPhotoRequirement StarchApples = new("StarchApples", "Starch apples", "FruitAfterStarch", ReceiptLevel: false);
    private static readonly IReadOnlyList<QcPhotoRequirement> FullPhotoOrder = [TruckPhoto, TopOfTruck, Hectre, WholeSample, CutApples, StarchApples];

    public IReadOnlyList<QcPhotoRequirement> GetRequirements(string? sampleTypeName)
    {
        var normalized = sampleTypeName ?? string.Empty;
        if (normalized.Contains("field", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (IsTruckSampleType(normalized))
        {
            return [TruckPhoto, TopOfTruck, Hectre, WholeSample, CutApples, StarchApples];
        }

        if (normalized.Contains("transfer", StringComparison.OrdinalIgnoreCase))
        {
            return [TruckPhoto, TopOfTruck, WholeSample, CutApples];
        }

        if (normalized.Contains("door", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("room", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("line", StringComparison.OrdinalIgnoreCase))
        {
            return [WholeSample, CutApples];
        }

        return [WholeSample, CutApples];
    }

    public IReadOnlyList<QcPhotoRequirement> GetAvailablePhotoTypes(string? sampleTypeName)
    {
        var normalized = sampleTypeName ?? string.Empty;
        if (normalized.Contains("field", StringComparison.OrdinalIgnoreCase))
        {
            return [WholeSample with { IsRequired = false }, CutApples with { IsRequired = false }, StarchApples with { IsRequired = false }];
        }

        if (IsTruckSampleType(normalized)
            || normalized.Contains("transfer", StringComparison.OrdinalIgnoreCase))
        {
            return MarkOptional(GetRequirements(sampleTypeName), FullPhotoOrder);
        }

        if (normalized.Contains("door", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("room", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("lot", StringComparison.OrdinalIgnoreCase))
        {
            return MarkOptional(GetRequirements(sampleTypeName), [WholeSample, CutApples, StarchApples]);
        }

        if (normalized.Contains("line", StringComparison.OrdinalIgnoreCase))
        {
            return MarkOptional(GetRequirements(sampleTypeName), [Hectre, WholeSample, CutApples, StarchApples]);
        }

        return MarkOptional(GetRequirements(sampleTypeName), [WholeSample, CutApples, StarchApples]);
    }

    public IReadOnlyList<ReadinessChecklistItem> BuildChecklist(string? sampleTypeName, IReadOnlyCollection<string> receiptPhotoTypes, IReadOnlyCollection<string> samplePhotoTypes) =>
        GetRequirements(sampleTypeName)
            .Select(requirement =>
            {
                var present = HasPhoto(requirement, receiptPhotoTypes, samplePhotoTypes);
                return present
                    ? new ReadinessChecklistItem("Required photos", requirement.FriendlyName, "Complete", "ready")
                    : new ReadinessChecklistItem("Required photos", requirement.FriendlyName, "Missing", "missing");
            })
            .ToList();

    public IReadOnlyList<string> MissingRequiredPhotos(string? sampleTypeName, IReadOnlyCollection<string> receiptPhotoTypes, IReadOnlyCollection<string> samplePhotoTypes) =>
        GetRequirements(sampleTypeName)
            .Where(requirement => !HasPhoto(requirement, receiptPhotoTypes, samplePhotoTypes))
            .Select(requirement => $"Missing required photo: {requirement.FriendlyName}")
            .ToList();

    private static bool HasPhoto(QcPhotoRequirement requirement, IReadOnlyCollection<string> receiptPhotoTypes, IReadOnlyCollection<string> samplePhotoTypes)
    {
        var source = (requirement.ReceiptLevel ? receiptPhotoTypes : samplePhotoTypes).Select(NormalizePhotoType);
        return source.Contains(requirement.PhotoType, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizePhotoType(string? photoType)
    {
        var normalized = (photoType ?? "").Trim();
        return normalized switch
        {
            "TopTruck" or "TopTruckPhoto" or "TopOfTruckPhoto" => "TopOfTruck",
            _ when normalized.Equals("Top truck photo", StringComparison.OrdinalIgnoreCase) => "TopOfTruck",
            _ when normalized.Equals("Top of truck", StringComparison.OrdinalIgnoreCase) => "TopOfTruck",
            _ => normalized
        };
    }

    private static IReadOnlyList<QcPhotoRequirement> MarkOptional(IReadOnlyList<QcPhotoRequirement> required, IReadOnlyList<QcPhotoRequirement> available)
    {
        var requiredTypes = required.Select(x => x.PhotoType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return available
            .Select(x => x with { IsRequired = requiredTypes.Contains(x.PhotoType) })
            .ToList();
    }

    private static bool IsTruckSampleType(string sampleTypeName) =>
        sampleTypeName.Contains("receiving", StringComparison.OrdinalIgnoreCase)
        || sampleTypeName.Contains("truck", StringComparison.OrdinalIgnoreCase);
}
