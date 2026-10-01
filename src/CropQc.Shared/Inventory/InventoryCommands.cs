using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

public enum InventoryCommandKind
{
    RoomMove, WarehouseTransfer, Dump, ProcessorSale, OutsideWarehouseTransfer, InterCompanyDispatch,
    ReceiveTransfer, Loss, TreatmentAssignment, TreatmentReversal, ReceiptCorrection, Return,
    TransferEdit, ReopenTransfer, BaselineAdjustment
}
public enum InventoryCommandStatus { Committed, Replayed, InvalidIntent, Blocked, Stale, Conflict, RetryRequired }
public sealed record InventoryCommandSource(InventoryIdentity Identity, InventoryLocation Location,
    string ExpectedFingerprint, ImmutableArray<InventoryVersion> ExpectedVersions);
public sealed record InventoryCommandDestination(int WarehouseId, int RoomId);
public enum InventoryAdjustmentDirection { Decrease, Increase }
public sealed record InventoryCommandLine(InventoryCommandSource Source, int Quantity,
    string TreatmentSignature, InventoryCommandDestination? Destination = null, long? ReceiptId = null,
    InventoryAdjustmentDirection AdjustmentDirection = InventoryAdjustmentDirection.Decrease);
public sealed record InventoryCommand(string OperationKey, InventoryCommandKind Kind, int ActorId,
    DateTimeOffset EffectiveAt, string Reason, ImmutableArray<InventoryCommandLine> Lines,
    int? CounterpartyId = null, string? CustodyGroup = null, long? TreatmentApplicationId = null,
    int? TreatmentChemicalId = null, string? OriginalOperationKey = null,
    InventoryReceivingEvidence? ReceivingEvidence = null, InventoryProcessorTerms? ProcessorTerms = null,
    long? ExpectedTransferVersion = null);
public sealed record InventoryReceivingEvidence(long ReceiptId, long ExpectedVersion);
public sealed record InventoryProcessorTerms(decimal Rate, string Basis, string Currency, decimal? PoundsPerBin = null);
public sealed record InventoryCommandEffect(string PositionKey, int Before, int After, int Quantity,
    long? ParentId, ImmutableArray<long> LedgerIds, ImmutableArray<long> MovementIds);
public sealed record InventoryCommandResult(InventoryCommandStatus Status, string OperationKey, string Detail,
    ImmutableArray<InventoryCommandEffect> Effects, int Attempts = 1);
public interface IInventoryCommandExecutor
{
    Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default);
}

// Evidence requirements belong to the operation, never to a caller-supplied boolean.
public static class InventoryCommandPolicy
{
    public static InventoryOperationRequirements Requirements(InventoryCommandKind kind, InventoryCommandLine line) =>
        new(RequireKnownTreatment: true, RequireExactReceipt: kind == InventoryCommandKind.ReceiptCorrection,
            ReceiptId: line.ReceiptId, TreatmentSignature: line.TreatmentSignature,
            AllowedCustody: line.Source.Location.Custody, ExpectedFingerprint: line.Source.ExpectedFingerprint);
    public static bool IsRoomMove(InventoryCommandKind kind) => kind is InventoryCommandKind.RoomMove
        or InventoryCommandKind.WarehouseTransfer;
    public static bool IsTreatment(InventoryCommandKind kind) => kind is InventoryCommandKind.TreatmentAssignment or InventoryCommandKind.TreatmentReversal;
}

public sealed record InventoryProjectionChange(long Id, int BeforeQuantity, int AfterQuantity,
    long BeforeVersion, long AfterVersion, string BeforeDisposition, string AfterDisposition,
    string Signature, long? ReceiptId, ImmutableArray<long> ApplicationIds, string RawIdentityKey, string TreatmentState, DateTimeOffset BeforeUpdatedAt);
public sealed record InventoryNormalizationPlan(string Algorithm, string EvidenceVersion, string Fingerprint,
    string Reason, InventoryReceiptProvenance ReceiptProvenance, ImmutableArray<InventoryEvidenceReference> Evidence,
    ImmutableArray<InventoryProjectionChange> Changes, int ReplacementQuantity, long? ReplacementReceiptId, long? ReplacementProjectionId = null);

public static class InventoryNormalizationPlanner
{
    public const string Algorithm = "canonical-pool-supersession/v1";
    public static InventoryNormalizationPlan? Plan(InventoryPositionEvidence evidence, InventoryAvailabilityResult result)
    {
        if (!result.IsOperable || result.TreatmentConfidence != InventoryConfidence.Proven)
            throw new InvalidOperationException("Unproven availability cannot authorize projection writes.");
        var current = evidence.Projections.Where(x => x.Disposition == "Current").ToArray();
        if (current.Sum(x => x.Quantity) == result.AuthoritativeQuantity && current.All(x => x.RawKey == result.Identity.Key)) return null;
        // An aggregate candidate is not a row allocation. Independently prove the entire
        // current pool WITHOUT ANY projection quantity, then supersede every identified
        // row. No surviving receipt allocation is invented or proportionally trimmed.
        var independent = InventoryAvailabilityResolver.Resolve(evidence with { Projections = [] }, new());
        if (!independent.IsOperable || independent.AvailableQuantity != result.AuthoritativeQuantity
            || independent.TreatmentSlices.Any(x => x.Signature != "u")
            || current.Any(x => !x.ExactIdentity || x.Quantity < 0 || x.Signature != "u" || !x.ApplicationIds.IsEmpty))
            throw new InvalidOperationException("No exact evidence-backed supersession plan exists.");
        return new(Algorithm, result.Proof.EvidenceVersion, result.Watermark.Fingerprint,
            "Retire exact old projection rows; recreate independently proven untreated pool without per-receipt allocation.",
            result.ReceiptProvenance, result.Proof.Evidence,
            current.Where(x => x.Quantity > 0).OrderBy(x => x.Id).Select(x => new InventoryProjectionChange(x.Id,
                x.Quantity, 0, x.Version, checked(x.Version + 1), "Current", "Historical", x.Signature, x.ReceiptId, x.ApplicationIds, x.RawKey, x.State, x.UpdatedAt)).ToImmutableArray(),
            result.AuthoritativeQuantity, result.ReceiptProvenance.Confidence == InventoryConfidence.Proven
                && result.ReceiptProvenance.ReceiptIds.Length == 1 ? result.ReceiptProvenance.ReceiptIds[0] : null);
    }
}
