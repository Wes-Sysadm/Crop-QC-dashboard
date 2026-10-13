using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

// A cohort is an aggregate recorded allocation, never an invented physical bin ID.
// Exact receipt ownership is deliberately nullable, independently of quantity.
public sealed record InventoryRecordedCohort(int Quantity, string Signature, string State,
    long? ReceiptId, ImmutableArray<long> ApplicationIds, DateTimeOffset ArrivedAt,
    ImmutableArray<InventoryEvidenceReference> Evidence)
{
    public DateTimeOffset LatestArrivalAt { get; init; } = ArrivedAt;
}
public sealed record InventoryEventReplayResult(bool QuantityConserved,
    ImmutableArray<InventoryRecordedCohort> Cohorts, ImmutableArray<string> UnresolvedEvents);

/// <summary>
/// Replays recorded custody effects and point-in-time treatment allocations.
/// Projection rows are deliberately not an input to any quantity or treatment decision.
/// The loader/command must separately validate authoritative parent/origin custody.
/// </summary>
public static class InventoryEventReplay
{
    public static InventoryEventReplayResult Replay(InventoryPositionEvidence position)
    {
        var cohorts = new List<InventoryRecordedCohort>();
        var problems = new List<string>();
        var ledger = position.Ledger.IsDefault ? [] : position.Ledger;
        var moves = position.Movements.IsDefault ? [] : position.Movements;
        var applications = position.Applications.IsDefault ? [] : position.Applications;
        var receipts = position.Receipts.IsDefault ? [] : position.Receipts;
        var quantityConserved = position.AuthoritativeQuantity >= 0 && position.IdentityVerified && position.CustodyVerified;
        if (position.Location.Custody != InventoryCustody.Room)
            return new(false, [], ["Room event replay requires room custody."]);

        var events = ledger.Select(x => new Event(x.RecordedAt ?? x.At, 0, x.Id, x, null))
            .Concat(applications.Where(x => x.RoomId == null || x.RoomId == position.Location.RoomId)
                .Select(x => new Event(x.RecordedAt ?? x.AppliedAt, 1, x.Id, null, x)))
            .Concat(applications.Where(x => x.ReversedAt != null)
                .Select(x => new Event(x.ReversedAt!.Value, 2, x.Id, null, x)))
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.Order).ThenBy(x => x.Id);
        foreach (var item in events)
        {
            if (item.Application is { } application)
            {
                if (item.Order == 2) Reverse(application.Id);
                else Apply(application);
                continue;
            }
            var row = item.Ledger!;
            var reference = new InventoryEvidenceReference("RoomInventoryAdjustment", row.Id.ToString());
            if (!row.ExactIdentity)
            {
                quantityConserved = false;
                problems.Add($"Ledger {row.Id}: recorded identity does not match this position.");
            }
            var related = moves.Where(x => x.ExactIdentity && x.Quantity > 0
                && (row.Quantity > 0 ? x.Incoming : x.Outgoing)
                && InventoryCohortEvidence.Related(row, x)).ToArray();
            if (row.Quantity > 0)
            {
                if (row.Kind == "ReceiptAdd" && row.ReceiptId is long receiptId
                    && receipts.Any(x => x.Id == receiptId && (x.OriginalIdentityVerified ?? x.ExactIdentity) && !x.IsTransferReceipt
                        && (x.OriginalQuantity ?? x.Quantity) == row.Quantity)
                    && ledger.Count(x => x.Kind == "ReceiptAdd" && x.ReceiptId == receiptId) == 1)
                {
                    // Committed origin quantity is retained even if the receipt was
                    // later corrected/voided. Later ledger effects are replayed below.
                    var crossed = applications.Where(a => (a.RoomId == null || a.RoomId == position.Location.RoomId)
                        && (a.ReceiptId == null || a.ReceiptId == receiptId)
                        && a.AppliedAt >= row.At && (a.RecordedAt ?? a.AppliedAt) < (row.RecordedAt ?? row.At)).ToArray();
                    var uncertain = crossed.Length > 0;
                    cohorts.Add(new(row.Quantity, uncertain ? "x" : "u", uncertain ? "Unknown" : "Untreated", receiptId, [], row.At, [reference]));
                    if (uncertain) problems.Add($"Ledger {row.Id}: effective arrival precedes applications {string.Join(',', crossed.Select(a => a.Id))}, but receipt custody was recorded afterward; occupancy allocation requires resolution.");
                }
                else if (related.Length > 0 && related.Sum(x => x.Quantity) == row.Quantity)
                {
                    foreach (var movement in related)
                    {
                        var ids = ApplicationIds(movement.Signature);
                        var known = movement.State == "Untreated" && movement.Signature == "u"
                            || movement.State == "Confirmed" && ids.Length > 0
                                && ids.All(id => applications.Any(x => x.Id == id));
                        var crossed = applications.Where(a => (a.RoomId == null || a.RoomId == position.Location.RoomId)
                            && (a.ReceiptId == null || movement.ReceiptId == null || a.ReceiptId == movement.ReceiptId)
                            && a.AppliedAt >= row.At && (a.RecordedAt ?? a.AppliedAt) < (row.RecordedAt ?? row.At)
                            && !ids.Contains(a.Id)).ToArray();
                        cohorts.Add(new(movement.Quantity, known && crossed.Length == 0 ? movement.Signature : "x",
                            known && crossed.Length == 0 ? movement.State : "Unknown", movement.ReceiptId,
                            known ? ids : [], row.At, [reference, new("TreatmentLineageMovement", movement.Id.ToString())]));
                        if (!known) problems.Add($"Movement {movement.Id}: incoming treatment membership is unresolved.");
                        if (crossed.Length > 0) problems.Add($"Ledger {row.Id}, movement {movement.Id}: effective arrival crosses already-recorded applications {string.Join(',', crossed.Select(a => a.Id))}; occupancy allocation requires resolution.");
                    }
                }
                else
                {
                    quantityConserved = false;
                    problems.Add($"Ledger {row.Id} ({row.Kind}): missing supported origin or exact incoming custody allocation.");
                    cohorts.Add(new(row.Quantity, "x", "Unknown", null, [], row.At, [reference]));
                }
            }
            else if (row.Quantity < 0)
            {
                if (cohorts.Sum(x => x.Quantity) < -row.Quantity)
                {
                    quantityConserved = false;
                    problems.Add($"Ledger {row.Id}: recorded withdrawal exceeds custody at commit time.");
                    continue;
                }
                if (related.Length > 0 && related.Sum(x => x.Quantity) == -row.Quantity)
                    foreach (var movement in related)
                        Withdraw(movement.Quantity, movement.Signature, movement.ReceiptId, reference);
                else
                {
                    // Receipt correction reason/ReceiptId is not proof of which
                    // physical allocation was withdrawn. Preserve shared ancestry.
                    Withdraw(-row.Quantity, null, null, reference);
                }
            }
        }

