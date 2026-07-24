using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class OrchardIdentityEvidenceTypes
{
    public const string CanonicalOrchard = "Canonical Orchard";
    public const string CanonicalOrchardAlias = "Alias";
    public const string CanonicalBlock = "Canonical Block";
    public const string CanonicalBlockAlias = "Canonical Block Alias";
    public const string Grower = "Grower";
    public const string GrowerAlias = "Grower Alias";
    public const string GrowerNumber = "Grower Number";
    public const string GrowerLot = "Grower Lot";
    public const string Receipt = "Receipt";
    public const string FieldSample = "Field Sample";
}

public sealed record OrchardIdentityEvidence(
    string EvidenceType,
    string Identity,
    string NormalizedIdentity,
    int? CanonicalOrchardId,
    string? CanonicalOrchardName,
    int? CanonicalBlockId = null,
    string? BlockName = null,
    int? CanonicalGrowerId = null,
    string? GrowerName = null,
    string? GrowerNumber = null,
    int? GrowerLotId = null,
    string? LotNumber = null,
    string? Facility = null,
    int? CropYear = null,
    DateTimeOffset? LastObservedAt = null,
    string? SourceRecord = null)
{
    public bool CanonicalSetupRequired => CanonicalOrchardId is null;
}

public sealed record OrchardIdentityResolutionSet(
    IReadOnlyList<CanonicalOrchardMatchSource> Orchards,
    IReadOnlyList<OrchardIdentityEvidence> Evidence);

public sealed record OrchardIdentitySearchResult(
    string ResultType,
    string DisplayName,
    int? CanonicalOrchardId,
    string? CanonicalOrchardName,
    IReadOnlyList<int> CanonicalBlockIds,
    int? CanonicalGrowerId,
    IReadOnlyList<int> GrowerLotIds,
    string? GrowerName,
    string? GrowerNumber,
    string? BlockName,
    string? LotNumber,
    IReadOnlyList<string> Facilities,
    IReadOnlyList<int> CropYears,
    IReadOnlyList<string> SourceRecords,
    DateTimeOffset? LastObservedAt,
    decimal Confidence,
    string MatchReason,
    bool CanonicalSetupRequired);

public interface IOrchardIdentityResolverService
{
    Task<OrchardIdentityResolutionSet> LoadAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OrchardIdentitySearchResult>> SearchAsync(string? query, int limit, CancellationToken cancellationToken);
}

public sealed class OrchardIdentityResolverService(CropQcDbContext dbContext) : IOrchardIdentityResolverService
{
    public async Task<OrchardIdentityResolutionSet> LoadAsync(CancellationToken cancellationToken)
    {
        var orchards = await dbContext.CanonicalOrchards.AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Aliases.Where(a => a.IsActive))
            .Include(x => x.ReportRecipients)
            .OrderBy(x => x.OrchardName)
            .ToListAsync(cancellationToken);
        var orchardSources = orchards
            .Where(x => !OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(x.OrchardName))
            .Select(x => new CanonicalOrchardMatchSource(
                x.Id,
                x.OrchardName,
                x.Aliases.Select(a => (a.AliasText, a.NormalizedAlias)).ToArray(),
                x.ReportRecipients.Select(r => (r.Id, r.EmailAddress, r.NormalizedEmailAddress, r.IsActive, r.IsDeleted)).ToArray()))
            .ToArray();
        var validOrchardIds = orchardSources.Select(x => x.Id).ToHashSet();
        var evidence = new List<OrchardIdentityEvidence>();
        foreach (var orchard in orchards.Where(x => validOrchardIds.Contains(x.Id)))
        {
            Add(evidence, OrchardIdentityEvidenceTypes.CanonicalOrchard, orchard.OrchardName, orchard.Id,
                orchard.OrchardName, lastObservedAt: orchard.UpdatedAt, sourceRecord: $"CanonicalOrchard:{orchard.Id}");
            foreach (var alias in orchard.Aliases)
            {
                Add(evidence, OrchardIdentityEvidenceTypes.CanonicalOrchardAlias, alias.AliasText, orchard.Id,
                    orchard.OrchardName, lastObservedAt: alias.UpdatedAt, sourceRecord: $"CanonicalOrchardAlias:{alias.Id}");
            }
        }

