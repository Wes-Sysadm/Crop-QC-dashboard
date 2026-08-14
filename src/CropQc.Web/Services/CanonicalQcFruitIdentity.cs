using CropQc.Data.Entities;

namespace CropQc.Web.Services;

/// <summary>
/// Identifies fruit for QC evidence resolution. Location is deliberately excluded: QC follows
/// the fruit while rooms and warehouses describe its current and historical location.
/// </summary>
internal sealed record CanonicalQcFruitIdentity(
    int CropYear,
    int? GrowerLotId,
    string GrowerNumber,
    string LotNumber,
    int? FruitProfileId,
    string VarietyCode,
    string ProductionType,
    bool? IsOrganic)
{
    public string LookupKey { get; } = string.Join('|',
        CropYear,
        GrowerLotId is int growerLotId ? $"GL:{growerLotId}" : $"LEGACY:{GrowerNumber}:{LotNumber}",
        FruitProfileId is int fruitProfileId ? $"FP:{fruitProfileId}" : $"LEGACY:{VarietyCode}:{ProductionType}:{OrganicToken(IsOrganic)}");

    public static CanonicalQcFruitIdentity? Create(
        int? cropYear,
        int? growerLotId,
        string? growerNumber,
        string? lotNumber,
        int? fruitProfileId,
        string? varietyCode,
        string? productionType,
        bool? isOrganic)
    {
        if (cropYear is null)
        {
            return null;
        }

        var normalizedGrower = Normalize(growerNumber);
        var normalizedLot = Normalize(lotNumber);
        var normalizedVariety = Normalize(varietyCode);
        var normalizedProduction = Normalize(productionType);
        var hasLotIdentity = growerLotId is not null
            || normalizedGrower.Length > 0 && normalizedLot.Length > 0;
        var hasProfileIdentity = fruitProfileId is not null
            || normalizedVariety.Length > 0 && normalizedProduction.Length > 0 && isOrganic is not null;
        if (!hasLotIdentity || !hasProfileIdentity)
        {
            return null;
        }

        return new CanonicalQcFruitIdentity(
            cropYear.Value,
            growerLotId,
            normalizedGrower,
            normalizedLot,
            fruitProfileId,
            normalizedVariety,
            normalizedProduction,
            isOrganic);
    }

    public static CanonicalQcFruitIdentity? FromReceipt(Receipt receipt) =>
        Create(
            receipt.CropYear,
            receipt.GrowerLotId,
            receipt.GrowerNumber ?? receipt.LotCode,
            receipt.LotCode,
            receipt.FruitProfileId,
            receipt.FruitProfile.VarietyCode,
            receipt.FruitProfile.ProductionType,
            receipt.FruitProfile.IsOrganic);

    public bool Matches(CanonicalQcFruitIdentity candidate)
    {
        if (CropYear != candidate.CropYear)
        {
            return false;
        }

        var lotMatches = GrowerLotId is int growerLotId && candidate.GrowerLotId is int candidateGrowerLotId
            ? growerLotId == candidateGrowerLotId
            : GrowerNumber.Length > 0
                && LotNumber.Length > 0
                && GrowerNumber == candidate.GrowerNumber
                && LotNumber == candidate.LotNumber;
        if (!lotMatches)
        {
            return false;
        }

        return FruitProfileId is int fruitProfileId && candidate.FruitProfileId is int candidateFruitProfileId
            ? fruitProfileId == candidateFruitProfileId
            : VarietyCode.Length > 0
                && ProductionType.Length > 0
                && IsOrganic is not null
                && VarietyCode == candidate.VarietyCode
                && ProductionType == candidate.ProductionType
                && IsOrganic == candidate.IsOrganic;
    }

    public static IReadOnlyList<T> ResolveUnambiguous<T>(
        CanonicalQcFruitIdentity target,
        IEnumerable<T> candidates,
        Func<T, CanonicalQcFruitIdentity?> identitySelector)
    {
        var matches = new List<T>();
        int? matchedGrowerLotId = null;
        int? matchedFruitProfileId = null;
        var growerLotAmbiguous = false;
        var fruitProfileAmbiguous = false;
        foreach (var candidate in candidates)
        {
            var identity = identitySelector(candidate);
            if (identity is null || !target.Matches(identity))
            {
                continue;
            }

            matches.Add(candidate);
            if (target.GrowerLotId is null && identity.GrowerLotId is int growerLotId)
            {
                growerLotAmbiguous |= matchedGrowerLotId is not null && matchedGrowerLotId != growerLotId;
                matchedGrowerLotId ??= growerLotId;
            }
            if (target.FruitProfileId is null && identity.FruitProfileId is int fruitProfileId)
            {
                fruitProfileAmbiguous |= matchedFruitProfileId is not null && matchedFruitProfileId != fruitProfileId;
                matchedFruitProfileId ??= fruitProfileId;
            }
        }

        if (matches.Count == 0)
        {
            return [];
        }

        if (growerLotAmbiguous)
        {
            return [];
        }

        if (fruitProfileAmbiguous)
        {
            return [];
        }

        return matches;
    }

    public static T? ResolveLatestUnambiguous<T>(
        CanonicalQcFruitIdentity target,
        IEnumerable<T> candidates,
        Func<T, CanonicalQcFruitIdentity?> identitySelector,
        Func<T, DateTimeOffset> sampleTakenAtSelector,
        Func<T, long> idSelector)
        where T : class
    {
        T? latest = null;
        int? matchedGrowerLotId = null;
        int? matchedFruitProfileId = null;
        var growerLotAmbiguous = false;
        var fruitProfileAmbiguous = false;
        foreach (var candidate in candidates)
        {
            var identity = identitySelector(candidate);
            if (identity is null || !target.Matches(identity))
            {
                continue;
            }

            if (target.GrowerLotId is null && identity.GrowerLotId is int growerLotId)
            {
                growerLotAmbiguous |= matchedGrowerLotId is not null && matchedGrowerLotId != growerLotId;
                matchedGrowerLotId ??= growerLotId;
            }
            if (target.FruitProfileId is null && identity.FruitProfileId is int fruitProfileId)
            {
                fruitProfileAmbiguous |= matchedFruitProfileId is not null && matchedFruitProfileId != fruitProfileId;
                matchedFruitProfileId ??= fruitProfileId;
            }

            if (latest is null
                || sampleTakenAtSelector(candidate) > sampleTakenAtSelector(latest)
                || sampleTakenAtSelector(candidate) == sampleTakenAtSelector(latest)
                    && idSelector(candidate) > idSelector(latest))
            {
                latest = candidate;
            }
        }

        return growerLotAmbiguous || fruitProfileAmbiguous ? null : latest;
    }

    public static IQueryable<QcSample> FilterReceiptSamples(
        IQueryable<QcSample> query,
        IReadOnlyCollection<CanonicalQcFruitIdentity> targets,
        DateTimeOffset? latestTakenAt = null)
    {
        if (targets.Count == 0)
        {
            return query.Where(_ => false);
        }

        var cropYears = targets.Select(x => x.CropYear).Distinct().ToList();
        var growerLotIds = targets.Where(x => x.GrowerLotId is not null).Select(x => x.GrowerLotId!.Value).Distinct().ToList();
        var growerNumbers = targets.Where(x => x.GrowerLotId is null).Select(x => x.GrowerNumber).Where(x => x.Length > 0).Distinct().ToList();
        var lotNumbers = targets.Where(x => x.GrowerLotId is null).Select(x => x.LotNumber).Where(x => x.Length > 0).Distinct().ToList();
        var fruitProfileIds = targets.Where(x => x.FruitProfileId is not null).Select(x => x.FruitProfileId!.Value).Distinct().ToList();
        var varietyCodes = targets.Where(x => x.FruitProfileId is null).Select(x => x.VarietyCode).Where(x => x.Length > 0).Distinct().ToList();
        var hasGrowerLotIds = growerLotIds.Count > 0;
        var hasGrowerNumbers = growerNumbers.Count > 0;
        var hasLotNumbers = lotNumbers.Count > 0;
        var hasFruitProfileIds = fruitProfileIds.Count > 0;
        var hasVarietyCodes = varietyCodes.Count > 0;

        query = query.Where(x => !x.IsDeleted
            && x.ReceiptId != null
            && cropYears.Contains(x.Receipt!.CropYear)
            && ((hasGrowerLotIds && x.Receipt.GrowerLotId != null && growerLotIds.Contains(x.Receipt.GrowerLotId.Value))
                || (hasGrowerNumbers && growerNumbers.Contains((x.Receipt.GrowerNumber ?? x.Receipt.LotCode).Trim().ToUpper()))
                || (hasLotNumbers && lotNumbers.Contains(x.Receipt.LotCode.Trim().ToUpper())))
            && ((hasFruitProfileIds && fruitProfileIds.Contains(x.Receipt.FruitProfileId))
                || (hasVarietyCodes && varietyCodes.Contains(x.Receipt.FruitProfile.VarietyCode.Trim().ToUpper()))));

        return latestTakenAt is null
            ? query
            : query.Where(x => x.SampleTakenAt <= latestTakenAt.Value);
    }

    public static int CandidateLimit(int targetCount) => Math.Clamp(targetCount * 20, 500, 5000);

    public static IOrderedQueryable<QcSample> OrderCandidates(
        IQueryable<QcSample> query,
        string? providerName) =>
        providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? query.OrderByDescending(x => x.Id)
            : query.OrderByDescending(x => x.SampleTakenAt).ThenByDescending(x => x.Id);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        for (var index = 0; index < trimmed.Length; index++)
        {
            if (char.ToUpperInvariant(trimmed[index]) != trimmed[index])
            {
                return trimmed.ToUpperInvariant();
            }
        }

        return trimmed;
    }

    private static string OrganicToken(bool? value) => value switch
    {
        true => "ORGANIC",
        false => "CONVENTIONAL",
        _ => "UNKNOWN"
    };
}