        quantityConserved &= cohorts.Sum(x => x.Quantity) == position.AuthoritativeQuantity;
        if (cohorts.Sum(x => x.Quantity) != position.AuthoritativeQuantity)
            problems.Add("Recorded event quantities do not equal the ledger snapshot; identify the missing baseline/correction event.");
        return new(quantityConserved, cohorts.Where(x => x.Quantity > 0).ToImmutableArray(), problems.Distinct().ToImmutableArray());

        void Withdraw(int amount, string? signature, long? receipt, InventoryEvidenceReference reference)
        {
            var selected = cohorts.Where(x => (signature == null || x.Signature == signature)
                && (receipt == null || x.ReceiptId == receipt)).ToArray();
            if (selected.Sum(x => x.Quantity) < amount)
            {
                // Quantity is established, but the recorded allocation cannot be
                // assigned to treatment cohorts. Never manufacture untreated stock.
                problems.Add($"{reference.Entity} {reference.Id}: withdrawal treatment/receipt allocation cannot be reconciled.");
                quantityConserved = false;
                selected = cohorts.ToArray();
                signature = null;
                receipt = null;
            }
            var remaining = selected.Sum(x => x.Quantity) - amount;
            foreach (var cohort in selected) cohorts.Remove(cohort);
            if (remaining == 0) return;
            var signatures = selected.Select(x => x.Signature).Distinct().ToArray();
            var states = selected.Select(x => x.State).Distinct().ToArray();
            var exactReceipts = selected.Select(x => x.ReceiptId).Distinct().ToArray();
            var known = signatures.Length == 1 && states.Length == 1;
            if (!known) problems.Add($"{reference.Entity} {reference.Id}: surviving treatment quantities remain shared/unresolved.");
            cohorts.Add(new(remaining, known ? signatures[0] : "x", known ? states[0] : "Unknown",
                exactReceipts.Length == 1 ? exactReceipts[0] : null,
                known ? selected[0].ApplicationIds : [], selected.Min(x => x.ArrivedAt),
                selected.SelectMany(x => x.Evidence).Append(reference).Distinct().ToImmutableArray())
            { LatestArrivalAt = selected.Max(x => x.LatestArrivalAt) });
        }