        var blocks = await dbContext.CanonicalOrchardBlocks.AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Aliases.Where(a => a.IsActive))
            .Include(x => x.CanonicalOrchard)
            .Include(x => x.CanonicalGrower)
            .OrderBy(x => x.OrchardName)
            .ThenBy(x => x.CanonicalBlockName)
            .ToListAsync(cancellationToken);
        var growers = await dbContext.CanonicalGrowers.AsNoTracking()
            .Where(x => x.IsActive && x.MergedIntoCanonicalGrowerId == null)
            .Include(x => x.Aliases.Where(a => a.IsActive))
            .Include(x => x.GrowerNumbers.Where(n => n.IsActive))
            .ToListAsync(cancellationToken);
        var growerLots = await dbContext.GrowerLots.AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.Warehouse)
            .Include(x => x.CanonicalOrchardBlock)
                .ThenInclude(x => x!.CanonicalOrchard)
            .ToListAsync(cancellationToken);
        var fieldSamples = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.ReceiptId == null && !x.IsDeleted)
            .Include(x => x.CanonicalOrchardBlock)
                .ThenInclude(x => x!.CanonicalOrchard)
            .ToListAsync(cancellationToken);

        foreach (var block in blocks.Where(x => validOrchardIds.Contains(x.CanonicalOrchardId)))
        {
            Add(evidence, OrchardIdentityEvidenceTypes.CanonicalBlock, block.OrchardName, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName, block.CanonicalGrowerId,
                block.CanonicalGrower?.DisplayName, sourceRecord: $"CanonicalOrchardBlock:{block.Id}");
            Add(evidence, OrchardIdentityEvidenceTypes.CanonicalBlock, block.CanonicalBlockName, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName, block.CanonicalGrowerId,
                block.CanonicalGrower?.DisplayName, sourceRecord: $"CanonicalOrchardBlock:{block.Id}");
            foreach (var alias in block.Aliases)
            {
                Add(evidence, OrchardIdentityEvidenceTypes.CanonicalBlockAlias, alias.AliasName, block.CanonicalOrchardId,
                    block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName, block.CanonicalGrowerId,
                    block.CanonicalGrower?.DisplayName, sourceRecord: $"OrchardBlockAlias:{alias.Id}");
            }
        }

        foreach (var grower in growers)
        {
            var targets = DistinctOrchards(blocks
                .Where(x => x.CanonicalGrowerId == grower.Id && validOrchardIds.Contains(x.CanonicalOrchardId))
                .Select(x => (x.CanonicalOrchardId, x.CanonicalOrchard.OrchardName)));
            AddForTargets(evidence, OrchardIdentityEvidenceTypes.Grower, grower.DisplayName, targets,
                canonicalGrowerId: grower.Id, growerName: grower.DisplayName,
                lastObservedAt: grower.UpdatedAt, sourceRecord: $"CanonicalGrower:{grower.Id}");
            foreach (var alias in grower.Aliases)
            {
                AddForTargets(evidence, OrchardIdentityEvidenceTypes.GrowerAlias, alias.AliasName, targets,
                    canonicalGrowerId: grower.Id, growerName: grower.DisplayName,
                    lastObservedAt: alias.UpdatedAt, sourceRecord: $"CanonicalGrowerAlias:{alias.Id}");
            }
            foreach (var number in grower.GrowerNumbers)
            {
                Add(evidence, OrchardIdentityEvidenceTypes.GrowerNumber, number.GrowerNumber, null, null,
                    canonicalGrowerId: grower.Id, growerName: grower.DisplayName, growerNumber: number.GrowerNumber,
                    facility: number.Facility, cropYear: number.CropYear, lastObservedAt: number.UpdatedAt,
                    sourceRecord: $"CanonicalGrowerNumber:{number.Id}");
            }
        }

        var receiptTargetsByGrowerLot = receipts
            .Where(x => x.GrowerLotId is not null && x.CanonicalOrchardBlock is not null
                && validOrchardIds.Contains(x.CanonicalOrchardBlock.CanonicalOrchardId))
            .GroupBy(x => x.GrowerLotId!.Value)
            .ToDictionary(
                x => x.Key,
                x => DistinctOrchards(x.Select(r => (
                    r.CanonicalOrchardBlock!.CanonicalOrchardId,
                    r.CanonicalOrchardBlock.CanonicalOrchard.OrchardName))));
        foreach (var lot in growerLots)
        {
            var targets = receiptTargetsByGrowerLot.GetValueOrDefault(lot.Id) ?? [];
            AddForTargets(evidence, OrchardIdentityEvidenceTypes.GrowerLot, lot.Grower, targets,
                growerName: lot.Grower, growerLotId: lot.Id, lotNumber: lot.LotNumber,
                lastObservedAt: lot.UpdatedAt, sourceRecord: $"GrowerLot:{lot.Id}");
            AddForTargets(evidence, OrchardIdentityEvidenceTypes.GrowerLot, lot.LotNumber, targets,
                growerName: lot.Grower, growerLotId: lot.Id, lotNumber: lot.LotNumber,
                lastObservedAt: lot.UpdatedAt, sourceRecord: $"GrowerLot:{lot.Id}");
        }

        foreach (var receipt in receipts.Where(x => x.CanonicalOrchardBlock is not null
            && validOrchardIds.Contains(x.CanonicalOrchardBlock.CanonicalOrchardId)))
        {
            var block = receipt.CanonicalOrchardBlock!;
            Add(evidence, OrchardIdentityEvidenceTypes.Receipt, receipt.GrowerName, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName, growerName: receipt.GrowerName,
                growerNumber: receipt.GrowerNumber, growerLotId: receipt.GrowerLotId, lotNumber: receipt.LotCode,
                facility: receipt.Warehouse.Code, cropYear: receipt.CropYear,
                lastObservedAt: receipt.ReceivedAt, sourceRecord: $"Receipt:{receipt.Id}");
            Add(evidence, OrchardIdentityEvidenceTypes.Receipt, receipt.LotCode, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName, growerName: receipt.GrowerName,
                growerNumber: receipt.GrowerNumber, growerLotId: receipt.GrowerLotId, lotNumber: receipt.LotCode,
                facility: receipt.Warehouse.Code, cropYear: receipt.CropYear,
                lastObservedAt: receipt.ReceivedAt, sourceRecord: $"Receipt:{receipt.Id}");
        }

        foreach (var sample in fieldSamples.Where(x => x.CanonicalOrchardBlock is not null
            && validOrchardIds.Contains(x.CanonicalOrchardBlock.CanonicalOrchardId)))
        {
            var block = sample.CanonicalOrchardBlock!;
            Add(evidence, OrchardIdentityEvidenceTypes.FieldSample, sample.FieldSampleGrowerName, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName,
                growerName: sample.FieldSampleGrowerName, growerNumber: sample.FieldSampleGrowerNumber,
                cropYear: sample.SampleTakenAt.Year, lastObservedAt: sample.SampleTakenAt, sourceRecord: $"QcSample:{sample.Id}");
            Add(evidence, OrchardIdentityEvidenceTypes.FieldSample, sample.FieldSampleOriginalBlockName, block.CanonicalOrchardId,
                block.CanonicalOrchard.OrchardName, block.Id, block.CanonicalBlockName,
                growerName: sample.FieldSampleGrowerName, growerNumber: sample.FieldSampleGrowerNumber,
                cropYear: sample.SampleTakenAt.Year, lastObservedAt: sample.SampleTakenAt, sourceRecord: $"QcSample:{sample.Id}");
        }

        return new OrchardIdentityResolutionSet(orchardSources, evidence);
    }

    public async Task<IReadOnlyList<OrchardIdentitySearchResult>> SearchAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = OrchardContactNormalization.NormalizeOrchardIdentity(query);
        if (normalized.Length < 2) return [];
        var set = await LoadAsync(cancellationToken);
        var matches = set.Evidence
            .Select(x => new
            {
                Evidence = x,
                Score = x.NormalizedIdentity == normalized
                    ? 1m
                    : x.NormalizedIdentity.Contains(normalized, StringComparison.Ordinal)
                        ? 0.90m
                        : OrchardBlockMatcher.Similarity(query ?? "", x.Identity)
            })
            .Where(x => x.Score >= 0.30m)
            .GroupBy(x => new
            {
                x.Evidence.EvidenceType,
                x.Evidence.Identity,
                x.Evidence.CanonicalOrchardId,
                x.Evidence.CanonicalOrchardName,
                x.Evidence.CanonicalGrowerId,
                x.Evidence.GrowerName,
                x.Evidence.GrowerNumber,
                x.Evidence.BlockName,
                x.Evidence.LotNumber
            })
            .Select(x => new OrchardIdentitySearchResult(
                x.Key.EvidenceType,
                x.Key.Identity,
                x.Key.CanonicalOrchardId,
                x.Key.CanonicalOrchardName,
                x.Select(y => y.Evidence.CanonicalBlockId).OfType<int>().Distinct().OrderBy(y => y).ToArray(),
                x.Key.CanonicalGrowerId,
                x.Select(y => y.Evidence.GrowerLotId).OfType<int>().Distinct().OrderBy(y => y).ToArray(),
                x.Key.GrowerName,
                x.Key.GrowerNumber,
                x.Key.BlockName,
                x.Key.LotNumber,
                x.Select(y => y.Evidence.Facility).Where(y => !string.IsNullOrWhiteSpace(y)).Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y).ToArray(),
                x.Select(y => y.Evidence.CropYear).OfType<int>().Distinct().OrderByDescending(y => y).ToArray(),
                x.Select(y => y.Evidence.SourceRecord).Where(y => !string.IsNullOrWhiteSpace(y)).Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y).ToArray(),
                x.Max(y => y.Evidence.LastObservedAt),
                x.Max(y => y.Score),
                x.Key.CanonicalOrchardId is null
                    ? "Existing identity found; canonical orchard setup or selection is required."
                    : $"Existing {x.Key.EvidenceType.ToLowerInvariant()} evidence resolves through a confirmed canonical relationship.",
                x.Key.CanonicalOrchardId is null))
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => EvidencePriority(x.ResultType))
            .ThenBy(x => x.DisplayName)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
        return matches;
    }

    private static void Add(
        ICollection<OrchardIdentityEvidence> evidence,
        string evidenceType,
        string? identity,
        int? canonicalOrchardId,
        string? canonicalOrchardName,
        int? canonicalBlockId = null,
        string? blockName = null,
        int? canonicalGrowerId = null,
        string? growerName = null,
        string? growerNumber = null,
        int? growerLotId = null,
        string? lotNumber = null,
        string? facility = null,
        int? cropYear = null,
        DateTimeOffset? lastObservedAt = null,
        string? sourceRecord = null)
    {
        if (string.IsNullOrWhiteSpace(identity)) return;
        evidence.Add(new OrchardIdentityEvidence(
            evidenceType,
            identity.Trim(),
            OrchardContactNormalization.NormalizeOrchardIdentity(identity),
            canonicalOrchardId,
            canonicalOrchardName,
            canonicalBlockId,
            blockName,
            canonicalGrowerId,
            growerName,
            growerNumber,
            growerLotId,
            lotNumber,
            facility,
            cropYear,
            lastObservedAt,
            sourceRecord));
    }

    private static IReadOnlyList<(int Id, string Name)> DistinctOrchards(
        IEnumerable<(int Id, string Name)> candidates) =>
        candidates.DistinctBy(x => x.Id).OrderBy(x => x.Name).ToArray();

    private static void AddForTargets(
        ICollection<OrchardIdentityEvidence> evidence,
        string evidenceType,
        string? identity,
        IReadOnlyList<(int Id, string Name)> targets,
        int? canonicalGrowerId = null,
        string? growerName = null,
        int? growerLotId = null,
        string? lotNumber = null,
        DateTimeOffset? lastObservedAt = null,
        string? sourceRecord = null)
    {
        if (targets.Count == 0)
        {
            Add(evidence, evidenceType, identity, null, null,
                canonicalGrowerId: canonicalGrowerId, growerName: growerName,
                growerLotId: growerLotId, lotNumber: lotNumber,
                lastObservedAt: lastObservedAt, sourceRecord: sourceRecord);
            return;
        }

        foreach (var target in targets)
        {
            Add(evidence, evidenceType, identity, target.Id, target.Name,
                canonicalGrowerId: canonicalGrowerId, growerName: growerName,
                growerLotId: growerLotId, lotNumber: lotNumber,
                lastObservedAt: lastObservedAt, sourceRecord: sourceRecord);
        }
    }

    internal static int EvidencePriority(string evidenceType) => evidenceType switch
    {
        OrchardIdentityEvidenceTypes.CanonicalOrchard => 1,
        OrchardIdentityEvidenceTypes.CanonicalOrchardAlias => 2,
        OrchardIdentityEvidenceTypes.Grower => 3,
        OrchardIdentityEvidenceTypes.GrowerAlias => 3,
        OrchardIdentityEvidenceTypes.GrowerLot => 4,
        OrchardIdentityEvidenceTypes.CanonicalBlock => 5,
        OrchardIdentityEvidenceTypes.CanonicalBlockAlias => 5,
        OrchardIdentityEvidenceTypes.Receipt => 6,
        OrchardIdentityEvidenceTypes.FieldSample => 6,
        OrchardIdentityEvidenceTypes.GrowerNumber => 7,
        _ => 99
    };
}
