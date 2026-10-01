using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CropQc.Shared.Inventory;

public static class InventoryLedgerKinds
{
    // Versions 0–2 retain legacy receipt-date baseline semantics. Canonical
    // descendants use their own effective date; only ReceiptAdd uses receiving time.
    public const int CanonicalCommandInvariantVersion = 3;
    public const string StartingInventoryImport = "StartingInventoryImport";
    public const string BinsRun = "BinsRun";
    public const string DroppedBins = "DroppedBins";
    public const string DroppedBinsReversal = "DroppedBinsReversal";
}

[JsonConverter(typeof(JsonStringEnumConverter<InventoryCustody>))]
public enum InventoryCustody { Room, InTransit, OutsideWarehouse, Processor }
[JsonConverter(typeof(JsonStringEnumConverter<InventoryConfidence>))]
public enum InventoryConfidence { Unknown, Proven, Ambiguous }
[JsonConverter(typeof(JsonStringEnumConverter<InventoryBlockerCode>))]
public enum InventoryBlockerCode
{
    NegativeAuthoritativeBalance, ConflictingIdentity, UnknownTreatment, MixedTreatmentAmbiguity,
    MissingReceiptProvenance, InvalidCustody, UnsupportedHistoricalEvidence, StaleRead,
    HistoricalSnapshotUnavailable, SelectedTreatmentUnavailable
}
[JsonConverter(typeof(JsonStringEnumConverter<ProjectionExclusionReason>))]
public enum ProjectionExclusionReason { Depleted, DuplicateStatusAlias, ConsumedHistoricalRepresentation, UnprovenProjection, StaleHistoricalPool }
[JsonConverter(typeof(JsonStringEnumConverter<NormalizationCandidateKind>))]
public enum NormalizationCandidateKind { RetireDuplicate, ReconcileHistoricalPool }

public sealed record InventoryScope(int? WarehouseId, ImmutableArray<int> RoomIds,
    InventoryCustody Custody = InventoryCustody.Room, long? CustodyRecordId = null);

// Requirements can constrain eligibility; they cannot supply or override a quantity.
public sealed record InventoryOperationRequirements(bool RequireKnownTreatment = true,
    bool RequireExactReceipt = false, long? ReceiptId = null, string? TreatmentSignature = null,
    InventoryCustody AllowedCustody = InventoryCustody.Room, string? ExpectedFingerprint = null);

public sealed record InventoryIdentity(int? CropYear, int? GrowerLotId, int? FruitProfileId,
    string Lot, string? GrowerNumber, string Variety, string ProductionType, bool? IsOrganic, string Status)
{
    public string Key => string.Join('|', CropYear?.ToString() ?? "-", GrowerLotId?.ToString() ?? "-",
        FruitProfileId?.ToString() ?? "-", Normalize(GrowerNumber ?? Lot), Normalize(Lot), Normalize(Variety),
        Normalize(ProductionType), IsOrganic?.ToString() ?? "-", InventoryStatusIdentity.Normalize(Status, ProductionType));
    public bool IsComplete => CropYear is not null && GrowerLotId is not null && FruitProfileId is not null
        && !string.IsNullOrWhiteSpace(Lot) && !string.IsNullOrWhiteSpace(GrowerNumber)
        && !string.IsNullOrWhiteSpace(Variety) && !string.IsNullOrWhiteSpace(ProductionType) && IsOrganic is not null;
    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
}

public sealed record InventoryLocation(InventoryCustody Custody, int WarehouseId, int? RoomId,
    string Facility, string Name, long? CustodyRecordId = null);
public sealed record InventoryEvidenceReference(string Entity, string Id);
public sealed record InventoryVersion(string Entity, string Id, long? Version, DateTimeOffset? UpdatedAt);
public sealed record InventoryReadWatermark(string Fingerprint, string Consistency,
    ImmutableArray<InventoryVersion> Versions);
public sealed record InventoryBlocker(InventoryBlockerCode Code, string Detail);
public sealed record InventoryTreatmentSlice(string Signature, string State, int Quantity,
    InventoryConfidence Confidence, ImmutableArray<long> ProjectionIds, ImmutableArray<long> ApplicationIds,
    ImmutableArray<long> ReceiptEvidenceIds);
public sealed record HistoricalInventoryProjection(ImmutableArray<long> ProjectionIds, int ExcludedQuantity,
    ProjectionExclusionReason Reason, bool EvidenceProven, ImmutableArray<InventoryEvidenceReference> Evidence);
