using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

// A maintenance plan refines an independently established pool proof. It never
// calculates room authority, assigns a gap a treatment, or allocates old bins to
// a recent receipt. Exact newer receipts survive byte-for-byte.
public static class SelectiveProjectionReconstruction
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<ImmutableArray<long>> PreservedCanonicalPopulationsAsync(CropQcDbContext db,
        InventoryPositionEvidence e, ReconstructionCommandEvidence.Coverage coverage, CancellationToken ct)
    {
        var current = e.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0 && x.ExactIdentity
            && x.RawKey == e.Identity.Key && x.State == "Untreated" && x.Signature == "u" && x.ApplicationIds.IsEmpty).ToArray();
        var positionKey = InventoryAvailabilityResolver.Resolve(e, new()).PositionKey;
        var normalizations = await db.AuditLogs.AsNoTracking().Where(x => x.Action == "CanonicalInventoryNormalization"
            && x.SourceApplication == "CanonicalInventory/v1" && x.EntityKey == positionKey).ToArrayAsync(ct);
        var validated = coverage.MovementIds;
        var result = ImmutableArray.CreateBuilder<long>();
        foreach (var p in current)
        {
            foreach (var audit in normalizations)
            {
                var plan = JsonSerializer.Deserialize<InventoryNormalizationPlan>(audit.AfterValuesJson!, Json);
                if (plan?.ReplacementProjectionId != p.Id || plan.ReplacementReceiptId != p.ReceiptId || plan.Changes.IsEmpty) continue;
                if (e.Ledger.Any(x => !coverage.LedgerIds.Contains(x.Id)
                    && !plan.Evidence.Any(r => r.Entity == "RoomInventoryAdjustment" && r.Id == x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))) continue;
                var ids = plan.Changes.Select(x => x.Id).ToArray();
                var retired = await db.TreatmentLineageSegments.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
                if (retired.Length != ids.Length || retired.Any(x => x.Disposition != "Historical" || x.CurrentBins != 0
                    || !plan.Changes.Any(c => c.Id == x.Id && c.BeforeQuantity == x.RetiredQuantity && c.AfterVersion == x.ConcurrencyVersion))) continue;
                var key = retired.Select(x => x.RetiredByCommandKey).Distinct().SingleOrDefault();
                if (key == null) continue;
                var command = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, ct);
                if (command == null || command.CommittedAt != audit.CreatedAt || command.ActorId != audit.UserId
                    || command.IntentHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.IntentJson)))) continue;
                var moves = e.Movements.Where(x => x.CreatedAt >= audit.CreatedAt
                    && (x.SourceProjectionId == p.Id || x.DestinationProjectionId == p.Id)).ToArray();
                if (moves.Any(x => !x.ExactIdentity || x.State != "Untreated" || x.Signature != "u" || !validated.Contains(x.Id))) continue;
                var quantity = plan.ReplacementQuantity + moves.Where(x => x.DestinationProjectionId == p.Id).Sum(x => x.Quantity)
                    - moves.Where(x => x.SourceProjectionId == p.Id).Sum(x => x.Quantity);
                if (quantity == p.Quantity) result.Add(p.Id);
            }
        }
        foreach (var p in current)
        {
            var moves = e.Movements.Where(x => x.SourceProjectionId == p.Id || x.DestinationProjectionId == p.Id).ToArray();
            if (moves.Length == 0 || moves.Any(x => !validated.Contains(x.Id) || !x.ExactIdentity
                || x.State != "Untreated" || x.Signature != "u" || x.ReceiptId != p.ReceiptId)) continue;
            var arrival = moves.Where(x => x.DestinationProjectionId == p.Id).Select(x => x.CreatedAt).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (e.Ledger.Any(x => (x.RecordedAt ?? x.At) >= arrival && !coverage.LedgerIds.Contains(x.Id))) continue;
            if (moves.Where(x => x.DestinationProjectionId == p.Id).Sum(x => x.Quantity)
                - moves.Where(x => x.SourceProjectionId == p.Id).Sum(x => x.Quantity) == p.Quantity) result.Add(p.Id);
        }
        return result.Distinct().ToImmutableArray();
    }

    // If previously audited canonical populations already cover all current
    // authority, there is no residual pool to create. Keep their IDs and metadata.
    public static InventoryNormalizationPlan? FullyRepresented(InventoryPositionEvidence e, ImmutableArray<long> known)
    {
        var current = e.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0).ToArray();
        var retained = current.Where(x => known.Contains(x.Id)).ToArray();
        var stale = current.Where(x => !known.Contains(x.Id)).ToArray();
        if (retained.Length == 0 || stale.Length == 0 || retained.Sum(x => x.Quantity) != e.AuthoritativeQuantity
            || stale.Any(x => !x.ExactIdentity || x.State != "Untreated" || x.Signature != "u" || !x.ApplicationIds.IsEmpty)) return null;
        var resolved = InventoryAvailabilityResolver.Resolve(e with { Projections = retained.ToImmutableArray() }, new());
        if (!resolved.IsOperable || resolved.TreatmentConfidence != InventoryConfidence.Proven) return null;
        var anchor = retained.OrderBy(x => x.Id).First();
        return new("preserved-canonical-populations/v1", "canonical-normalization-and-movement/v1", e.Watermark.Fingerprint,
            "Current canonical populations account for all authority; retire only excess historical representations.", resolved.ReceiptProvenance,
            resolved.Proof.Evidence, stale.OrderBy(x => x.Id).Select(x => new InventoryProjectionChange(x.Id, x.Quantity, 0,
                x.Version, x.Version + 1, x.Disposition, "Historical", x.Signature, x.ReceiptId, x.ApplicationIds, x.RawKey, x.State, x.UpdatedAt)).ToImmutableArray(),
            anchor.Quantity, anchor.ReceiptId, anchor.Id);
    }

    public static (InventoryNormalizationPlan? Plan, ImmutableArray<string> Blockers) Refine(
        InventoryPositionEvidence evidence, InventoryNormalizationPlan? provenPool, ImmutableArray<long> known = default)
    {
        if (provenPool == null) return (null, []);
        var current = evidence.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0).ToArray();
        if (current.Any(x => !x.ExactIdentity || x.State != "Untreated" || x.Signature != "u" || !x.ApplicationIds.IsEmpty))
            return (null, ["Selective shared-pool reconstruction requires independently proven untreated history; current treatment populations cannot be collapsed."]);
        var preserve = current.Where(x => !known.IsDefault && known.Contains(x.Id)).ToList();
        foreach (var receipt in current.Where(x => x.ReceiptId != null).GroupBy(x => x.ReceiptId!.Value))
        {
            var rows = receipt.ToArray();
            if (rows.All(x => preserve.Any(p => p.Id == x.Id))) continue;
            // Legacy transfer snapshots can carry a receipt ID without proving
            // surviving receipt ancestry. Their independently proven shared pool
            // must not be mistaken for a newly recorded ordinary receipt.
            if (!rows.Any(p => evidence.Movements.Any(x => x.Kind == "Receipt" && x.Incoming && x.DestinationProjectionId == p.Id))) continue;
            // A canonical key alone is insufficient. Immutable movement allocation
            // and the normal resolver's exact receipt proof must both agree.
            if (rows.Any(x => x.RawKey != evidence.Identity.Key)) continue;
            if (rows.Any(p => !evidence.Movements.Any(x => x.Incoming && x.DestinationProjectionId == p.Id)
                || evidence.Movements.Where(x => x.DestinationProjectionId == p.Id).Sum(x => x.Quantity)
                    - evidence.Movements.Where(x => x.SourceProjectionId == p.Id).Sum(x => x.Quantity) != p.Quantity)) continue;
            var receiptQuantity = rows.Sum(x => x.Quantity);
            var remaining = evidence.AuthoritativeQuantity - receiptQuantity;
            if (remaining < 0) return (null, [$"Receipt {receipt.Key}: recorded allocation exceeds canonical room authority."]);
            var candidate = evidence with
            {
                Projections = rows.Concat(remaining == 0 ? [] : new[] { Shared(evidence, remaining) }).ToImmutableArray()
            };
            var exact = InventoryAvailabilityResolver.Resolve(candidate, new(RequireExactReceipt: true, ReceiptId: receipt.Key));
            if (exact.IsOperable && exact.ReceiptProvenance.Confidence == InventoryConfidence.Proven) preserve.AddRange(rows.Where(x => preserve.All(y => y.Id != x.Id)));
            else return (null, [$"Receipt {receipt.Key}: incoming movements exist but exact current receipt allocation is not proven; preserve its rows and investigate the conflicting ledger/movement event."]);
        }
        var preservedIds = preserve.Select(x => x.Id).ToHashSet();
        var residual = evidence.AuthoritativeQuantity - preserve.Sum(x => x.Quantity);
        var changes = provenPool.Changes.Where(x => !preservedIds.Contains(x.Id)).ToImmutableArray();
        if (residual < 0 || changes.IsEmpty) return (null, ["No disjoint historical excess remains after preserving proven receipt populations."]);
        if (residual == 0)
        {
            var anchor = preserve.OrderBy(x => x.Id).First();
            return (provenPool with
            {
                Changes = changes,
                ReplacementQuantity = anchor.Quantity,
                ReplacementReceiptId = anchor.ReceiptId,
                ReplacementProjectionId = anchor.Id,
                Reason = "Retire only historical excess; all canonical authority is already represented by independently proven receipt populations."
            }, []);
        }
        if (preserve.Any(x => x.ReceiptId == null))
            return (null, ["A proven current shared population exists alongside an unresolved residual; a second shared projection would overlap it. No current population was selected for retirement."]);
        var represented = evidence with { Projections = preserve.Append(Shared(evidence, residual)).ToImmutableArray() };
        var post = InventoryAvailabilityResolver.Resolve(represented, new());
        if (!post.IsOperable || post.TreatmentConfidence != InventoryConfidence.Proven)
            return (null, ["Normal canonical resolver rejects the proposed disjoint receipt/shared treatment populations."]);
        return (provenPool with
        {
            Changes = changes,
            ReplacementQuantity = residual,
            ReplacementReceiptId = null,
            Reason = "Preserve exact current receipt populations; reconstruct only the independently proven residual shared pool."
        }, []);
    }

    private static InventoryProjectionEvidence Shared(InventoryPositionEvidence e, int quantity) => new(
        -1, e.Identity.Key, quantity, "Untreated", "u", null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, true, []);
}
