using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

public sealed record InventoryRecordedArrival(long LedgerId, ImmutableArray<InventoryRecordedCohort> Cohorts);
public sealed record InventoryRecordedCustodyLink(long DebitId, long CreditId, int Quantity,
    InventoryEvidenceReference Parent);

/// <summary>
/// Carries treatment from a proven recorded departure when a legacy custody
/// transaction predates lineage movement rows. It never uses projection state.
/// The adapter must validate each parent and its exact debit/credit pair.
/// </summary>
public static class InventoryRecordedCustodyReplay
{
    public static ImmutableArray<InventoryPositionEvidence> Enrich(
        IReadOnlyList<InventoryPositionEvidence> positions, IReadOnlyList<InventoryRecordedCustodyLink> links)
    {
        var ledgerPositions = positions.SelectMany(p => p.Ledger.Select(l => (Row: l, Position: p)))
            .GroupBy(x => x.Row.Id).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var credits = links.GroupBy(x => x.CreditId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var memo = new Dictionary<long, InventoryRecordedArrival?>();
        var visiting = new HashSet<long>();
        return positions.Select(p => p with
        {
            RecordedArrivals = p.Ledger.Where(l => l.Quantity > 0).Select(l => Prove(l.Id))
                .OfType<InventoryRecordedArrival>().ToImmutableArray()
        }).ToImmutableArray();

        InventoryRecordedArrival? Prove(long creditId)
        {
            if (memo.TryGetValue(creditId, out var cached)) return cached;
            if (visiting.Count >= 32 || !visiting.Add(creditId)) return null;
            var proof = ProveCore(creditId);
            visiting.Remove(creditId);
            memo[creditId] = proof;
            return proof;
        }

        InventoryRecordedArrival? ProveCore(long creditId)
        {
            if (!credits.TryGetValue(creditId, out var link)
                || !ledgerPositions.TryGetValue(link.CreditId, out var destination)
                || !ledgerPositions.TryGetValue(link.DebitId, out var source)) return null;
            var debit = source.Row;
            var credit = destination.Row;
            if (!debit.ExactIdentity || !credit.ExactIdentity || link.Quantity <= 0
                || debit.Quantity != -link.Quantity || credit.Quantity != link.Quantity
                || source.Position.Identity.Key != destination.Position.Identity.Key
                || source.Position.Location.RoomId == destination.Position.Location.RoomId
                || debit.At != credit.At
                || (debit.RecordedAt ?? debit.At) > (credit.RecordedAt ?? credit.At)) return null;
            // Preserve baseline semantics: a raw history that does not reconcile
            // with the canonical ledger cannot authorize a historical replay.
            if (source.Position.Ledger.Sum(l => (long)l.Quantity) != source.Position.AuthoritativeQuantity) return null;
            var recordedAt = debit.RecordedAt ?? debit.At;
            var prior = source.Position.Ledger.Where(l => (l.RecordedAt ?? l.At) < recordedAt
                || (l.RecordedAt ?? l.At) == recordedAt && l.Id < debit.Id).ToImmutableArray();
            var priorArrivals = prior.Where(l => l.Quantity > 0).Select(l => Prove(l.Id))
                .OfType<InventoryRecordedArrival>().ToImmutableArray();
            var carriedIds = priorArrivals.SelectMany(a => a.Cohorts).SelectMany(c => c.ApplicationIds).ToHashSet();
            // A historical application can be reversed between two legacy hops.
            // Carry its event as well as its ID, even when the intermediate room
            // has no modern movement or projection linking that application.
            var applications = source.Position.Applications.Concat(positions
                .Where(p => p.Identity.Key == source.Position.Identity.Key).SelectMany(p => p.Applications)
                .Where(a => carriedIds.Contains(a.Id))).DistinctBy(a => a.Id).ToImmutableArray();
            // A later-recorded, backdated application can change treatment at
            // departure. Do not call the old allocation untreated by omission.
            if (source.Position.Applications.Any(a => (a.RoomId == null || a.RoomId == source.Position.Location.RoomId)
                && a.AppliedAt <= debit.At && (a.RecordedAt ?? a.AppliedAt) > recordedAt)) return null;
            var sourceAtDeparture = source.Position with
            {
                Ledger = prior,
                Projections = [],
                AuthoritativeQuantity = checked(prior.Sum(l => l.Quantity)),
                Applications = applications.Where(a => (a.RecordedAt ?? a.AppliedAt) <= recordedAt)
                    .Select(a => a.ReversedAt > recordedAt ? a with { ReversedAt = null } : a).ToImmutableArray(),
                RecordedArrivals = priorArrivals
            };
            var replay = InventoryEventReplay.Replay(sourceAtDeparture);
            if (!replay.QuantityConserved || !replay.UnresolvedEvents.IsEmpty
                || replay.Cohorts.Any(c => c.State is not ("Untreated" or "Confirmed"))
                || replay.Cohorts.Sum(c => c.Quantity) < link.Quantity) return null;
            var refs = replay.Cohorts.SelectMany(c => c.Evidence).Append(link.Parent)
                .Append(new("RoomInventoryAdjustment", debit.Id.ToString()))
                .Append(new("RoomInventoryAdjustment", credit.Id.ToString())).Distinct().ToImmutableArray();
            ImmutableArray<InventoryRecordedCohort> departed;
            if (replay.Cohorts.Sum(c => c.Quantity) == link.Quantity)
                departed = replay.Cohorts.Select(c => c with { ArrivedAt = credit.At, LatestArrivalAt = credit.At, Evidence = refs }).ToImmutableArray();
            else
            {
                // A partial draw from one proven treatment pool does not prove
                // which receipts survived. Mixed treatment needs an allocation.
                if (replay.Cohorts.Select(c => (c.Signature, c.State)).Distinct().Count() != 1) return null;
                var first = replay.Cohorts[0];
                var receipts = replay.Cohorts.Select(c => c.ReceiptId).Distinct().ToArray();
                departed = [new(link.Quantity, first.Signature, first.State,
                    receipts.Length == 1 ? receipts[0] : null, first.ApplicationIds, credit.At, refs)];
            }
            return new(credit.Id, departed);
        }
    }
}
