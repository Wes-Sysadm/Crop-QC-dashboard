using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Data.Inventory;

// Explicit maintenance API on the existing canonical transaction owner. No ordinary
// route calls it. Approval, dry run and execution are distinct operations.
public sealed partial class InventoryCommandExecutor
{
    private const string ReconstructionEntity = "ProjectionReconstruction";
    private const string ReconstructionSource = "CanonicalProjectionReconstruction/v1";
    private static readonly JsonSerializerOptions ReconstructionJson = new(JsonSerializerDefaults.Web)
    { ReferenceHandler = ReferenceHandler.IgnoreCycles };
    private static readonly string[] ReconstructionProtectedTables =
    ["Rooms", "Warehouses", "GrowerLots", "FruitProfiles", "Receipts", "ReceiptVarietyLines", "RoomInventoryAdjustments", "RoomTransfers", "InterCrewTransfers", "OutsideWarehouseTransfers",
     "BinsRunEntries", "ActualRuns", "ActualRunRevisions", "RoomInventoryLosses", "ReceiptInventoryOverrides",
     "InventoryIdentityCorrections", "RoomTreatmentApplications", "RoomTreatmentApplicationSources",
     "TreatmentLineageMovements", "TreatmentLineageSegmentApplications", "RoomDepletions", "ProcessorShipments", "ProcessorShipmentLines", "InventoryCommands", "AuditLogs"];

    public async Task<ProjectionReconstructionPreview> PreviewProjectionReconstructionAsync(
        ProjectionReconstructionTarget target, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        Require(db.Database.IsNpgsql(), "Projection reconstruction requires PostgreSQL.");
        // Use the same evidence isolation label as approval/execution so the exact
        // preview comparison remains stable without omitting any evidence fields.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        return await ReadReconstructionAsync(db, target, ct);
    }

    public async Task<long> ApproveProjectionReconstructionAsync(ProjectionReconstructionApprovalRequest request,
        bool confirmDisposableRestore = false, CancellationToken ct = default)
    {
        Require(request.ExplicitlyApprove && request.Preview.Eligible && ValidRepairKey(request.ApprovalKey)
            && !string.IsNullOrWhiteSpace(request.ApprovalReference) && !string.IsNullOrWhiteSpace(request.Reason),
            "Explicit approval of a complete eligible preview and external approval reference is required.");
        await using var db = await contexts.CreateDbContextAsync(ct);
        Require(db.Database.IsNpgsql() && db.CanonicalInventoryEnabled, "Strict canonical PostgreSQL mode is required.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockReconstructionAsync(db, ct);
        await AuthorizeReconstructionActorAsync(db, request.ApproverId, ct);
        await VerifyReconstructionBackupAsync(db, request, confirmDisposableRestore, ct);
        var payload = JsonSerializer.Serialize(request, ReconstructionJson);
        var previous = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.EntityName == ReconstructionEntity
            && x.Action == "Approve" && x.EntityKey == request.ApprovalKey && x.SourceApplication == ReconstructionSource, ct);
        if (previous != null)
        {
            Require(previous.AfterValuesJson == payload, "Approval key already binds different evidence.");
            return previous.Id;
        }
        var current = await ReadReconstructionAsync(db, request.Preview.Target, ct);
        Require(current.Eligible && SamePreview(current, request.Preview), "Approval preview is stale or unsupported.");
        var audit = ReconstructionAudit(request.ApproverId, "Approve", request.ApprovalKey, null, payload);
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return audit.Id;
    }

