using CropQc.Web.Models;

namespace CropQc.Web.Services;

internal static class RoomReceiptProvenanceResolver
{
    internal const int MaximumTargets = 2_000;
    internal const int MaximumReceiptCandidates = 10_000;
    internal const int MaximumTransferCandidates = 10_000;
    internal const int MaximumRoomsPerPath = 500;

    internal static IReadOnlyDictionary<string, IReadOnlyList<ResolvedReceiptEvidence>> Resolve(
        IReadOnlyCollection<ReceiptProvenanceTarget> targets,
        IReadOnlyCollection<ReceiptProvenanceCandidate> receipts,
        IReadOnlyCollection<TransferProvenanceCandidate> transfers)
    {
        RequireWithinBound(targets.Count, MaximumTargets, "current fruit identities");
        RequireWithinBound(receipts.Count, MaximumReceiptCandidates, "receipt candidates");
        RequireWithinBound(transfers.Count, MaximumTransferCandidates, "transfer candidates");

        var activeTransfers = transfers
            .Where(x => !x.IsReversed)
            .OrderBy(x => x.TransferredAt)
            .ThenBy(x => x.Id)
            .ToList();
        var result = new Dictionary<string, IReadOnlyList<ResolvedReceiptEvidence>>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (target.CurrentBins <= 0)
            {
                result[target.Key] = [];
                continue;
            }

            var matchingReceipts = CanonicalQcFruitIdentity.ResolveUnambiguous(
                target.Identity,
                receipts,
                x => x.Identity);
            var matchingTransfers = CanonicalQcFruitIdentity.ResolveUnambiguous(
                target.Identity,
                activeTransfers,
                x => x.Identity);
            var evidence = new List<ResolvedReceiptEvidence>();

            foreach (var receipt in matchingReceipts)
            {
                if (receipt.OriginalRoomId == target.RoomId)
                {
                    evidence.Add(new ResolvedReceiptEvidence(receipt, RoomReceiptEvidenceTypes.Direct, []));
                    continue;
                }

                var path = FindChronologicalPath(receipt, target.RoomId, matchingTransfers);
                if (path is not null)
                {
                    evidence.Add(new ResolvedReceiptEvidence(receipt, RoomReceiptEvidenceTypes.TransferLinked, path));
                    continue;
                }

                if (IsLegacy(target.Identity))
                {
                    evidence.Add(new ResolvedReceiptEvidence(receipt, RoomReceiptEvidenceTypes.PossibleSource, []));
                }
            }

            result[target.Key] = evidence
                .OrderBy(x => EvidenceRank(x.EvidenceType))
                .ThenByDescending(x => x.Receipt.ReceivedAt)
                .ThenBy(x => x.Receipt.DisplayReceiptId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Receipt.Id)
                .ToList();
        }

        return result;
    }

    private static IReadOnlyList<long>? FindChronologicalPath(
        ReceiptProvenanceCandidate receipt,
        int targetRoomId,
        IReadOnlyList<TransferProvenanceCandidate> transfers)
    {
        var pathsByRoom = new Dictionary<int, IReadOnlyList<long>>
        {
            [receipt.OriginalRoomId] = []
        };

        foreach (var transfer in transfers)
        {
            if (transfer.TransferredAt < receipt.ReceivedAt
                || !pathsByRoom.TryGetValue(transfer.SourceRoomId, out var sourcePath))
            {
                continue;
            }

            if (sourcePath.Count >= MaximumRoomsPerPath)
            {
                throw new InvalidOperationException(
                    $"Receipt provenance path exceeds the safe limit of {MaximumRoomsPerPath} transfers.");
            }

            if (!pathsByRoom.ContainsKey(transfer.DestinationRoomId))
            {
                pathsByRoom[transfer.DestinationRoomId] = [.. sourcePath, transfer.Id];
            }
        }

        return pathsByRoom.GetValueOrDefault(targetRoomId);
    }

    internal static int EvidenceRank(string evidenceType) => evidenceType switch
    {
        RoomReceiptEvidenceTypes.Direct => 0,
        RoomReceiptEvidenceTypes.TransferLinked => 1,
        RoomReceiptEvidenceTypes.PossibleSource => 2,
        _ => 3
    };

    private static bool IsLegacy(CanonicalQcFruitIdentity identity) =>
        identity.GrowerLotId is null || identity.FruitProfileId is null;

    private static void RequireWithinBound(int count, int maximum, string label)
    {
        if (count > maximum)
        {
            throw new InvalidOperationException(
                $"Receipt provenance {label} exceed the safe limit of {maximum}. Narrow the facility or room filter.");
        }
    }
}

internal sealed record ReceiptProvenanceTarget(
    string Key,
    int RoomId,
    int CurrentBins,
    CanonicalQcFruitIdentity Identity);

internal sealed record ReceiptProvenanceCandidate(
    long Id,
    string DisplayReceiptId,
    int OriginalRoomId,
    string OriginalWarehouse,
    string OriginalRoom,
    int OriginalBins,
    DateTimeOffset ReceivedAt,
    string GrowerNumber,
    string GrowerName,
    CanonicalQcFruitIdentity Identity);

internal sealed record TransferProvenanceCandidate(
    long Id,
    int SourceRoomId,
    int DestinationRoomId,
    DateTimeOffset TransferredAt,
    bool IsReversed,
    CanonicalQcFruitIdentity Identity);

internal sealed record ResolvedReceiptEvidence(
    ReceiptProvenanceCandidate Receipt,
    string EvidenceType,
    IReadOnlyList<long> TransferPathIds);
