using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class ProjectionActualRunSeparationTests
{
    [Fact]
    public void PlanningProjection_ReadyScenarioIsImmutableAndLegacyConvertedStatusIsNotEditable()
    {
        Assert.Contains(RunProjectionStatuses.Draft, RunProjectionStatuses.Editable);
        Assert.DoesNotContain(RunProjectionStatuses.Ready, RunProjectionStatuses.Editable);
        Assert.DoesNotContain(RunProjectionStatuses.Converted, RunProjectionStatuses.Editable);
    }

    [Fact]
    public void EstimatedAllocation_UsesBinSharesAndReconcilesEveryOverallOutput()
    {
        var expectation = Expectation((1, 60), (2, 40));
        var packout = Packout(1000m, 101m, 51m, 25m);

        var rows = new PackoutSourceAllocationService().Allocate(packout, expectation, DateTimeOffset.UtcNow);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 60m, 40m }, rows.Select(x => x.ContributionPercent).ToArray());
        Assert.Equal(new[] { 600m, 400m }, rows.Select(x => x.AllocatedPackedPounds).ToArray());
        Assert.Equal(1000m, rows.Sum(x => x.AllocatedPackedPounds));
        Assert.Equal(101m, rows.Sum(x => x.AllocatedJuicePounds));
        Assert.Equal(51m, rows.Sum(x => x.AllocatedPeelerPounds));
        Assert.Equal(25m, rows.Sum(x => x.AllocatedWastePounds));
        Assert.Equal(25, rows.Sum(x => x.AllocatedWholeBoxes));
        Assert.All(rows, x => Assert.Equal(ActualAllocationVersions.Current, x.AllocationVersion));
    }

    [Fact]
    public void EstimatedAllocation_LargestRemainderIsDeterministicAndWholeBoxesReconcile()
    {
        var expectation = Expectation((9, 1), (2, 1), (5, 1));
        var packout = Packout(299m, 0m, 0m, 0m);

        var first = new PackoutSourceAllocationService().Allocate(packout, expectation, DateTimeOffset.UtcNow);
        var second = new PackoutSourceAllocationService().Allocate(packout, expectation, DateTimeOffset.UtcNow);

        Assert.Equal(7, first.Sum(x => x.AllocatedWholeBoxes));
        Assert.Equal(
            first.OrderBy(x => x.RunExpectationSourceId).Select(x => x.AllocatedWholeBoxes),
            second.OrderBy(x => x.RunExpectationSourceId).Select(x => x.AllocatedWholeBoxes));
        Assert.Equal(299m, first.Sum(x => x.AllocatedPackedPounds));
    }

    [Fact]
    public void EstimatedAllocation_CategoryMapsReconcileAtSixDecimalPrecision()
    {
        var expectation = Expectation((1, 1), (2, 1), (3, 1));
        var packout = Packout(40m, 0m, 0m, 0m);
        packout.Lines.Add(new PackoutReportLine
        {
            ProductCategory = PackoutProductCategories.Packed,
            NormalizedPackCode = "80-US1",
            ExtendedWeightPounds = 1m,
            RawText = "test",
            Confidence = 1m,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var rows = new PackoutSourceAllocationService().Allocate(packout, expectation, DateTimeOffset.UtcNow);
        var allocated = rows.Sum(x =>
            JsonSerializer.Deserialize<Dictionary<string, decimal>>(x.PackCodeAllocationJson)!["80-US1"]);

        Assert.Equal(1m, allocated);
    }

    [Fact]
    public void EbsCleanup_ProtectsEvans7ByRoomIdentityRegardlessOfFruit()
    {
        Assert.True(EbsInventoryCleanupService.IsEvans7Room(Room("EVANS-7", "Evans Street 7")));
        Assert.True(EbsInventoryCleanupService.IsEvans7Room(Room("OTHER", "Evans 7")));
        Assert.False(EbsInventoryCleanupService.IsEvans7Room(Room("EVANS-6", "Evans Street 6")));
    }

    [Fact]
    public void Views_KeepPackoutOnActualRunAndKeepPlanningIndependent()
    {
        var projection = Read("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");
        var actual = Read("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml");
        var controller = Read("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("Planning Projection", projection);
        Assert.DoesNotContain("enctype=\"multipart/form-data\"", projection);
        Assert.Contains("Run Expectation", actual);
        Assert.Contains("Packout Result", actual);
        Assert.Contains("Expected vs. Actual", actual);
        Assert.Contains("Estimated Allocation", actual);
        Assert.Contains("RejectProjectionPackoutUpload", controller);
    }

    [Fact]
    public void Migration_IsProviderCompatibleAndDoesNotResetOperationalData()
    {
        var migration = Read(
            "src",
            "CropQc.Data",
            "Migrations",
            "20260731014107_SeparatePlanningProjectionsFromActualRuns.cs");
        var up = migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("MigrationProviderTypes.StoreType", up);
        Assert.Contains("NpgsqlValueGenerationStrategy.IdentityByDefaultColumn", up);
        Assert.Contains("timestamp with time zone", up);
        Assert.Contains("MigrationProviderTypes.Sql", up);
        Assert.DoesNotContain("DropTable", up);
        Assert.DoesNotContain("DropColumn", up);
        Assert.DoesNotContain("RunProjections\", schema", up);
    }

    [Fact]
    public void ProjectionReset_IsSeparateAuditedIdempotentArchive()
    {
        var preflight = Read("scripts", "postgresql", "preflight-planning-projection-reset.sql");
        var apply = Read("scripts", "postgresql", "apply-planning-projection-reset.sql");
        var verify = Read("scripts", "postgresql", "verify-planning-projection-reset.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", preflight);
        Assert.Contains("WHERE NOT p.\"IsDeleted\"", apply);
        Assert.Contains("ArchiveInvalidLegacyPlanningProjection", apply);
        Assert.DoesNotContain("DELETE FROM", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RoomInventoryAdjustments\" SET", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active_projection_count", verify);
    }

    private static RunExpectation Expectation(params (long Id, int Bins)[] sourceRows)
    {
        var expectation = new RunExpectation
        {
            TotalBins = sourceRows.Sum(x => x.Bins),
            FacilitySnapshot = "WP",
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}",
            ConfigurationSnapshotJson = "{}",
            CalculationVersion = RunExpectationCalculationVersions.Current
        };
        foreach (var row in sourceRows)
        {
            expectation.Sources.Add(new RunExpectationSource
            {
                Id = row.Id,
                BinsContributed = row.Bins,
                FacilitySnapshot = "WP",
                RoomSnapshot = "Room",
                GrowerSnapshot = "Grower",
                LotSnapshot = $"Lot {row.Id}",
                VarietySnapshot = "Bartlett",
                ProductionTypeSnapshot = "Conventional",
                QcMeasurementSnapshotJson = "{}",
                SizeDistributionSnapshotJson = "{}",
                GradeDistributionSnapshotJson = "{}"
            });
        }
        return expectation;
    }

    private static PackoutRun Packout(decimal packed, decimal juice, decimal peeler, decimal waste) =>
        new()
        {
            Status = PackoutRunStatuses.Finalized,
            FacilitySnapshot = "WP",
            LotNumberSnapshot = "MULTI",
            VarietySnapshot = "Bartlett",
            PackedProductPounds = packed,
            JuicePounds = juice,
            PeelerSlicerPounds = peeler,
            WastePounds = waste
        };

    private static EbsInventoryCleanupService.ProtectedRoomIdentity Room(string code, string name) =>
        new(1, code, name, null, null, null);

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFile(parts));

    private static string RepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