    public async Task<ProjectionReconstructionResult> ReconstructProjectionAsync(ProjectionReconstructionRequest request,
        bool confirmDisposableRestore = false, CancellationToken ct = default)
    {
        if (!request.ExplicitlyExecute || !ValidRepairKey(request.OperationKey) || request.OperatorId <= 0 || !request.Preview.Eligible)
            return new("Blocked", request.OperationKey, "Explicit operator execution, approval and an eligible exact preview are required.");
        var intent = JsonSerializer.Serialize(request, ReconstructionJson);
        var hash = RepairHash(intent);
        await using var db = await contexts.CreateDbContextAsync(ct);
        Require(db.Database.IsNpgsql() && db.CanonicalInventoryEnabled, "Strict canonical PostgreSQL mode is required.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var commitAttempted = false;
        try
        {
            await LockReconstructionAsync(db, ct);
            await AuthorizeReconstructionActorAsync(db, request.OperatorId, ct);
            var prior = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == request.OperationKey, ct);
            if (prior != null)
            {
                Require(prior.IntentHash == hash && prior.IntentJson == intent, "Operation key conflicts with previously committed intent.");
                return JsonSerializer.Deserialize<ProjectionReconstructionResult>(prior.ResultJson, ReconstructionJson)! with { Status = "Replayed" };
            }
            var approvalRow = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ApprovalAuditId
                && x.EntityName == ReconstructionEntity && x.Action == "Approve" && x.SourceApplication == ReconstructionSource, ct);
            Require(approvalRow != null, "Separate immutable approval record is missing.");
            var approval = JsonSerializer.Deserialize<ProjectionReconstructionApprovalRequest>(approvalRow!.AfterValuesJson!, ReconstructionJson)!;
            Require(approval.ExplicitlyApprove && approvalRow.UserId == approval.ApproverId
                && SamePreview(approval.Preview, request.Preview), "Approval does not bind this exact target and evidence.");
            await AuthorizeReconstructionActorAsync(db, approval.ApproverId, ct);
            await VerifyReconstructionBackupAsync(db, approval, confirmDisposableRestore, ct);
            var before = await ReadReconstructionAsync(db, request.Preview.Target, ct);
            Require(before.Eligible && SamePreview(before, request.Preview), "Evidence changed; obtain a fresh preview and approval.");
            await Stage("ReconstructionValidated", db, 1, ct);
            db.CanonicalCommandTransaction = true;
            var ids = before.Plan!.Changes.Select(x => x.Id).ToArray();
            var rows = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
            Require(rows.Count == ids.Length, "A targeted projection disappeared.");
            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows) CanonicalProjectionFactory.Retire(row, request.OperationKey, now);
            await db.SaveChangesAsync(ct);
            await Stage("ReconstructionRetired", db, 1, ct);
            var target = before.Target;
            TreatmentLineageSegment replacement;
            if (before.Plan.ReplacementProjectionId is long preservedId)
                replacement = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == preservedId, ct);
            else
            {
                replacement = await new CanonicalProjectionFactory(db).CurrentAsync(target.Identity, target.WarehouseId,
                    target.RoomId, "u", "Untreated", null, [], now, ct);
                Require(replacement.CurrentBins == 0 && replacement.ReceiptId == null, "Replacement is not an empty shared projection.");
                replacement.CurrentBins = before.Plan.ReplacementQuantity;
                replacement.ConcurrencyVersion = 1;
            }
            await db.SaveChangesAsync(ct);
            await Stage("ReconstructionReplaced", db, 1, ct);

            // Independently reload persisted authority and evidence before commit.
            var after = await ReadReconstructionAsync(db, target, ct);
            Require(after.AuthoritativeQuantity == before.AuthoritativeQuantity && after.ProjectedQuantity == before.AuthoritativeQuantity
                && after.MovementFingerprint == before.MovementFingerprint && after.AuditFingerprint == before.AuditFingerprint,
                "Post-operation quantity, movement or audit conservation failed.");
            var excludedIds = before.Plan.ReplacementProjectionId == null ? ids.Append(replacement.Id).ToArray() : ids;
            var protectedAfter = await ReconstructionProtectedHashAsync(db, excludedIds, ct);
            Require(protectedAfter == before.ProtectedFingerprint, "Protected or unrelated records changed.");
            var postEvidence = (await new InventoryEvidenceLoader(db).LoadAsync(new(target.WarehouseId, [target.RoomId]), DateTimeOffset.UtcNow, ct))
                .Positions.Single(x => x.Identity.Key == target.Identity.Key);
            var post = InventoryAvailabilityResolver.Resolve(postEvidence, new());
            Require(post.IsOperable && post.TreatmentConfidence == InventoryConfidence.Proven
                && post.TreatmentSlices.All(x => x.Signature == "u" && x.State == "Untreated" && x.ApplicationIds.IsEmpty)
                && post.RawProjectionQuantity == before.AuthoritativeQuantity,
                "Post-operation untreated shared-pool proof failed.");
            var committedEvidence = new ReconstructionCommitEvidence(postEvidence,
                await CaptureReconstructionBoundariesAsync(db, ct), protectedAfter, after.BeforeSegmentsJson);
            var verificationSeal = RepairHash(JsonSerializer.Serialize(committedEvidence, ReconstructionJson));
            var audit = ReconstructionAudit(request.OperatorId, "Reconstruct", request.OperationKey, before.BeforeSegmentsJson,
                JsonSerializer.Serialize(new
                {
                    request.ApprovalAuditId,
                    before,
                    after = after.BeforeSegmentsJson,
                    replacement.Id,
                    receiptId = replacement.ReceiptId,
                    authoritativeDelta = 0,
                    protectedAfter,
                    committedEvidence
                }, ReconstructionJson));
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync(ct);
            var result = new ProjectionReconstructionResult("Committed", request.OperationKey,
                "Historical projections superseded; authoritative inventory and treatment evidence preserved.", replacement.Id, audit.Id,
                VerificationSeal: verificationSeal);
            db.InventoryCommands.Add(new()
            {
                OperationKey = request.OperationKey,
                ActorId = request.OperatorId,
                IntentHash = hash,
                IntentJson = intent,
                ResultJson = JsonSerializer.Serialize(result, ReconstructionJson),
                CommittedAt = now
            });
            await db.SaveChangesAsync(ct);
            await Stage("ReconstructionBeforeCommit", db, 1, ct);
            commitAttempted = true;
            await tx.CommitAsync(ct);
            await Stage("ReconstructionAfterCommit", db, 1, ct);
            return result;
        }
        catch (Exception ex)
        {
            if (!commitAttempted)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { /* Disposing the connection also aborts. */ }
                return new(ex is PostgresException { SqlState: "55P03" or "40001" or "40P01" } ? "Conflict" : "Blocked",
                    request.OperationKey, ex is Rejection ? ex.Message : "Reconstruction aborted; no repair committed. Refresh evidence before retrying.");
            }
            // Never describe an uncertain commit as rolled back. The persisted command
            // is the authority; retry must use the identical operation key and payload.
            try
            {
                await using var verify = await contexts.CreateDbContextAsync(CancellationToken.None);
                var committed = await verify.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == request.OperationKey);
                if (committed?.IntentHash == hash && committed.IntentJson == intent)
                    return JsonSerializer.Deserialize<ProjectionReconstructionResult>(committed.ResultJson, ReconstructionJson)! with { Status = "Replayed" };
            }
            catch { /* Preserve unknown outcome if independent verification is unavailable. */ }
            return new("OutcomeUnknown", request.OperationKey, "Verify the persisted command; retry only this identical operation key and intent.");
        }
    }

    public async Task<ProjectionReconstructionResult> VerifyProjectionReconstructionAsync(string operationKey, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        Require(db.Database.IsNpgsql(), "Independent verification requires PostgreSQL.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var command = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operationKey, ct);
        Require(command != null && command.IntentHash == RepairHash(command.IntentJson), "Persisted repair command is missing or inconsistent.");
        var request = JsonSerializer.Deserialize<ProjectionReconstructionRequest>(command!.IntentJson, ReconstructionJson)!;
        var result = JsonSerializer.Deserialize<ProjectionReconstructionResult>(command.ResultJson, ReconstructionJson)!;
        Require(request.Preview?.Plan != null && result.ReplacementSegmentId != null, "Operation is not a projection reconstruction.");
        Require(command.ActorId == request.OperatorId && command.OperationKey == request.OperationKey, "Execution journal identity is inconsistent.");
        var approvalRow = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ApprovalAuditId, ct);
        Require(approvalRow != null && approvalRow.EntityName == ReconstructionEntity && approvalRow.Action == "Approve"
            && approvalRow.SourceApplication == ReconstructionSource && approvalRow.BeforeValuesJson == null,
            "Independent approval audit is missing or inconsistent.");
        var approval = JsonSerializer.Deserialize<ProjectionReconstructionApprovalRequest>(approvalRow!.AfterValuesJson!, ReconstructionJson)!;
        Require(approval.ExplicitlyApprove && approval.ApprovalKey == approvalRow.EntityKey && approval.ApproverId == approvalRow.UserId
            && !string.IsNullOrWhiteSpace(approval.ApprovalReference) && !string.IsNullOrWhiteSpace(approval.Reason)
            && SamePreview(approval.Preview, request.Preview!), "Approval does not bind the immutable committed intent.");
        var audit = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == result.RepairAuditId, ct);
        Require(audit != null && audit.EntityName == ReconstructionEntity && audit.Action == "Reconstruct"
            && audit.EntityKey == operationKey && audit.UserId == request.OperatorId && audit.SourceApplication == ReconstructionSource
            && audit.BeforeValuesJson == request.Preview!.BeforeSegmentsJson, "Repair audit is missing or inconsistent.");
        var ids = request.Preview!.Plan!.Changes.Select(x => x.Id).ToArray();
        var rows = await db.TreatmentLineageSegments.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        Require(rows.Count == ids.Length && rows.All(x => x.Disposition == "Historical" && x.CurrentBins == 0
            && x.RetiredByCommandKey == operationKey && request.Preview.Plan.Changes.Any(c => c.Id == x.Id
                && c.BeforeQuantity == x.RetiredQuantity && c.AfterVersion == x.ConcurrencyVersion && c.ReceiptId == x.ReceiptId
                && c.RawIdentityKey == x.IdentityKey && c.Signature == x.TreatmentSignature)), "Historical supersession evidence changed.");
        using var originalRows = JsonDocument.Parse(request.Preview.BeforeSegmentsJson);
        string[] retirementFields = ["currentBins", "disposition", "retiredAt", "retiredQuantity", "retiredByCommandKey", "concurrencyVersion", "updatedAt"];
        foreach (var row in rows)
        {
            var original = originalRows.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetInt64() == row.Id);
            var actual = JsonSerializer.SerializeToElement(row, ReconstructionJson);
            Require(row.RetiredAt != null && row.RetiredAt == row.UpdatedAt
                && original.EnumerateObject().Where(x => !retirementFields.Contains(x.Name))
                    .All(x => x.Value.GetRawText() == actual.GetProperty(x.Name).GetRawText()), "Historical projection metadata changed.");
        }
        using var auditAfter = JsonDocument.Parse(audit!.AfterValuesJson!);
        Require(auditAfter.RootElement.GetProperty("approvalAuditId").GetInt64() == request.ApprovalAuditId
            && auditAfter.RootElement.GetProperty("authoritativeDelta").GetInt32() == 0
            && auditAfter.RootElement.GetProperty("protectedAfter").GetString() == request.Preview.ProtectedFingerprint
            && SamePreview(auditAfter.RootElement.GetProperty("before").Deserialize<ProjectionReconstructionPreview>(ReconstructionJson)!, request.Preview),
            "Repair audit does not match the approved evidence.");
        Require(auditAfter.RootElement.GetProperty("id").GetInt64() == result.ReplacementSegmentId
            && auditAfter.RootElement.GetProperty("receiptId").Deserialize<long?>() == request.Preview.Plan.ReplacementReceiptId
            && auditAfter.RootElement.TryGetProperty("committedEvidence", out _), "Committed verification evidence is missing or inconsistent.");
        var committed = auditAfter.RootElement.GetProperty("committedEvidence").Deserialize<ReconstructionCommitEvidence>(ReconstructionJson)!;
        Require(auditAfter.RootElement.GetProperty("after").GetString() == committed.CurrentRowsJson,
            "Execution audit after-state differs from sealed commit evidence.");
        VerifyCommittedReconstruction(request, result, committed);
        return await VerifyCurrentReconstructionAsync(db, request, result, committed, ct);
    }

    private static bool ValidRepairKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 60;
    private static bool SamePreview(ProjectionReconstructionPreview a, ProjectionReconstructionPreview b) =>
        JsonSerializer.Serialize(a with { SnapshotCapturedAt = null }, ReconstructionJson) == JsonSerializer.Serialize(b with { SnapshotCapturedAt = null }, ReconstructionJson);
    private static string RepairHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static AuditLog ReconstructionAudit(int actor, string action, string key, string? before, string after) => new()
    {
        UserId = actor,
        Action = action,
        EntityName = ReconstructionEntity,
        EntityKey = key,
        BeforeValuesJson = before,
        AfterValuesJson = after,
        SourceApplication = ReconstructionSource,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task AuthorizeReconstructionActorAsync(CropQcDbContext db, int actor, CancellationToken ct) =>
        Require(await db.Users.AnyAsync(x => x.Id == actor && x.IsActive
            && x.UserRoles.Any(r => r.Role.IsActive && r.Role.Name == BuiltInRoleNames.Admin), ct),
            "An active authorized maintenance administrator is required in addition to explicit approval and proof.");

    private static async Task VerifyReconstructionBackupAsync(CropQcDbContext db, ProjectionReconstructionApprovalRequest request,
        bool disposable, CancellationToken ct)
    {
        if (disposable)
        {
            var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            Require(connection.Host is "127.0.0.1" or "localhost" && connection.Database != null
                && (connection.Database.StartsWith("pr271_release_") || connection.Database.StartsWith("cropqc_test_")),
                "Disposable confirmation is accepted only for isolated local test databases.");
            return;
        }
        Require(!string.IsNullOrWhiteSpace(request.IndependentBackupVerificationReference), "Independent backup verification reference is required.");
        var backup = await db.BackupRunRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.VerifiedBackupRunId, ct);
        Require(backup != null && backup.BackupType == BackupRunTypes.PreDeployment
            && backup.Status == "Succeeded" && backup.VerifiedAt != null && backup.PrunedAt == null
            && backup.Sha256 == request.VerifiedBackupSha256 && !string.IsNullOrWhiteSpace(backup.PackageStorageKey)
            && backup.CompletedAt >= DateTimeOffset.UtcNow.AddHours(-24), "A current verified retained recovery point is required.");
    }

    private static async Task LockReconstructionAsync(CropQcDbContext db, CancellationToken ct)
    {
        // Maintenance only: NOWAIT never queues a repair behind live writers. Table
        // locks also cover insert phantoms and legacy/non-Serializable writers.
        var tables = ReconstructionProtectedTables.Append("TreatmentLineageSegments").Order();
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE " + string.Join(',', tables.Select(x => '"' + x + '"'))
            + " IN SHARE ROW EXCLUSIVE MODE NOWAIT", ct);
    }

    private static async Task<string> TableFingerprintAsync(CropQcDbContext db, string table, string predicate, CancellationToken ct, params object[] parameters) =>
        await db.Database.SqlQueryRaw<string>("SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) AS \"Value\" FROM \""
            + table + "\" t " + predicate, parameters).SingleAsync(ct);

    private static async Task<string> ReconstructionProtectedHashAsync(CropQcDbContext db, long[] excluded, CancellationToken ct, string? completedKey = null)
    {
        var hashes = new SortedDictionary<string, string>();
        foreach (var table in ReconstructionProtectedTables)
            hashes[table] = table == "InventoryCommands" && completedKey != null
                ? await TableFingerprintAsync(db, table, "WHERE \"OperationKey\" <> {0}", ct, completedKey)
                : await TableFingerprintAsync(db, table,
                    table == "AuditLogs" ? "WHERE NOT (\"EntityName\"='ProjectionReconstruction' AND \"SourceApplication\"='CanonicalProjectionReconstruction/v1')" : "", ct);
        hashes["UnrelatedSegments"] = await TableFingerprintAsync(db, "TreatmentLineageSegments",
            excluded.Length == 0 ? "" : "WHERE \"Id\" NOT IN (" + string.Join(',', excluded) + ")", ct);
        return RepairHash(JsonSerializer.Serialize(hashes));
    }

    // Classification is diagnostic only. Only a fresh database-bound preview,
    // separate approval and the serializable execution path can authorize writes.
    public static (string Classification, ImmutableArray<string> Blockers, InventoryNormalizationPlan? Plan)
        AssessProjectionReconstruction(InventoryPositionEvidence evidence)
    {
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        var blockers = new List<string>();
        InventoryNormalizationPlan? plan = null;
        var category = result.AuthoritativeQuantity < 0 ? "AuthoritativeInventoryProblem" : "AdditionalReconciliationEvidence";
        try
        {
            Require(result.AuthoritativeQuantity > 0 && result.RawProjectionQuantity > result.AuthoritativeQuantity,
                "Only positive occupied pools with proven historical excess are eligible; depleted and negative positions remain excluded.");
            foreach (var debit in evidence!.Ledger.Where(x => x.Quantity < 0))
            {
                var moves = evidence.Movements.Where(x => x.Outgoing && x.Parent == debit.MovementParent).ToArray();
                Require(debit.MovementParent != null && moves.Length > 0 && moves.All(x => x.ExactIdentity
                    && x.Quantity > 0 && x.State == "Untreated" && x.Signature == "u")
                    && moves.Sum(x => x.Quantity) == -debit.Quantity,
                    $"Ledger debit {debit.Id} lacks complete exact untreated movement evidence.");
            }
            plan = InventoryNormalizationPlanner.Plan(evidence!, result);
            Require(plan != null && !plan.Changes.IsEmpty && plan.Changes.Select(x => x.RawIdentityKey).Distinct().Count() > 1,
                "A complete historical status-alias supersession plan is required.");
            plan = plan! with { ReplacementReceiptId = null };
            category = "ProvenReconstructionCandidate";
        }
        catch (InvalidOperationException ex) { blockers.Add(ex.Message); }
        catch (Rejection ex) { blockers.Add(ex.Message); }
        return (category, blockers.ToImmutableArray(), plan);
    }

    private static async Task<ProjectionReconstructionPreview> ReadReconstructionAsync(CropQcDbContext db,
        ProjectionReconstructionTarget target, CancellationToken ct)
    {
        Require(target.Identity.IsComplete && target.RoomId > 0 && target.WarehouseId > 0, "Exact complete room identity required.");
        var capturedAt = DateTimeOffset.UtcNow;
        var batch = await new InventoryEvidenceLoader(db).LoadAsync(new(target.WarehouseId, [target.RoomId]), capturedAt, ct);
        var evidence = batch.Positions.SingleOrDefault(x => x.Identity.Key == target.Identity.Key);
        Require(evidence != null, "Target identity is absent.");
        var result = InventoryAvailabilityResolver.Resolve(evidence!, new());
        var canonical = await ReconstructionCommandEvidence.ValidateAsync(db, evidence!, ct);
        var known = await SelectiveProjectionReconstruction.PreservedCanonicalPopulationsAsync(db, evidence!, canonical, ct);
        var (category, blockers, plan) = AssessProjectionReconstruction(evidence!);
        var represented = SelectiveProjectionReconstruction.FullyRepresented(evidence!, known);
        if (represented != null) { category = "ProvenReconstructionCandidate"; blockers = []; plan = represented; }
        RecordedHistoryAssessment? recordedHistory = null;
        if (plan == null && result.AuthoritativeQuantity > 0 && result.RawProjectionQuantity > result.AuthoritativeQuantity)
        {
            var history = await RecordedInventoryHistoryReconstruction.LoadAsync(db, evidence!, ct);
            recordedHistory = RecordedInventoryHistoryReconstruction.Assess(evidence!, history);
            blockers = recordedHistory.Blockers;
            if (recordedHistory.Proven)
            {
                plan = recordedHistory.Plan;
                category = "ProvenReconstructionCandidate";
            }
        }
        if (plan != null)
        {
            var selective = SelectiveProjectionReconstruction.Refine(evidence!, plan, known);
            plan = selective.Plan;
            if (plan == null) { category = "AdditionalReconciliationEvidence"; blockers = selective.Blockers; }
        }
        if (!canonical.InvalidCommands.IsEmpty)
        {
            plan = null; category = "AdditionalReconciliationEvidence";
            blockers = blockers.AddRange(canonical.InvalidCommands.Select(x => $"Canonical command {x}: journal, parent, ledger or movement effects are missing, contradictory or unsupported."));
        }
        var custody = ImmutableArray.CreateBuilder<InventoryPositionEvidence>();
        foreach (var kind in new[] { InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor })
        {
            var other = await new InventoryEvidenceLoader(db).LoadAsync(new(target.WarehouseId, [target.RoomId], kind), DateTimeOffset.UtcNow, ct);
            custody.AddRange(other.Positions.Where(x => x.Identity.Key == target.Identity.Key));
        }
        if (custody.Any(x => !InventoryAvailabilityResolver.Resolve(x, new(AllowedCustody: x.Location.Custody)).IsOperable))
        {
            plan = null; category = "AdditionalReconciliationEvidence";
            blockers = blockers.AddRange(custody.Select(x => new { Evidence = x, Result = InventoryAvailabilityResolver.Resolve(x, new(AllowedCustody: x.Location.Custody)) })
                .Where(x => !x.Result.IsOperable).Select(x => $"{x.Evidence.Location.Custody} parent {x.Evidence.Location.CustodyRecordId}: "
                    + string.Join("; ", x.Result.Blockers.Select(b => $"{b.Code}: {b.Detail}"))));
        }
        var unsupportedReceipts = await db.InterCrewTransfers.AsNoTracking().Where(x => x.CropYear == target.Identity.CropYear
            && x.GrowerLotId == target.Identity.GrowerLotId && x.FruitProfileId == target.Identity.FruitProfileId
            && (x.SourceRoomId == target.RoomId || x.DestinationRoomId == target.RoomId)
            && (x.Status != InterCrewTransferStatuses.InTransit && x.Status != InterCrewTransferStatuses.Received
                && x.Status != InterCrewTransferStatuses.Reversed || x.Status == InterCrewTransferStatuses.InTransit && x.BinsReceived != null
                || x.Status == InterCrewTransferStatuses.Received && x.BinsReceived != x.BinsLoaded)).ToArrayAsync(ct);
        if (unsupportedReceipts.Length > 0)
        {
            plan = null; category = "AdditionalReconciliationEvidence";
            blockers = blockers.AddRange(unsupportedReceipts.Select(x => $"InterCrewTransfer {x.Id}: status {x.Status}, dispatched {x.BinsLoaded}, acknowledged {x.BinsReceived}; this branch cannot prove partial/held settlement allocations. Acknowledgement is not room placement."));
        }
        var ids = plan?.Changes.Select(x => x.Id).Order().ToArray()
            ?? evidence!.Projections.Where(x => x.Disposition == "Current" && x.Quantity > 0).Select(x => x.Id).Order().ToArray();
        var rows = await db.TreatmentLineageSegments.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
        var beforeJson = JsonSerializer.Serialize(rows, ReconstructionJson);
        var movementHash = await TableFingerprintAsync(db, "TreatmentLineageMovements", "", ct);
        var auditHash = await TableFingerprintAsync(db, "AuditLogs",
            "WHERE NOT (\"EntityName\"='ProjectionReconstruction' AND \"SourceApplication\"='CanonicalProjectionReconstruction/v1')", ct);
        var protectedHash = await ReconstructionProtectedHashAsync(db, ids, ct);
        var revisions = await ReceiptRevisionEvidenceValidator.EvaluateAsync(db, evidence!, ct);
        if (recordedHistory?.Proven == true)
            revisions = revisions.Select(x => x with
            {
                UntreatedPoolProven = true,
                ExactRemainingAllocationProven = false,
                MissingEvidence = ["Exact surviving receipt allocation remains unresolved; preserve the shared pool. Correction reasons do not assign bins."]
            }).ToImmutableArray();
        var fingerprint = RepairHash(JsonSerializer.Serialize(new { evidence, beforeJson, movementHash, auditHash, protectedHash, custody }, ReconstructionJson));
        return new(target, category, blockers.ToImmutableArray(), result.AuthoritativeQuantity, result.RawProjectionQuantity,
            "u", plan, fingerprint, movementHash, auditHash, protectedHash, beforeJson, revisions, evidence, custody.ToImmutable(), capturedAt);
    }
}
