using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class BusinessRuleContractTests
{
    [InventoryPostgresFact]
    [Trait("BusinessRule", "GOV-001")]
    public async Task Mandatory_provider_is_local_disposable_PostgreSql18()
    {
        var settings = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES"));
        Assert.Contains(settings.Host, new[] { "localhost", "127.0.0.1", "::1" });
        CropQc.Data.ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(settings.ConnectionString);
        await using var connection = new Npgsql.NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(18, connection.PostgreSqlVersion.Major);
    }

    [Fact]
    [Trait("BusinessRule", "INV-001")]
    public void INV001_missing_origin_cannot_become_available_stock()
    {
        var evidence = InventoryEvidenceCorpus.Basic(19, 29) with { Ledger = [], Receipts = [], Movements = [] };
        var before = JsonSerializer.Serialize(evidence);
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        Assert.False(result.IsOperable);
        Assert.Equal(0, result.AvailableQuantity);
        Assert.NotEmpty(result.Blockers);
        Assert.Equal(before, JsonSerializer.Serialize(evidence));
    }

    [Fact]
    [Trait("BusinessRule", "INV-004")]
    [Trait("BusinessRule", "INV-006")]
    public void INV004_shared_projection_excess_cannot_invent_quantity_or_receipt_ancestry()
    {
        var evidence = InventoryEvidenceCorpus.Wp7();
        var before = JsonSerializer.Serialize(evidence);
        var ordinary = InventoryAvailabilityResolver.Resolve(evidence, new());
        Assert.True(ordinary.IsOperable);
        Assert.Equal(1122, ordinary.AuthoritativeQuantity);
        Assert.Equal(1122, ordinary.AvailableQuantity);
        Assert.Equal(446, ordinary.ProjectionExcess);
        Assert.NotEqual(InventoryConfidence.Proven, ordinary.ReceiptProvenance.Confidence);
        var exact = InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true, ReceiptId: 905));
        Assert.False(exact.IsOperable);
        Assert.Equal(1122, exact.AuthoritativeQuantity);
        Assert.Contains(exact.Blockers, x => x.Code == InventoryBlockerCode.MissingReceiptProvenance);
        Assert.Equal(before, JsonSerializer.Serialize(evidence));
    }

    [InventoryPostgresFact]
    [Trait("BusinessRule", "INV-003")]
    [Trait("BusinessRule", "AUD-001")]
    public async Task INV003_move_has_equal_ledger_legs_and_preserves_original_evidence()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var originalLedger = await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var originalAudits = await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var ledgerIds = originalLedger.Select(x => x.Id).ToArray();
        var auditIds = originalAudits.Select(x => x.Id).ToArray();
        var intent = await f.Command(InventoryCommandKind.RoomMove, 7);
        var result = await f.Execute(intent);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(12, await f.Physical());
        Assert.Equal(7, await f.Physical(9003));
        var added = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => !ledgerIds.Contains(x.Id)).ToListAsync();
        Assert.Equal(new[] { -7, 7 }, added.Select(x => x.ChangeAmount).Order().ToArray());
        Assert.Equal(0, added.Sum(x => x.ChangeAmount));
        Assert.All(added, x => Assert.NotNull(x.RoomTransferId));
        Assert.Single(added.Select(x => x.RoomTransferId).Distinct());
        Assert.Equal(JsonSerializer.Serialize(originalLedger),
            JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().Where(x => ledgerIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync()));
        Assert.Equal(JsonSerializer.Serialize(originalAudits),
            JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().Where(x => auditIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync()));
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(intent)).Status);
        Assert.Equal(committed, await f.Snapshot());
    }

    [InventoryPostgresFact]
    [Trait("BusinessRule", "INV-002")]
    [Trait("BusinessRule", "MOV-001")]
    public async Task INV002_partial_transit_return_conserves_each_container_and_replay()
    {
        // Existing main contract: partial RETURN, not #271's pending acknowledgement.
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        var id = receiving.Lines[0].Source.Location.CustodyRecordId!.Value;
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = CanonicalTruckReceiptWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
        var original = await db.TreatmentLineageMovements.AsNoTracking()
            .SingleAsync(x => x.InterCrewTransferId == id && x.MovementType == "InterCrewDispatch");
        var form = new TransitEditForm
        {
            TransferId = id,
            TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.ConcurrencyVersion).SingleAsync(),
            DispatchMovementId = original.Id,
            Bins = 5,
            Reason = "Disposable conservation contract"
        };
        Assert.Null(await service.EditTransferAsync(form, default));
        var transit = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.InTransit, id), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Equal(5, await f.Physical());
        Assert.Equal(14, transit.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.All(transit.Positions, x => Assert.True(x.IsOperable));
        Assert.Equal(19, await f.Physical() + transit.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(0, (await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(
            await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.Id == original.Id)));
        var committed = await f.Snapshot();
        Assert.Null(await service.EditTransferAsync(form, default));
        Assert.Equal(committed, await f.Snapshot());
    }
}
