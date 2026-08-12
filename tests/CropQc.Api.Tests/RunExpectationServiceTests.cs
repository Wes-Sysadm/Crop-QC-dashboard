using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class RunExpectationServiceTests
{
    private static readonly DateTimeOffset RunAt = DateTimeOffset.Parse("2026-07-29T00:31:00Z");

    [Fact]
    public async Task Historical_reconstruction_uses_reporting_identity_and_latest_qc_not_after_physical_run()
    {
        await using var fixture = await Fixture.CreateAsync(includePreRunSamples: true);

        var expectation = await fixture.Service.CreateHistoricalReconstructionAsync(
            fixture.Run,
            fixture.Revision,
            [fixture.Entry],
            1,
            RunAt.AddDays(14),
            "test-package",
            CancellationToken.None);

        var source = Assert.Single(expectation.Sources);
        Assert.Equal(2026, source.CropYearSnapshot);
        Assert.Equal(17, source.FruitProfileId);
        Assert.Equal("Bartlett", source.VarietySnapshot);
        Assert.Equal("Conventional", source.ProductionTypeSnapshot);
        Assert.False(source.IsOrganicSnapshot);
        Assert.Equal(11, source.QcSampleId);
        Assert.Equal(RunAt.AddMinutes(-1), source.QcSampleTakenAtSnapshot);
        Assert.NotEqual(12, source.QcSampleId);
        Assert.True(RunExpectationMetadata.TryGetHistoricalReconstruction(expectation.ConfigurationSnapshotJson, out var metadata));
        Assert.Equal(RunAt, metadata!.PhysicalRunAt);
        Assert.Equal(RunAt, metadata.QcEvidenceCutoff);
        Assert.Equal(RunAt.AddDays(14), metadata.ReconstructedAt);
        Assert.Equal("test-package", metadata.CorrectionPackageIdentifier);
    }

    [Fact]
    public async Task Normal_expectation_with_only_post_run_qc_has_no_qc_evidence_and_no_reconstruction_marker()
    {
        await using var fixture = await Fixture.CreateAsync(includePreRunSamples: false);

        var expectation = await fixture.Service.CreateFrozenAsync(
            fixture.Run,
            fixture.Revision,
            [fixture.Entry],
            1,
            RunAt,
            CancellationToken.None);

        var source = Assert.Single(expectation.Sources);
        Assert.Null(source.QcSampleId);
        Assert.Null(source.QcSampleTakenAtSnapshot);
        Assert.Equal(0, source.QcFruitCountSnapshot);
        Assert.False(RunExpectationMetadata.TryGetHistoricalReconstruction(expectation.ConfigurationSnapshotJson, out _));
        Assert.Contains("ApplePoundsPerBin", expectation.ConfigurationSnapshotJson, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(CropQcDbContext db, ActualRun run, ActualRunRevision revision, BinsRunEntry entry)
        {
            Db = db;
            Run = run;
            Revision = revision;
            Entry = entry;
            Service = new RunExpectationService(db, NullLogger<RunExpectationService>.Instance);
        }

        public CropQcDbContext Db { get; }
        public ActualRun Run { get; }
        public ActualRunRevision Revision { get; }
        public BinsRunEntry Entry { get; }
        public IRunExpectationService Service { get; }

        public static async Task<Fixture> CreateAsync(bool includePreRunSamples)
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"run-expectation-{Guid.NewGuid():N}")
                .Options;
            var db = new CropQcDbContext(options);
            var warehouse = new Warehouse { Id = 4, Code = "WP", Name = "Windy Point" };
            var room = new Room { Id = 1, WarehouseId = 4, Warehouse = warehouse, Code = "1", Name = "Room 1" };
            var reportingProfile = new FruitProfile
            {
                Id = 17,
                Name = "Bartlett",
                VarietyCode = "BART",
                FruitType = "Pear",
                ProductionType = "Conventional",
                IsOrganic = false
            };
            var physicalProfile = new FruitProfile
            {
                Id = 18,
                Name = "D'Anjou Organic",
                VarietyCode = "DANJ",
                FruitType = "Pear",
                ProductionType = "Organic",
                IsOrganic = true
            };
            var sampleType = new SampleType { Id = 1, Name = "Receiving" };
            var receipt = new Receipt
            {
                Id = 100,
                CropYear = 2026,
                ReceivedAt = RunAt.AddDays(-2),
                CompuTechReceiptId = "R100",
                WarehouseId = 4,
                Warehouse = warehouse,
                RoomId = 1,
                Room = room,
                FruitProfileId = 17,
                FruitProfile = reportingProfile,
                GrowerNumber = "1084",
                GrowerName = "WP Orchard Conventional",
                LotCode = "1084"
            };
            if (includePreRunSamples)
            {
                receipt.Samples.Add(Sample(10, receipt, sampleType, RunAt.AddMinutes(-2), 70));
                receipt.Samples.Add(Sample(11, receipt, sampleType, RunAt.AddMinutes(-1), 80));
            }
            receipt.Samples.Add(Sample(12, receipt, sampleType, RunAt.AddMinutes(1), 90));
            db.AddRange(warehouse, room, reportingProfile, physicalProfile, sampleType, receipt);
            await db.SaveChangesAsync();

            var run = new ActualRun { Id = 1, Status = ActualRunStatuses.Active, CurrentRevisionNumber = 1, RunAt = RunAt };
            var revision = new ActualRunRevision
            {
                Id = 1,
                ActualRunId = 1,
                RevisionNumber = 1,
                OperationType = ActualRunRevisionTypes.Create,
                OperationKey = "test",
                IsCurrent = true
            };
            var entry = new BinsRunEntry
            {
                Id = 31,
                WarehouseId = 4,
                RoomId = 1,
                CropYear = null,
                FruitProfileId = 18,
                ReportingCropYearSnapshot = 2026,
                ReportingFruitProfileIdSnapshot = 17,
                ReportingVarietyCodeSnapshot = "BART",
                ProductionTypeSnapshot = "Conventional",
                IsOrganicSnapshot = false,
                GrowerName = "WP Orchard Conventional",
                LotNumber = "1084",
                VarietyCode = "DANJ",
                InventoryStatus = "Organic",
                BinsRun = 184,
                RunAt = RunAt
            };
            return new Fixture(db, run, revision, entry);
        }

        private static QcSample Sample(long id, Receipt receipt, SampleType sampleType, DateTimeOffset takenAt, int size)
        {
            var sample = new QcSample
            {
                Id = id,
                ReceiptId = receipt.Id,
                Receipt = receipt,
                SampleTypeId = sampleType.Id,
                SampleType = sampleType,
                Status = "Complete",
                StarchStatus = "Complete",
                PhotoStatus = "Complete",
                EmailStatus = "Not sent",
                SampleTakenAt = takenAt
            };
            sample.FruitReadings.Add(new QcFruitReading
            {
                Id = id,
                QcSampleId = id,
                QcSample = sample,
                RowNumber = 1,
                SizeCategory = size,
                SizeStatus = "Complete"
            });
            return sample;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
