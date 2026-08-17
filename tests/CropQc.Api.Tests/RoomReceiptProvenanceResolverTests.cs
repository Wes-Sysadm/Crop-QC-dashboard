using CropQc.Web.Models;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class RoomReceiptProvenanceResolverTests
{
    [Fact]
    public void DirectReceipt_IsShownOnlyForPositiveCurrentIdentity()
    {
        var identity = Identity();
        var receipt = Receipt(identity, roomId: 1);

        var current = Resolve([Target("current", 1, 60, identity)], [receipt], []);
        var empty = Resolve([Target("empty", 1, 0, identity)], [receipt], []);

        var evidence = Assert.Single(current["current"]);
        Assert.Equal(RoomReceiptEvidenceTypes.Direct, evidence.EvidenceType);
        Assert.Empty(empty["empty"]);
    }

    [Fact]
    public void FullTransfer_LinksOriginalReceiptWithoutChangingHistoricalReceipt()
    {
        var identity = Identity();
        var receipt = Receipt(identity, roomId: 1, bins: 100);
        var transfer = Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z");

        var evidence = Assert.Single(Resolve([Target("B", 2, 100, identity)], [receipt], [transfer])["B"]);

        Assert.Equal(RoomReceiptEvidenceTypes.TransferLinked, evidence.EvidenceType);
        Assert.Equal([10L], evidence.TransferPathIds);
        Assert.Equal(1, receipt.OriginalRoomId);
        Assert.Equal(100, receipt.OriginalBins);
    }

    [Fact]
    public void PartialTransfer_ShowsReceiptInSourceAndDestinationWithoutInventedSplit()
    {
        var identity = Identity();
        var receipt = Receipt(identity, roomId: 1, bins: 100);
        var transfer = Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z");
        var result = Resolve(
            [Target("A", 1, 60, identity), Target("B", 2, 40, identity)],
            [receipt],
            [transfer]);

        Assert.Equal(RoomReceiptEvidenceTypes.Direct, Assert.Single(result["A"]).EvidenceType);
        Assert.Equal(RoomReceiptEvidenceTypes.TransferLinked, Assert.Single(result["B"]).EvidenceType);
        Assert.All(result.SelectMany(x => x.Value), x => Assert.Equal(100, x.Receipt.OriginalBins));
    }

    [Fact]
    public void ChainedTransfer_FollowsChronologicalPath()
    {
        var identity = Identity();
        var result = Resolve(
            [Target("C", 3, 40, identity)],
            [Receipt(identity, roomId: 1)],
            [
                Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z"),
                Transfer(11, 2, 3, identity, "2026-08-03T12:00:00Z")
            ]);

        var evidence = Assert.Single(result["C"]);
        Assert.Equal(RoomReceiptEvidenceTypes.TransferLinked, evidence.EvidenceType);
        Assert.Equal([10L, 11L], evidence.TransferPathIds);
    }

    [Fact]
    public void PartialChain_CanShowSameReceiptInEveryCurrentRoom()
    {
        var identity = Identity();
        var result = Resolve(
            [Target("A", 1, 50, identity), Target("B", 2, 10, identity), Target("C", 3, 40, identity)],
            [Receipt(identity, roomId: 1)],
            [
                Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z"),
                Transfer(11, 2, 3, identity, "2026-08-03T12:00:00Z")
            ]);

        Assert.Equal(RoomReceiptEvidenceTypes.Direct, Assert.Single(result["A"]).EvidenceType);
        Assert.Equal([10L], Assert.Single(result["B"]).TransferPathIds);
        Assert.Equal([10L, 11L], Assert.Single(result["C"]).TransferPathIds);
    }

    [Fact]
    public void MultiplePlausibleReceipts_AreAllShownWithoutAllocatedBins()
    {
        var identity = Identity();
        var receipts = new[]
        {
            Receipt(identity, id: 1, display: "TR1", roomId: 1, bins: 30),
            Receipt(identity, id: 2, display: "TR2", roomId: 1, bins: 70)
        };
        var evidence = Resolve(
            [Target("B", 2, 40, identity)],
            receipts,
            [Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z")])["B"];

        Assert.Equal(2, evidence.Count);
        Assert.Equal(["TR1", "TR2"], evidence.Select(x => x.Receipt.DisplayReceiptId));
        Assert.Equal([30, 70], evidence.Select(x => x.Receipt.OriginalBins));
    }

    [Theory]
    [InlineData(2025, 77, 11, false)]
    [InlineData(2026, 78, 11, false)]
    [InlineData(2026, 77, 12, false)]
    public void WrongDurableIdentity_IsExcluded(int year, int growerLotId, int profileId, bool organic)
    {
        var targetIdentity = Identity();
        var wrongIdentity = Identity(year, growerLotId, profileId, organic);
        var result = Resolve(
            [Target("B", 2, 40, targetIdentity)],
            [Receipt(wrongIdentity, roomId: 1)],
            [Transfer(10, 1, 2, targetIdentity, "2026-08-02T12:00:00Z")]);

        Assert.Empty(result["B"]);
    }

    [Fact]
    public void OrganicAndConventionalFallbackIdentities_DoNotCross()
    {
        var conventional = CanonicalQcFruitIdentity.Create(2026, 77, "9392", "9392", null, "GALA", "Conventional", false)!;
        var organic = CanonicalQcFruitIdentity.Create(2026, 77, "9392", "9392", null, "GALA", "Organic", true)!;
        var result = Resolve(
            [Target("B", 2, 40, conventional)],
            [Receipt(organic, roomId: 1)],
            [Transfer(10, 1, 2, conventional, "2026-08-02T12:00:00Z")]);

        Assert.Empty(result["B"]);
    }

    [Fact]
    public void GrowerName_IsNotPartOfDurableIdentity()
    {
        var identity = Identity();
        var receipt = Receipt(identity, roomId: 1) with { GrowerName = "Historical name" };
        var evidence = Assert.Single(Resolve(
            [Target("B", 2, 40, identity)],
            [receipt],
            [Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z")])["B"]);

        Assert.Equal("Historical name", evidence.Receipt.GrowerName);
        Assert.Equal(RoomReceiptEvidenceTypes.TransferLinked, evidence.EvidenceType);
    }

    [Fact]
    public void ReceiptCreatedAfterTransfer_IsExcluded()
    {
        var identity = Identity();
        var future = Receipt(identity, roomId: 1) with { ReceivedAt = DateTimeOffset.Parse("2026-08-03T12:00:00Z") };
        var result = Resolve(
            [Target("B", 2, 40, identity)],
            [future],
            [Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z")]);

        Assert.Empty(result["B"]);
    }

    [Fact]
    public void ReversedTransfer_DoesNotLeaveStaleDestinationEvidence()
    {
        var identity = Identity();
        var result = Resolve(
            [Target("B", 2, 1, identity)],
            [Receipt(identity, roomId: 1)],
            [
                Transfer(10, 1, 2, identity, "2026-08-02T12:00:00Z") with { IsReversed = true },
                Transfer(11, 2, 1, identity, "2026-08-03T12:00:00Z")
            ]);

        Assert.Empty(result["B"]);
    }

    [Fact]
    public void DirectEvidence_RanksBeforeTransferAndPossibleEvidence()
    {
        Assert.True(RoomReceiptProvenanceResolver.EvidenceRank(RoomReceiptEvidenceTypes.Direct)
            < RoomReceiptProvenanceResolver.EvidenceRank(RoomReceiptEvidenceTypes.TransferLinked));
        Assert.True(RoomReceiptProvenanceResolver.EvidenceRank(RoomReceiptEvidenceTypes.TransferLinked)
            < RoomReceiptProvenanceResolver.EvidenceRank(RoomReceiptEvidenceTypes.PossibleSource));
    }

    [Fact]
    public void ExactUnambiguousLegacyIdentity_CanBePossibleSource()
    {
        var legacy = CanonicalQcFruitIdentity.Create(2026, null, "9392", "9392", 2, "GALA", "Conventional", false)!;
        var canonical = CanonicalQcFruitIdentity.Create(2026, 77, "9392", "9392", 2, "GALA", "Conventional", false)!;
        var evidence = Assert.Single(Resolve(
            [Target("B", 2, 12, legacy)],
            [Receipt(canonical, roomId: 1)],
            [])["B"]);

        Assert.Equal(RoomReceiptEvidenceTypes.PossibleSource, evidence.EvidenceType);
    }

    [Fact]
    public void AmbiguousLegacyIdentity_FailsClosed()
    {
        var legacy = CanonicalQcFruitIdentity.Create(2026, null, "9392", "9392", 2, "GALA", "Conventional", false)!;
        var first = CanonicalQcFruitIdentity.Create(2026, 77, "9392", "9392", 2, "GALA", "Conventional", false)!;
        var second = CanonicalQcFruitIdentity.Create(2026, 78, "9392", "9392", 2, "GALA", "Conventional", false)!;
        var result = Resolve(
            [Target("B", 2, 12, legacy)],
            [Receipt(first, id: 1, roomId: 1), Receipt(second, id: 2, roomId: 1)],
            []);

        Assert.Empty(result["B"]);
    }

    [Fact]
    public void ResolverBounds_FailSafely()
    {
        var identity = Identity();
        var targets = Enumerable.Range(1, RoomReceiptProvenanceResolver.MaximumTargets + 1)
            .Select(x => Target(x.ToString(), 1, 1, identity))
            .ToList();

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(targets, [], []));

        Assert.Contains("safe limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidateBounds_FailSafely()
    {
        var identity = Identity();
        var receipts = Enumerable.Range(1, RoomReceiptProvenanceResolver.MaximumReceiptCandidates + 1)
            .Select(x => Receipt(identity, id: x))
            .ToList();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve([Target("A", 1, 1, identity)], receipts, []));

        Assert.Contains("receipt candidates", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransferPathBound_FailsSafely()
    {
        var identity = Identity();
        var transfers = Enumerable.Range(1, RoomReceiptProvenanceResolver.MaximumRoomsPerPath + 1)
            .Select(x => Transfer(x, x, x + 1, identity, DateTimeOffset.Parse("2026-08-02T12:00:00Z").AddMinutes(x).ToString("O")))
            .ToList();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve(
                [Target("far", RoomReceiptProvenanceResolver.MaximumRoomsPerPath + 2, 1, identity)],
                [Receipt(identity, roomId: 1)],
                transfers));

        Assert.Contains("path exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomView_ExplainsInferenceAndLabelsHistoricalBins()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));

        Assert.Contains("Likely Source Receipts", view);
        Assert.Contains("Receipts are inferred from matching fruit identity and transfer history", view);
        Assert.Contains("partial transfer", view);
        Assert.Contains("Original receipt bins (historical)", view);
        Assert.Contains("GrowerNumber", view);
        Assert.Contains("EvidenceType", view);
    }

    [Fact]
    public void CurrentGrowerLots_ShowsTransferDerivedReceiptEvidence()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml"));

        Assert.Contains("Likely source receipts", view);
        Assert.Contains("receipt.EvidenceType", view);
        Assert.Contains("/Receipts/@receipt.ReceiptId", view);
    }

    [Fact]
    public void DashboardResolution_IsBatchedAndRoomWideResultsAreDeduplicated()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("DecorateReceiptProvenanceAsync(currentCandidates", service);
        Assert.Contains("DecorateReceiptProvenanceAsync(activeLots", service);
        Assert.Contains("MaximumReceiptCandidates + 1", service);
        Assert.Contains("MaximumTransferCandidates + 1", service);
        Assert.Contains("roomWideLinks", service);
        Assert.Contains(".GroupBy(x => x.ReceiptId)", service);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ResolvedReceiptEvidence>> Resolve(
        IReadOnlyCollection<ReceiptProvenanceTarget> targets,
        IReadOnlyCollection<ReceiptProvenanceCandidate> receipts,
        IReadOnlyCollection<TransferProvenanceCandidate> transfers) =>
        RoomReceiptProvenanceResolver.Resolve(targets, receipts, transfers);

    private static CanonicalQcFruitIdentity Identity(
        int year = 2026,
        int growerLotId = 77,
        int profileId = 11,
        bool organic = false) =>
        CanonicalQcFruitIdentity.Create(
            year,
            growerLotId,
            "9392",
            "9392",
            profileId,
            "GALA",
            organic ? "Organic" : "Conventional",
            organic)!;

    private static ReceiptProvenanceTarget Target(
        string key,
        int roomId,
        int currentBins,
        CanonicalQcFruitIdentity identity) =>
        new(key, roomId, currentBins, identity);

    private static ReceiptProvenanceCandidate Receipt(
        CanonicalQcFruitIdentity identity,
        long id = 1,
        string display = "TR108869",
        int roomId = 1,
        int bins = 100) =>
        new(
            id,
            display,
            roomId,
            "EBS",
            $"ROOM-{roomId}",
            bins,
            DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            "9392",
            "Authoritative grower",
            identity);

    private static TransferProvenanceCandidate Transfer(
        long id,
        int source,
        int destination,
        CanonicalQcFruitIdentity identity,
        string transferredAt) =>
        new(id, source, destination, DateTimeOffset.Parse(transferredAt), false, identity);

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
