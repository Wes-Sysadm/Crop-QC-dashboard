using System.Text.Json;
using System.Collections.Immutable;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class InventoryMovementEvidenceTests
{
    [Fact]
    public void Corrected_or_voided_receipt_metadata_does_not_erase_committed_ledger_effects()
    {
        var source = ReadObservation().Single(x => x.Location.RoomId == 55 && x.Identity.Lot == "1242");
        var origin = Assert.Single(source.Ledger);
        var receipt = Assert.Single(source.Receipts);
        var correction = origin with
        {
            Id = 900001,
            Quantity = -10,
            Kind = "ReceiptAdminOverride",
            At = origin.At.AddDays(1),
            RecordedAt = origin.At.AddDays(1)
        };
        var result = InventoryEventReplay.Replay(source with
        {
            AuthoritativeQuantity = 24,
            Ledger = [origin, correction],
            Receipts = [receipt with { Quantity = 0, IsDeleted = true,
                OriginalQuantity = 34, OriginalIdentityVerified = true }],
            Projections = []
        });
        Assert.True(result.QuantityConserved);
        Assert.Equal(24, Assert.Single(result.Cohorts).Quantity);
        Assert.Equal("Untreated", Assert.Single(result.Cohorts).State);
        Assert.Empty(result.UnresolvedEvents);
    }

    [Fact]
    public void Backdated_arrival_crossing_a_recorded_treatment_names_the_conflicting_events()
    {
        var source = ReadObservation().Single(x => x.Location.RoomId == 55 && x.Identity.Lot == "1242");
        var origin = Assert.Single(source.Ledger);
        var application = new InventoryApplicationEvidence(123456, origin.At.AddMinutes(1), null, null, 55)
        { RecordedAt = origin.At.AddMinutes(1), Allocations = [] };
        var evidence = source with
        {
            ApplicationAllocationsLoaded = true,
            Ledger = [origin with { RecordedAt = origin.At.AddMinutes(5) }],
            Applications = [application]
        };
        var replay = InventoryEventReplay.Replay(evidence);
        Assert.True(replay.QuantityConserved);
        Assert.Equal("Unknown", Assert.Single(replay.Cohorts).State);
        Assert.Contains(replay.UnresolvedEvents, x => x.Contains(origin.Id.ToString()) && x.Contains("123456"));
    }

    [Fact]
    public void Complete_recorded_allocations_recover_known_treatment_and_keep_shared_receipt_ancestry()
    {
        var observed = ReadObservation().Single(x => x.Location.RoomId == 73 && x.Identity.Lot == "1242" && x.Identity.Variety == "RED");
        // Supplementary READ ONLY capture: application source 40, app 29,
        // receipt 2378, 70 bins, prior u, result u|a:29; identity 1242/RED.
        var evidence = observed with
        {
            ApplicationAllocationsLoaded = true,
            Applications = observed.Applications.Select(a => a.Id == 29
                ? a with { Allocations = [new(40, 70, 2378, "u", "u|a:29", true)] } : a).ToImmutableArray()
        };
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        Assert.True(result.IsOperable, string.Join(';', result.Blockers));
        Assert.True(result.UsesRecordedCohorts);
        Assert.Equal(652, result.AvailableQuantity);
        Assert.Equal(582, result.TreatmentSlices.Where(x => x.Signature == "u").Sum(x => x.Quantity));
        Assert.Equal(70, Assert.Single(result.TreatmentSlices, x => x.Signature == "u|a:29").Quantity);
        Assert.Equal(56, Assert.Single(result.TreatmentSlices, x => x.ReceiptEvidenceIds.IsEmpty).Quantity);
        Assert.Equal(InventoryConfidence.Unknown, result.ReceiptProvenance.Confidence);
    }

    [Fact]
    public void Destination_admission_uses_custody_and_ledger_not_null_projection_arrays()
    {
        var source = ReadObservation().Single(x => x.Location.RoomId == 55 && x.Identity.Lot == "1242");
        var empty = source with
        {
            AuthoritativeQuantity = 0,
            Projections = default,
            Movements = default,
            Ledger = default,
            Receipts = default,
            Applications = default
        };
        Assert.True(InventoryDestinationAdmission.Assess(empty).Allowed);
        Assert.True(InventoryDestinationAdmission.Assess(source with { Projections = default }).Allowed);
        Assert.False(InventoryDestinationAdmission.Assess(source with { AuthoritativeQuantity = -1 }).Allowed);
        Assert.False(InventoryDestinationAdmission.Assess(source with { CustodyVerified = false }).Allowed);
        Assert.False(InventoryDestinationAdmission.Assess(source with { CommittedQuantity = 35 }).Allowed);
    }

    [Fact]
    public void Recorded_events_reconstruct_receipt_treatment_without_using_destination_projections()
    {
        var destination = ReadObservation().Single(x => x.Location.RoomId == 73
            && x.Identity.Lot == "1242" && x.Identity.Variety == "RED");
        foreach (var projections in new[] { destination.Projections, [] })
        {
            var replay = InventoryEventReplay.Replay(destination with { Projections = projections });
            Assert.True(replay.QuantityConserved, string.Join("; ", replay.UnresolvedEvents));
            Assert.Equal(652, replay.Cohorts.Sum(x => x.Quantity));
            Assert.Equal(582, replay.Cohorts.Where(x => x.State == "Untreated").Sum(x => x.Quantity));
            var treated = Assert.Single(replay.Cohorts, x => x.State == "Confirmed");
            Assert.Equal(70, treated.Quantity);
            Assert.Equal(2378, treated.ReceiptId);
            Assert.Equal(new[] { 29L }, treated.ApplicationIds.ToArray());
            Assert.Equal(56, Assert.Single(replay.Cohorts, x => x.ReceiptId == null).Quantity);
            Assert.Empty(replay.UnresolvedEvents);
        }
    }

    // Captured from deployed 50bdc1a in a RepeatableRead, READ ONLY transaction.
    // No command was submitted to production. Keep the original observation.
    [Fact]
    public void Recorded_McDougall_snapshot_reproduces_the_destination_projection_block()
    {
        var positions = ReadObservation();
        var source = positions.Single(x => x.Location.RoomId == 55 && x.Identity.Lot == "1242");
        var destination = positions.Single(x => x.Location.RoomId == 73 && x.Identity.Key == source.Identity.Key);
        Assert.Equal("RED", source.Identity.Variety);
        Assert.Equal(34, InventoryAvailabilityResolver.Resolve(source, new()).AvailableQuantity);
        var result = InventoryAvailabilityResolver.Resolve(destination, new());
        Assert.Equal(652, result.AuthoritativeQuantity);
        Assert.Equal(442, result.RawProjectionQuantity);
        Assert.Contains(result.Blockers, x => x.Code == InventoryBlockerCode.MixedTreatmentAmbiguity);
        Assert.False(result.IsOperable); // This is the current executor's destination predicate.
        Assert.Equal(63, positions.Single(x => x.Identity.Variety == "ATGL").AuthoritativeQuantity);
        Assert.DoesNotContain(positions, x => x.Location.RoomId == 55 && x.Identity.Variety == "ATGL");
    }

    internal static InventoryPositionEvidence[] ReadObservation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CropQc.sln"))) root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "tests/CropQc.Api.Tests/Fixtures/InventoryMovement/mcdougall-20261009.json")));
        return document.RootElement.GetProperty("Positions").EnumerateArray()
            .Select(x => x.GetProperty("Evidence").Deserialize<InventoryPositionEvidence>()!).ToArray();
    }
}