public sealed record InventoryNormalizationCandidate(NormalizationCandidateKind Kind,
    ImmutableArray<long> ProjectionIds, int ExcludedQuantity, int TargetPoolQuantity,
    bool ExactRowAllocationProven, ImmutableArray<InventoryEvidenceReference> Evidence);
public sealed record InventoryReceiptProvenance(InventoryConfidence Confidence, ImmutableArray<long> ReceiptIds,
    string Explanation);
public sealed record InventoryAvailabilityProof(string Algorithm, string EvidenceVersion,
    ImmutableArray<InventoryEvidenceReference> Evidence, ImmutableArray<InventoryNormalizationCandidate> Candidates,
    string ChronologyBasis, ImmutableArray<long> BackdatedLedgerIds)
{
    public bool NormalizationRequired => !Candidates.IsEmpty;
}
public sealed record InventoryAvailabilityResult(string PositionKey, InventoryIdentity Identity,
    InventoryLocation Location, DateTimeOffset? OccupiedSince, int AuthoritativeQuantity,
    int CommittedQuantity, int AvailableQuantity, int RawProjectionQuantity,
    InventoryConfidence QuantityConfidence, InventoryConfidence TreatmentConfidence,
    ImmutableArray<InventoryTreatmentSlice> TreatmentSlices, InventoryReceiptProvenance ReceiptProvenance,
    ImmutableArray<HistoricalInventoryProjection> HistoricalProjections, InventoryAvailabilityProof Proof,
    ImmutableArray<InventoryBlocker> Blockers, InventoryReadWatermark Watermark)
{
    public bool IsOperable => Blockers.IsEmpty && AuthoritativeQuantity >= 0;
    public int ProjectionExcess => Math.Max(0, RawProjectionQuantity - Math.Max(0, AuthoritativeQuantity));
}
public sealed record InventoryAvailabilityBatch(DateTimeOffset AsOf, string Algorithm,
    int EvidenceRowsLoaded, ImmutableArray<InventoryAvailabilityResult> Positions);

public interface IInventoryAvailability
{
    Task<InventoryAvailabilityBatch> ResolveAsync(InventoryScope scope, InventoryOperationRequirements requirements,
        DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

// Immutable evidence boundary: the resolver has no DbContext, entity or write API.
public sealed record InventoryLedgerEvidence(long Id, int Quantity, string Kind, DateTimeOffset At,
    long? ReceiptId, string? MovementParent, bool ExactIdentity, DateTimeOffset? RecordedAt = null);
public sealed record InventoryProjectionEvidence(long Id, string RawKey, int Quantity, string State,
    string Signature, long? ReceiptId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    long Version, bool ExactIdentity, ImmutableArray<long> ApplicationIds, string Disposition = "Current", int? RetiredQuantity = null);
public sealed record InventoryMovementEvidence(long Id, string Kind, int Quantity, DateTimeOffset At,
    DateTimeOffset CreatedAt, string Signature, string State, long? ReceiptId, long? SourceProjectionId,
    long? DestinationProjectionId, bool Incoming, bool Outgoing, string? Parent, long? ReversesId, bool ExactIdentity);
public sealed record InventoryReceiptEvidence(long Id, int Quantity, bool ExactIdentity, bool IsDeleted,
    bool IsTransferReceipt, DateTimeOffset UpdatedAt, long Version);
public sealed record InventoryApplicationEvidence(long Id, DateTimeOffset AppliedAt, DateTimeOffset? ReversedAt,
    long? ReceiptId);
public sealed record InventoryPositionEvidence(InventoryIdentity Identity, InventoryLocation Location,
    int AuthoritativeQuantity, int CommittedQuantity, bool IdentityVerified, bool CustodyVerified,
    ImmutableArray<InventoryLedgerEvidence> Ledger, ImmutableArray<InventoryProjectionEvidence> Projections,
    ImmutableArray<InventoryMovementEvidence> Movements, ImmutableArray<InventoryReceiptEvidence> Receipts,
    ImmutableArray<InventoryApplicationEvidence> Applications, InventoryReadWatermark Watermark,
    bool HistoricalSnapshotUnavailable = false);
public sealed record InventoryEvidenceBatch(ImmutableArray<InventoryPositionEvidence> Positions, int RowsLoaded);
public interface IInventoryEvidenceLoader
{
    Task<InventoryEvidenceBatch> LoadAsync(InventoryScope scope, DateTimeOffset asOf, CancellationToken cancellationToken);
}