        void Apply(InventoryApplicationEvidence application)
        {
            var reference = new InventoryEvidenceReference("RoomTreatmentApplication", application.Id.ToString());
            var eligible = cohorts.Where(x => x.ArrivedAt <= application.AppliedAt
                && (application.ReceiptId == null || x.ReceiptId == application.ReceiptId
                    || x.ReceiptId == null && x.Evidence.Any(reference => reference.Entity == "RoomInventoryAdjustment"
                        && ledger.Any(l => l.Id.ToString() == reference.Id && l.ReceiptId == application.ReceiptId)))).ToArray();
            if (eligible.Any(x => x.LatestArrivalAt > application.AppliedAt))
            {
                problems.Add($"Application {application.Id}: shared surviving inventory spans different arrival times; exact point-in-time occupancy is unresolved.");
                MarkUnknown(eligible, reference);
                return;
            }
            if (!application.Allocations.IsDefaultOrEmpty)
            {
                var plans = new List<InventoryRecordedCohort[]>();
                var allocated = new HashSet<InventoryRecordedCohort>();
                foreach (var group in application.Allocations.GroupBy(x => new { x.ReceiptId, x.PriorSignature, x.ResultSignature }))
                {
                    var selected = eligible.Where(x => x.Signature == group.Key.PriorSignature
                        && x.ReceiptId == group.Key.ReceiptId).ToArray();
                    var expectedSignature = Signature(ApplicationIds(group.Key.PriorSignature).Append(application.Id).Distinct().Order().ToImmutableArray());
                    if (group.Any(x => !x.ExactIdentity || x.Quantity <= 0)
                        || selected.Sum(x => x.Quantity) != group.Sum(x => x.Quantity)
                        || group.Key.ResultSignature != expectedSignature || selected.Any(x => !allocated.Add(x)))
                    {
                        problems.Add($"Application {application.Id}, sources {string.Join(',', group.Select(x => x.Id))}: recorded occupancy allocation does not reconcile.");
                        MarkUnknown(eligible, reference);
                        return;
                    }
                    plans.Add(selected);
                }
                // Validate every allocation against one pre-application state.
                // A null receipt identifies shared stock, not every receipt.
                foreach (var selected in plans) Treat(selected, application.Id, reference);
            }
            else if (position.ApplicationAllocationsLoaded && eligible.Length > 0)
            {
                problems.Add($"Application {application.Id}: recorded allocation sources are missing for occupied inventory.");
                MarkUnknown(eligible, reference);
            }
            else if (eligible.Length > 0)
            {
                // Older evidence payloads have no allocation snapshots. Receipt
                // scope or whole-room occupancy still defines the application;
                // this does not allocate a receipt inside an already shared pool.
                Treat(eligible, application.Id, reference);
            }
            else if (application.ReceiptId is long receipt && cohorts.Any(x => x.ReceiptId == null))
            {
                problems.Add($"Application {application.Id}: receipt {receipt} membership within shared custody is unresolved.");
                MarkUnknown(cohorts.Where(x => x.ReceiptId == null).ToArray(), reference);
            }
        }

        void Treat(InventoryRecordedCohort[] selected, long applicationId, InventoryEvidenceReference reference)
        {
            foreach (var cohort in selected)
            {
                var index = cohorts.IndexOf(cohort);
                var ids = cohort.ApplicationIds.Append(applicationId).Distinct().Order().ToImmutableArray();
                cohorts[index] = cohort with
                {
                    Signature = cohort.State == "Unknown" ? "x" : Signature(ids),
                    State = cohort.State == "Unknown" ? "Unknown" : "Confirmed",
                    ApplicationIds = ids,
                    Evidence = cohort.Evidence.Append(reference).Distinct().ToImmutableArray()
                };
            }
        }

        void MarkUnknown(InventoryRecordedCohort[] selected, InventoryEvidenceReference reference)
        {
            foreach (var cohort in selected)
                cohorts[cohorts.IndexOf(cohort)] = cohort with
                { Signature = "x", State = "Unknown", Evidence = cohort.Evidence.Append(reference).Distinct().ToImmutableArray() };
        }

        void Reverse(long applicationId)
        {
            for (var index = 0; index < cohorts.Count; index++)
            {
                var cohort = cohorts[index];
                if (!cohort.ApplicationIds.Contains(applicationId)) continue;
                var active = cohort.ApplicationIds.Where(id => id != applicationId).ToImmutableArray();
                cohorts[index] = cohort with
                {
                    ApplicationIds = active,
                    Signature = cohort.State == "Unknown" ? "x" : Signature(active),
                    State = cohort.State == "Unknown" ? "Unknown" : active.IsEmpty ? "Untreated" : "Confirmed"
                };
            }
        }
    }

    private sealed record Event(DateTimeOffset RecordedAt, int Order, long Id,
        InventoryLedgerEvidence? Ledger, InventoryApplicationEvidence? Application);
    private static string Signature(ImmutableArray<long> ids) => ids.IsEmpty ? "u" : $"u|a:{string.Join(',', ids.Order())}";
    public static ImmutableArray<long> ApplicationIds(string signature)
    {
        if (!signature.StartsWith("u|a:", StringComparison.Ordinal)) return [];
        var ids = new List<long>();
        foreach (var token in signature[4..].Split(','))
        {
            if (!long.TryParse(token, out var id) || id <= 0 || ids.Contains(id)) return [];
            ids.Add(id);
        }
        return ids.Order().ToImmutableArray();
    }
}
