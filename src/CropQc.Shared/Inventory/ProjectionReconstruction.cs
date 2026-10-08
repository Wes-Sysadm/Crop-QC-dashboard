using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

public sealed record ProjectionReconstructionTarget(int WarehouseId, int RoomId, InventoryIdentity Identity);
public sealed record ProjectionReconstructionPreview(
    ProjectionReconstructionTarget Target, string Classification, ImmutableArray<string> Blockers,
    int AuthoritativeQuantity, int ProjectedQuantity, string ExpectedTreatmentSignature,
    InventoryNormalizationPlan? Plan, string Fingerprint, string MovementFingerprint, string AuditFingerprint,
    string ProtectedFingerprint, string BeforeSegmentsJson, ImmutableArray<ReceiptRevisionAssessment> ReceiptRevisions,
    InventoryPositionEvidence? Snapshot = null, ImmutableArray<InventoryPositionEvidence> OtherCustody = default,
    DateTimeOffset? SnapshotCapturedAt = null)
{
    public bool Eligible => Classification == "ProvenReconstructionCandidate" && Blockers.IsEmpty && Plan != null;
}
public sealed record ProjectionReconstructionApprovalRequest(string ApprovalKey, int ApproverId, string ApprovalReference,
    string Reason, bool ExplicitlyApprove, ProjectionReconstructionPreview Preview,
    long? VerifiedBackupRunId = null, string? VerifiedBackupSha256 = null, string? IndependentBackupVerificationReference = null);
public sealed record ProjectionReconstructionRequest(string OperationKey, long ApprovalAuditId, int OperatorId,
    bool ExplicitlyExecute, ProjectionReconstructionPreview Preview);
public sealed record ProjectionReconstructionResult(string Status, string OperationKey, string Detail,
    long? ReplacementSegmentId = null, long? RepairAuditId = null, int AuthoritativeQuantityDelta = 0,
    string? VerificationSeal = null);
public sealed record ReceiptRevisionAssessment(long ReceiptId, bool CorrectionChainValid,
    bool ExactRemainingAllocationProven, bool UntreatedPoolProven, int OriginalQuantity, int CurrentQuantity,
    ImmutableArray<string> MissingEvidence, ImmutableArray<string> CorrectionIds, ImmutableArray<long> AuditIds, bool IsDeleted = false)
{
    public int EffectiveReceiptQuantity => IsDeleted ? 0 : CurrentQuantity;
}
