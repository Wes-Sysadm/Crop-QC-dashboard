using System.Data;
using System.Reflection;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class DatabaseStartupDiagnostics
{
    public const string ExpectedSchemaMigration = "20260902011217_AddInventoryIdentityCorrections";

    private static readonly SchemaExpectation[] RequiredSchemaExpectations =
    [
        new("CanonicalOrchards", "CanonicalOrchards", null),
        new("OrchardReportRecipients", "OrchardReportRecipients", null),
        new("GrowerReportRecipients", "GrowerReportRecipients", null),
        new("GrowerReportRecipients.Id", "GrowerReportRecipients", "Id", RequireNotNullable: true),
        new("GrowerReportRecipients.CanonicalGrowerNumberId", "GrowerReportRecipients", "CanonicalGrowerNumberId", RequireNotNullable: true),
        new("GrowerReportRecipients.EmailAddress", "GrowerReportRecipients", "EmailAddress", RequireNotNullable: true),
        new("GrowerReportRecipients.NormalizedEmailAddress", "GrowerReportRecipients", "NormalizedEmailAddress", RequireNotNullable: true),
        new("GrowerReportRecipients.IsActive", "GrowerReportRecipients", "IsActive", RequireNotNullable: true),
        new("GrowerReportRecipients.IsDeleted", "GrowerReportRecipients", "IsDeleted", RequireNotNullable: true),
        new("GrowerReportRecipients.CreatedAt", "GrowerReportRecipients", "CreatedAt", RequireNotNullable: true),
        new("GrowerReportRecipients.CreatedByUserId", "GrowerReportRecipients", "CreatedByUserId", RequireNullable: true),
        new("GrowerReportRecipients.UpdatedAt", "GrowerReportRecipients", "UpdatedAt", RequireNotNullable: true),
        new("GrowerReportRecipients.UpdatedByUserId", "GrowerReportRecipients", "UpdatedByUserId", RequireNullable: true),
        new("GrowerReportRecipients.DeletedAt", "GrowerReportRecipients", "DeletedAt", RequireNullable: true),
        new("GrowerReportRecipients.DeletedByUserId", "GrowerReportRecipients", "DeletedByUserId", RequireNullable: true),
        new("Receipts.CanonicalOrchardBlockId", "Receipts", "CanonicalOrchardBlockId"),
        new("CanonicalOrchardBlocks.CanonicalOrchardId", "CanonicalOrchardBlocks", "CanonicalOrchardId"),
        new("PackCodeDefinitions", "PackCodeDefinitions", null),
        new("PackoutAnalysisConfigurations", "PackoutAnalysisConfigurations", null),
        new("PackoutRuns", "PackoutRuns", null),
        new("PackoutEmailAttempts", "PackoutEmailAttempts", null),
        new("PackoutReportSources", "PackoutReportSources", null),
        new("PackoutReportSources.StorageProvider", "PackoutReportSources", "StorageProvider", RequireNullable: true),
        new("PackoutReportSources.StorageKey", "PackoutReportSources", "StorageKey", RequireNullable: true),
        new("PackoutReportSources.StoragePath", "PackoutReportSources", "StoragePath", RequireNullable: true),
        new("PackoutReportSources.DriveId", "PackoutReportSources", "DriveId", RequireNullable: true),
        new("PackoutReportSources.FileId", "PackoutReportSources", "FileId", RequireNullable: true),
        new("PackoutReportSources.FolderId", "PackoutReportSources", "FolderId", RequireNullable: true),
        new("PackoutReportSources.ParseStatus", "PackoutReportSources", "ParseStatus", RequireNotNullable: true),
        new("PackoutReportSources.UploadedAt", "PackoutReportSources", "UploadedAt", RequireNullable: true),
        new("PackoutReportSources.UploadedByUserId", "PackoutReportSources", "UploadedByUserId", RequireNullable: true),
        new("PackoutReportLines", "PackoutReportLines", null),
        new("RunProjectionSources.TotalDefectPercentageSnapshot", "RunProjectionSources", "TotalDefectPercentageSnapshot"),
        new("RunProjections.IsLocked", "RunProjections", "IsLocked"),
        new("RunProjections.LockedAt", "RunProjections", "LockedAt"),
        new("RunProjections.LockedByUserId", "RunProjections", "LockedByUserId"),
        new("QcSamples.DefectInspectionStatus", "QcSamples", "DefectInspectionStatus"),
        new("BinsRunEntries.IsReconciled", "BinsRunEntries", "IsReconciled"),
        new("BinsRunEntries.ReconciledAt", "BinsRunEntries", "ReconciledAt"),
        new("BinsRunEntries.ReconciledByUserId", "BinsRunEntries", "ReconciledByUserId"),
        new("ActualRuns", "ActualRuns", null),
        new("ActualRunRevisions", "ActualRunRevisions", null),
        new("ActualRunOverrideRequests", "ActualRunOverrideRequests", null),
        new("ActualRunOverrideRequestLines", "ActualRunOverrideRequestLines", null),
        new("SalesDesks", "SalesDesks", null),
        new("SalesDesks.Id", "SalesDesks", "Id", RequireNotNullable: true),
        new("SalesDesks.Name", "SalesDesks", "Name", RequireNotNullable: true),
        new("SalesDesks.IsActive", "SalesDesks", "IsActive", RequireNotNullable: true),
        new("SalesDesks.DisplayOrder", "SalesDesks", "DisplayOrder", RequireNotNullable: true),
        new("SalesDesks.CreatedAt", "SalesDesks", "CreatedAt", RequireNotNullable: true),
        new("SalesDesks.CreatedByUserId", "SalesDesks", "CreatedByUserId", RequireNullable: true),
        new("SalesDesks.UpdatedAt", "SalesDesks", "UpdatedAt", RequireNotNullable: true),
        new("SalesDesks.UpdatedByUserId", "SalesDesks", "UpdatedByUserId", RequireNullable: true),
        new("ActualRuns.SalesDeskId", "ActualRuns", "SalesDeskId", RequireNullable: true),
        new("ActualRuns.SalesDeskNameSnapshot", "ActualRuns", "SalesDeskNameSnapshot", RequireNullable: true),
        new("ActualRunOverrideRequests.SalesDeskId", "ActualRunOverrideRequests", "SalesDeskId", RequireNullable: true),
        new("ActualRunOverrideRequests.SalesDeskNameSnapshot", "ActualRunOverrideRequests", "SalesDeskNameSnapshot", RequireNullable: true),
        new("ActualRunSalesDeskCorrections", "ActualRunSalesDeskCorrections", null),
        new("ActualRunSalesDeskCorrections.Id", "ActualRunSalesDeskCorrections", "Id", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.ActualRunId", "ActualRunSalesDeskCorrections", "ActualRunId", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.OperationKey", "ActualRunSalesDeskCorrections", "OperationKey", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.ExpectedConcurrencyVersion", "ActualRunSalesDeskCorrections", "ExpectedConcurrencyVersion", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.PreviousSalesDeskId", "ActualRunSalesDeskCorrections", "PreviousSalesDeskId", RequireNullable: true),
        new("ActualRunSalesDeskCorrections.PreviousSalesDeskNameSnapshot", "ActualRunSalesDeskCorrections", "PreviousSalesDeskNameSnapshot", RequireNullable: true),
        new("ActualRunSalesDeskCorrections.NewSalesDeskId", "ActualRunSalesDeskCorrections", "NewSalesDeskId", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.NewSalesDeskNameSnapshot", "ActualRunSalesDeskCorrections", "NewSalesDeskNameSnapshot", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.Reason", "ActualRunSalesDeskCorrections", "Reason", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.CorrectedByUserId", "ActualRunSalesDeskCorrections", "CorrectedByUserId", RequireNotNullable: true),
        new("ActualRunSalesDeskCorrections.CorrectedAt", "ActualRunSalesDeskCorrections", "CorrectedAt", RequireNotNullable: true),
        new("RoomInventoryAdjustments.ActualRunId", "RoomInventoryAdjustments", "ActualRunId"),
        new("RoomInventoryAdjustments.ActualRunRevisionId", "RoomInventoryAdjustments", "ActualRunRevisionId"),
        new("BinsRunEntries.ActualRunId", "BinsRunEntries", "ActualRunId"),
        new("BinsRunEntries.ActualRunRevisionId", "BinsRunEntries", "ActualRunRevisionId"),
        new("BinsRunEntries.TransactionType", "BinsRunEntries", "TransactionType"),
        new("BinsRunEntries.ReversesBinsRunEntryId", "BinsRunEntries", "ReversesBinsRunEntryId"),
        new("RoomTransfers", "RoomTransfers", null),
        new("RoomInventoryAdjustments.InventoryInvariantVersion", "RoomInventoryAdjustments", "InventoryInvariantVersion"),
        new("RoomInventoryAdjustments.InventoryOperationKey", "RoomInventoryAdjustments", "InventoryOperationKey"),
        new("RoomInventoryAdjustments.RoomTransferId", "RoomInventoryAdjustments", "RoomTransferId"),
        new("RunExpectations", "RunExpectations", null),
        new("RunExpectations.Id", "RunExpectations", "Id", RequireNotNullable: true),
        new("RunExpectations.ActualRunId", "RunExpectations", "ActualRunId", RequireNotNullable: true),
        new("RunExpectations.ActualRunRevisionId", "RunExpectations", "ActualRunRevisionId", RequireNotNullable: true),
        new("RunExpectations.RevisionNumber", "RunExpectations", "RevisionNumber", RequireNotNullable: true),
        new("RunExpectations.FacilityWarehouseId", "RunExpectations", "FacilityWarehouseId", RequireNotNullable: true),
        new("RunExpectations.FacilitySnapshot", "RunExpectations", "FacilitySnapshot", RequireNotNullable: true),
        new("RunExpectations.RunAtSnapshot", "RunExpectations", "RunAtSnapshot", RequireNotNullable: true),
        new("RunExpectations.TotalBins", "RunExpectations", "TotalBins", RequireNotNullable: true),
        new("RunExpectations.GrossPounds", "RunExpectations", "GrossPounds", RequireNotNullable: true),
        new("RunExpectations.ExpectedPackoutPercent", "RunExpectations", "ExpectedPackoutPercent", RequireNotNullable: true),
        new("RunExpectations.ExpectedPackedPounds", "RunExpectations", "ExpectedPackedPounds", RequireNotNullable: true),
        new("RunExpectations.ExpectedPackedBoxes", "RunExpectations", "ExpectedPackedBoxes", RequireNotNullable: true),
        new("RunExpectations.ExpectedWholeBoxes", "RunExpectations", "ExpectedWholeBoxes", RequireNotNullable: true),
        new("RunExpectations.ExpectedCullPounds", "RunExpectations", "ExpectedCullPounds", RequireNotNullable: true),
        new("RunExpectations.ExpectedJuicePounds", "RunExpectations", "ExpectedJuicePounds", RequireNotNullable: true),
        new("RunExpectations.ExpectedPeelerPounds", "RunExpectations", "ExpectedPeelerPounds", RequireNotNullable: true),
        new("RunExpectations.ExpectedWastePounds", "RunExpectations", "ExpectedWastePounds", RequireNotNullable: true),
        new("RunExpectations.ConfidencePercent", "RunExpectations", "ConfidencePercent", RequireNotNullable: true),
        new("RunExpectations.SizeDistributionSnapshotJson", "RunExpectations", "SizeDistributionSnapshotJson", RequireNotNullable: true),
        new("RunExpectations.GradeDistributionSnapshotJson", "RunExpectations", "GradeDistributionSnapshotJson", RequireNotNullable: true),
        new("RunExpectations.ConfigurationSnapshotJson", "RunExpectations", "ConfigurationSnapshotJson", RequireNotNullable: true),
        new("RunExpectations.CalculationVersion", "RunExpectations", "CalculationVersion", RequireNotNullable: true),
        new("RunExpectations.CalculatedAt", "RunExpectations", "CalculatedAt", RequireNotNullable: true),
        new("RunExpectations.CreatedByUserId", "RunExpectations", "CreatedByUserId", RequireNullable: true),
        new("RunExpectationSources", "RunExpectationSources", null),
        new("RunExpectationSources.Id", "RunExpectationSources", "Id", RequireNotNullable: true),
        new("RunExpectationSources.RunExpectationId", "RunExpectationSources", "RunExpectationId", RequireNotNullable: true),
        new("RunExpectationSources.BinsRunEntryId", "RunExpectationSources", "BinsRunEntryId", RequireNotNullable: true),
        new("RunExpectationSources.WarehouseId", "RunExpectationSources", "WarehouseId", RequireNotNullable: true),
        new("RunExpectationSources.RoomId", "RunExpectationSources", "RoomId", RequireNotNullable: true),
        new("RunExpectationSources.FacilitySnapshot", "RunExpectationSources", "FacilitySnapshot", RequireNotNullable: true),
        new("RunExpectationSources.RoomSnapshot", "RunExpectationSources", "RoomSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.CropYearSnapshot", "RunExpectationSources", "CropYearSnapshot", RequireNullable: true),
        new("RunExpectationSources.GrowerLotId", "RunExpectationSources", "GrowerLotId", RequireNullable: true),
        new("RunExpectationSources.FruitProfileId", "RunExpectationSources", "FruitProfileId", RequireNullable: true),
        new("RunExpectationSources.GrowerSnapshot", "RunExpectationSources", "GrowerSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.LotSnapshot", "RunExpectationSources", "LotSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.VarietySnapshot", "RunExpectationSources", "VarietySnapshot", RequireNotNullable: true),
        new("RunExpectationSources.ProductionTypeSnapshot", "RunExpectationSources", "ProductionTypeSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.IsOrganicSnapshot", "RunExpectationSources", "IsOrganicSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.BinsContributed", "RunExpectationSources", "BinsContributed", RequireNotNullable: true),
        new("RunExpectationSources.ContributionPercent", "RunExpectationSources", "ContributionPercent", RequireNotNullable: true),
        new("RunExpectationSources.QcSampleId", "RunExpectationSources", "QcSampleId", RequireNullable: true),
        new("RunExpectationSources.QcSampleTakenAtSnapshot", "RunExpectationSources", "QcSampleTakenAtSnapshot", RequireNullable: true),
        new("RunExpectationSources.QcFruitCountSnapshot", "RunExpectationSources", "QcFruitCountSnapshot", RequireNotNullable: true),
        new("RunExpectationSources.QcMeasurementSnapshotJson", "RunExpectationSources", "QcMeasurementSnapshotJson", RequireNotNullable: true),
        new("RunExpectationSources.SizeDistributionSnapshotJson", "RunExpectationSources", "SizeDistributionSnapshotJson", RequireNotNullable: true),
        new("RunExpectationSources.GradeDistributionSnapshotJson", "RunExpectationSources", "GradeDistributionSnapshotJson", RequireNotNullable: true),
        new("RunExpectationSources.GrossPounds", "RunExpectationSources", "GrossPounds", RequireNotNullable: true),
        new("RunExpectationSources.ExpectedPackedPounds", "RunExpectationSources", "ExpectedPackedPounds", RequireNotNullable: true),
        new("RunExpectationSources.ExpectedWholeBoxes", "RunExpectationSources", "ExpectedWholeBoxes", RequireNotNullable: true),
        new("RunExpectationSources.ExpectedCullPounds", "RunExpectationSources", "ExpectedCullPounds", RequireNotNullable: true),
        new("RunExpectationSources.ConfidencePercent", "RunExpectationSources", "ConfidencePercent", RequireNotNullable: true),
        new("RunExpectationSources.WarningSnapshot", "RunExpectationSources", "WarningSnapshot", RequireNullable: true),
        new("PackoutSourceAllocations", "PackoutSourceAllocations", null),
        new("PackoutSourceAllocations.Id", "PackoutSourceAllocations", "Id", RequireNotNullable: true),
        new("PackoutSourceAllocations.PackoutRunId", "PackoutSourceAllocations", "PackoutRunId", RequireNotNullable: true),
        new("PackoutSourceAllocations.RunExpectationSourceId", "PackoutSourceAllocations", "RunExpectationSourceId", RequireNotNullable: true),
        new("PackoutSourceAllocations.BinsContributed", "PackoutSourceAllocations", "BinsContributed", RequireNotNullable: true),
        new("PackoutSourceAllocations.ContributionPercent", "PackoutSourceAllocations", "ContributionPercent", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedPackedPounds", "PackoutSourceAllocations", "AllocatedPackedPounds", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedWholeBoxes", "PackoutSourceAllocations", "AllocatedWholeBoxes", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedResidualPounds", "PackoutSourceAllocations", "AllocatedResidualPounds", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedJuicePounds", "PackoutSourceAllocations", "AllocatedJuicePounds", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedPeelerPounds", "PackoutSourceAllocations", "AllocatedPeelerPounds", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocatedWastePounds", "PackoutSourceAllocations", "AllocatedWastePounds", RequireNotNullable: true),
        new("PackoutSourceAllocations.PackCodeAllocationJson", "PackoutSourceAllocations", "PackCodeAllocationJson", RequireNotNullable: true),
        new("PackoutSourceAllocations.SizeAllocationJson", "PackoutSourceAllocations", "SizeAllocationJson", RequireNotNullable: true),
        new("PackoutSourceAllocations.GradeAllocationJson", "PackoutSourceAllocations", "GradeAllocationJson", RequireNotNullable: true),
        new("PackoutSourceAllocations.AllocationVersion", "PackoutSourceAllocations", "AllocationVersion", RequireNotNullable: true),
        new("PackoutSourceAllocations.CalculatedAt", "PackoutSourceAllocations", "CalculatedAt", RequireNotNullable: true),
        new("PackoutRuns.ActualRunId", "PackoutRuns", "ActualRunId", RequireNullable: true),
        new("PackoutRuns.RunExpectationId", "PackoutRuns", "RunExpectationId", RequireNullable: true),
        new("PackoutRuns.RunProjectionId", "PackoutRuns", "RunProjectionId", RequireNullable: true),
        new("UserEmploymentHistory", "UserEmploymentHistory", null),
        new("UserEmploymentHistory.Id", "UserEmploymentHistory", "Id", RequireNotNullable: true),
        new("UserEmploymentHistory.UserId", "UserEmploymentHistory", "UserId", RequireNotNullable: true),
        new("UserEmploymentHistory.PreviousEmploymentFacility", "UserEmploymentHistory", "PreviousEmploymentFacility", RequireNotNullable: true),
        new("UserEmploymentHistory.EmploymentFacility", "UserEmploymentHistory", "EmploymentFacility", RequireNotNullable: true),
        new("UserEmploymentHistory.EffectiveAt", "UserEmploymentHistory", "EffectiveAt", RequireNotNullable: true),
        new("UserEmploymentHistory.ChangedByUserId", "UserEmploymentHistory", "ChangedByUserId", RequireNullable: true),
        new("UserEmploymentHistory.ChangedAt", "UserEmploymentHistory", "ChangedAt", RequireNotNullable: true),
        new("Users.EmploymentFacility", "Users", "EmploymentFacility", RequireNotNullable: true),
        new("Users.EmploymentEffectiveAt", "Users", "EmploymentEffectiveAt", RequireNullable: true),
        new("Users.EmploymentUpdatedAt", "Users", "EmploymentUpdatedAt", RequireNullable: true),
        new("Users.EmploymentUpdatedByUserId", "Users", "EmploymentUpdatedByUserId", RequireNullable: true),
        new("ActualRuns.RunFacilityWarehouseId", "ActualRuns", "RunFacilityWarehouseId", RequireNullable: true),
        new("ActualRuns.RunFacilityCodeSnapshot", "ActualRuns", "RunFacilityCodeSnapshot", RequireNullable: true),
        new("ActualRuns.RunFacilityAssignmentSource", "ActualRuns", "RunFacilityAssignmentSource", RequireNullable: true),
        new("ActualRuns.RunFacilityAssignedAt", "ActualRuns", "RunFacilityAssignedAt", RequireNullable: true),
        new("ActualRuns.RunFacilityAssignedByUserId", "ActualRuns", "RunFacilityAssignedByUserId", RequireNullable: true),
        new("ActualRunOverrideRequests.RunFacilityWarehouseId", "ActualRunOverrideRequests", "RunFacilityWarehouseId", RequireNullable: true),
        new("ActualRunOverrideRequests.RunFacilityCodeSnapshot", "ActualRunOverrideRequests", "RunFacilityCodeSnapshot", RequireNullable: true),
        new("ActualRunOverrideRequests.RunFacilityAssignmentSource", "ActualRunOverrideRequests", "RunFacilityAssignmentSource", RequireNullable: true),
        new("BinsRunEntries.ReportingFacilityWarehouseId", "BinsRunEntries", "ReportingFacilityWarehouseId", RequireNullable: true),
        new("BinsRunEntries.ReportingFacilityCodeSnapshot", "BinsRunEntries", "ReportingFacilityCodeSnapshot", RequireNullable: true),
        new("BinsRunEntries.ReportingFacilityAssignmentSource", "BinsRunEntries", "ReportingFacilityAssignmentSource", RequireNullable: true),
        new("BinsRunEntries.ReportingFacilityAssignedAt", "BinsRunEntries", "ReportingFacilityAssignedAt", RequireNullable: true),
        new("BinsRunEntries.ReportingFacilityAssignedByUserId", "BinsRunEntries", "ReportingFacilityAssignedByUserId", RequireNullable: true),
        new("BinsRunEntries.ReportingCropYearSnapshot", "BinsRunEntries", "ReportingCropYearSnapshot", RequireNullable: true),
        new("BinsRunEntries.ReportingFruitProfileIdSnapshot", "BinsRunEntries", "ReportingFruitProfileIdSnapshot", RequireNullable: true),
        new("BinsRunEntries.ReportingVarietyCodeSnapshot", "BinsRunEntries", "ReportingVarietyCodeSnapshot", RequireNullable: true),
        new("BinsRunEntries.ProductionTypeSnapshot", "BinsRunEntries", "ProductionTypeSnapshot", RequireNullable: true),
        new("BinsRunEntries.IsOrganicSnapshot", "BinsRunEntries", "IsOrganicSnapshot", RequireNullable: true),
        new("BinsRunEntries.GrowerNumberSnapshot", "BinsRunEntries", "GrowerNumberSnapshot", RequireNullable: true),
        new("Receipts.ConcurrencyVersion", "Receipts", "ConcurrencyVersion", RequireNotNullable: true),
        new("ReceiptInventoryOverrides", "ReceiptInventoryOverrides", null),
        new("ReceiptInventoryOverrides.Id", "ReceiptInventoryOverrides", "Id", RequireNotNullable: true),
        new("ReceiptInventoryOverrides.ReceiptId", "ReceiptInventoryOverrides", "ReceiptId", RequireNotNullable: true),
        new("ReceiptInventoryOverrides.AdministratorUserId", "ReceiptInventoryOverrides", "AdministratorUserId", RequireNotNullable: true),
        new("ReceiptInventoryOverrides.OperationKey", "ReceiptInventoryOverrides", "OperationKey", RequireNotNullable: true),
        new("ReceiptInventoryOverrides.IsComplete", "ReceiptInventoryOverrides", "IsComplete", RequireNotNullable: true),
        new("RoomInventoryAdjustments.ReceiptInventoryOverrideId", "RoomInventoryAdjustments", "ReceiptInventoryOverrideId", RequireNullable: true),
        new("EndOfDayFillReportGroups", "EndOfDayFillReportGroups", null),
        new("EndOfDayFillReportGroups.Facility", "EndOfDayFillReportGroups", "Facility", RequireNotNullable: true),
        new("EndOfDayFillReportGroups.WarehouseId", "EndOfDayFillReportGroups", "WarehouseId", RequireNotNullable: true),
        new("Rooms.EndOfDayFillReportGroupId", "Rooms", "EndOfDayFillReportGroupId", RequireNullable: true),
        new("EndOfDayFillReportRecipients", "EndOfDayFillReportRecipients", null),
        new("EndOfDayFillReportRecipients.NormalizedEmailAddress", "EndOfDayFillReportRecipients", "NormalizedEmailAddress", RequireNotNullable: true),
        new("EndOfDayFillUserGroupAssignments", "EndOfDayFillUserGroupAssignments", null),
        new("EndOfDayFillUserGroupAssignments.UserId", "EndOfDayFillUserGroupAssignments", "UserId", RequireNotNullable: true),
        new("EndOfDayFillUserGroupAssignments.ReportGroupId", "EndOfDayFillUserGroupAssignments", "ReportGroupId", RequireNotNullable: true),
        new("EndOfDayFillReportSends", "EndOfDayFillReportSends", null),
        new("EndOfDayFillReportSends.PacificReportDate", "EndOfDayFillReportSends", "PacificReportDate", RequireNotNullable: true),
        new("EndOfDayFillReportSends.RevisionNumber", "EndOfDayFillReportSends", "RevisionNumber", RequireNotNullable: true),
        new("EndOfDayFillReportSends.SnapshotHash", "EndOfDayFillReportSends", "SnapshotHash", RequireNotNullable: true),
        new("EndOfDayFillReportSends.SnapshotJson", "EndOfDayFillReportSends", "SnapshotJson", RequireNotNullable: true),
        new("EndOfDayFillReportSends.Status", "EndOfDayFillReportSends", "Status", RequireNotNullable: true),
        new("EndOfDayFillSendReservations", "EndOfDayFillSendReservations", null),
        new("EndOfDayFillSendReservations.SendAttemptId", "EndOfDayFillSendReservations", "SendAttemptId", RequireNotNullable: true),
        new("Roles.IsActive", "Roles", "IsActive", RequireNotNullable: true),
        new("Roles.NormalizedName", "Roles", "NormalizedName", RequireNotNullable: true),
        new("RolePageAccesses", "RolePageAccesses", null),
        new("RolePageAccesses.RoleId", "RolePageAccesses", "RoleId", RequireNotNullable: true),
        new("RolePageAccesses.AreaKey", "RolePageAccesses", "AreaKey", RequireNotNullable: true),
        new("RolePageAccesses.AccessLevel", "RolePageAccesses", "AccessLevel", RequireNotNullable: true),
        new("RolePageAccesses.UpdatedByUserId", "RolePageAccesses", "UpdatedByUserId", RequireNullable: true),
        new("RolePageAccesses.UpdatedAt", "RolePageAccesses", "UpdatedAt", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments", "InventoryDiagnosticAcknowledgments", null),
        new("InventoryDiagnosticAcknowledgments.Id", "InventoryDiagnosticAcknowledgments", "Id", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DiagnosticKey", "InventoryDiagnosticAcknowledgments", "DiagnosticKey", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DiagnosticType", "InventoryDiagnosticAcknowledgments", "DiagnosticType", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DiagnosticCode", "InventoryDiagnosticAcknowledgments", "DiagnosticCode", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DiagnosticMessage", "InventoryDiagnosticAcknowledgments", "DiagnosticMessage", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.RoomInventoryAdjustmentId", "InventoryDiagnosticAcknowledgments", "RoomInventoryAdjustmentId", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.InvariantVersion", "InventoryDiagnosticAcknowledgments", "InvariantVersion", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.Reason", "InventoryDiagnosticAcknowledgments", "Reason", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DiagnosticSnapshotJson", "InventoryDiagnosticAcknowledgments", "DiagnosticSnapshotJson", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DismissedByUserId", "InventoryDiagnosticAcknowledgments", "DismissedByUserId", RequireNullable: true),
        new("InventoryDiagnosticAcknowledgments.DismissedByEmail", "InventoryDiagnosticAcknowledgments", "DismissedByEmail", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.DismissedAt", "InventoryDiagnosticAcknowledgments", "DismissedAt", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.IsActive", "InventoryDiagnosticAcknowledgments", "IsActive", RequireNotNullable: true),
        new("InventoryDiagnosticAcknowledgments.RestoredByUserId", "InventoryDiagnosticAcknowledgments", "RestoredByUserId", RequireNullable: true),
        new("InventoryDiagnosticAcknowledgments.RestoredByEmail", "InventoryDiagnosticAcknowledgments", "RestoredByEmail", RequireNullable: true),
        new("InventoryDiagnosticAcknowledgments.RestoredAt", "InventoryDiagnosticAcknowledgments", "RestoredAt", RequireNullable: true),
        new("RoomInventoryAdjustments.RoomInventoryLossId", "RoomInventoryAdjustments", "RoomInventoryLossId", RequireNullable: true),
        new("RoomInventoryLosses", "RoomInventoryLosses", null),
        new("RoomInventoryLosses.Id", "RoomInventoryLosses", "Id", RequireNotNullable: true),
        new("RoomInventoryLosses.OperationKey", "RoomInventoryLosses", "OperationKey", RequireNotNullable: true),
        new("RoomInventoryLosses.WarehouseId", "RoomInventoryLosses", "WarehouseId", RequireNotNullable: true),
        new("RoomInventoryLosses.RoomId", "RoomInventoryLosses", "RoomId", RequireNotNullable: true),
        new("RoomInventoryLosses.ReceiptId", "RoomInventoryLosses", "ReceiptId", RequireNullable: true),
        new("RoomInventoryLosses.CropYear", "RoomInventoryLosses", "CropYear", RequireNullable: true),
        new("RoomInventoryLosses.GrowerLotId", "RoomInventoryLosses", "GrowerLotId", RequireNullable: true),
        new("RoomInventoryLosses.FruitProfileId", "RoomInventoryLosses", "FruitProfileId", RequireNullable: true),
        new("RoomInventoryLosses.GrowerName", "RoomInventoryLosses", "GrowerName", RequireNotNullable: true),
        new("RoomInventoryLosses.GrowerNumber", "RoomInventoryLosses", "GrowerNumber", RequireNullable: true),
        new("RoomInventoryLosses.LotNumber", "RoomInventoryLosses", "LotNumber", RequireNotNullable: true),
        new("RoomInventoryLosses.PoolStart", "RoomInventoryLosses", "PoolStart", RequireNullable: true),
        new("RoomInventoryLosses.VarietyCode", "RoomInventoryLosses", "VarietyCode", RequireNotNullable: true),
        new("RoomInventoryLosses.InventoryStatus", "RoomInventoryLosses", "InventoryStatus", RequireNullable: true),
        new("RoomInventoryLosses.LossType", "RoomInventoryLosses", "LossType", RequireNotNullable: true),
        new("RoomInventoryLosses.BinCount", "RoomInventoryLosses", "BinCount", RequireNotNullable: true),
        new("RoomInventoryLosses.Reason", "RoomInventoryLosses", "Reason", RequireNotNullable: true),
        new("RoomInventoryLosses.Notes", "RoomInventoryLosses", "Notes", RequireNullable: true),
        new("RoomInventoryLosses.OccurredAt", "RoomInventoryLosses", "OccurredAt", RequireNullable: true),
        new("RoomInventoryLosses.CreatedByUserId", "RoomInventoryLosses", "CreatedByUserId", RequireNotNullable: true),
        new("RoomInventoryLosses.CreatedAt", "RoomInventoryLosses", "CreatedAt", RequireNotNullable: true),
        new("RoomInventoryLosses.IsReversed", "RoomInventoryLosses", "IsReversed", RequireNotNullable: true),
        new("RoomInventoryLosses.ReversedAt", "RoomInventoryLosses", "ReversedAt", RequireNullable: true),
        new("RoomInventoryLosses.ReversedByUserId", "RoomInventoryLosses", "ReversedByUserId", RequireNullable: true),
        new("RoomInventoryLosses.ReverseReason", "RoomInventoryLosses", "ReverseReason", RequireNullable: true),
        new("BinsRunEntries.TreatmentStateSnapshot", "BinsRunEntries", "TreatmentStateSnapshot", RequireNullable: true),
        new("BinsRunEntries.TreatmentSignatureSnapshot", "BinsRunEntries", "TreatmentSignatureSnapshot", RequireNullable: true),
        new("BinsRunEntries.TreatmentSummarySnapshot", "BinsRunEntries", "TreatmentSummarySnapshot", RequireNullable: true),
        new("ActualRunOverrideRequestLines.TreatmentSignature", "ActualRunOverrideRequestLines", "TreatmentSignature", RequireNullable: true),
        new("TreatmentChemicals", "TreatmentChemicals", null),
        new("TreatmentChemicals.Id", "TreatmentChemicals", "Id", RequireNotNullable: true),
        new("TreatmentChemicals.ProductName", "TreatmentChemicals", "ProductName", RequireNotNullable: true),
        new("TreatmentChemicals.CommonName", "TreatmentChemicals", "CommonName", RequireNullable: true),
        new("TreatmentChemicals.Crop", "TreatmentChemicals", "Crop", RequireNotNullable: true),
        new("TreatmentChemicals.ApplicationLevel", "TreatmentChemicals", "ApplicationLevel", RequireNotNullable: true),
        new("TreatmentChemicals.Volume", "TreatmentChemicals", "Volume", RequireNotNullable: true),
        new("TreatmentChemicals.Unit", "TreatmentChemicals", "Unit", RequireNotNullable: true),
        new("TreatmentChemicals.UnitPrice", "TreatmentChemicals", "UnitPrice", RequireNotNullable: true),
        new("TreatmentChemicals.Currency", "TreatmentChemicals", "Currency", RequireNotNullable: true),
        new("TreatmentChemicals.IsActive", "TreatmentChemicals", "IsActive", RequireNotNullable: true),
        new("TreatmentChemicals.CreatedAt", "TreatmentChemicals", "CreatedAt", RequireNotNullable: true),
        new("TreatmentChemicals.CreatedByUserId", "TreatmentChemicals", "CreatedByUserId", RequireNullable: true),
        new("TreatmentChemicals.UpdatedAt", "TreatmentChemicals", "UpdatedAt", RequireNotNullable: true),
        new("TreatmentChemicals.UpdatedByUserId", "TreatmentChemicals", "UpdatedByUserId", RequireNullable: true),
        new("RoomTreatmentApplications", "RoomTreatmentApplications", null),
        new("RoomTreatmentApplications.Id", "RoomTreatmentApplications", "Id", RequireNotNullable: true),
        new("RoomTreatmentApplications.OperationKey", "RoomTreatmentApplications", "OperationKey", RequireNotNullable: true),
        new("RoomTreatmentApplications.ApplicationLevel", "RoomTreatmentApplications", "ApplicationLevel", RequireNotNullable: true),
        new("RoomTreatmentApplications.ReceiptId", "RoomTreatmentApplications", "ReceiptId", RequireNullable: true),
        new("RoomTreatmentApplications.TreatmentChemicalId", "RoomTreatmentApplications", "TreatmentChemicalId", RequireNotNullable: true),
        new("RoomTreatmentApplications.WarehouseId", "RoomTreatmentApplications", "WarehouseId", RequireNotNullable: true),
        new("RoomTreatmentApplications.RoomId", "RoomTreatmentApplications", "RoomId", RequireNotNullable: true),
        new("RoomTreatmentApplications.AppliedAt", "RoomTreatmentApplications", "AppliedAt", RequireNotNullable: true),
        new("RoomTreatmentApplications.AppliedByUserId", "RoomTreatmentApplications", "AppliedByUserId", RequireNotNullable: true),
        new("RoomTreatmentApplications.Notes", "RoomTreatmentApplications", "Notes", RequireNullable: true),
        new("RoomTreatmentApplications.TotalBinsSnapshot", "RoomTreatmentApplications", "TotalBinsSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.ProductNameSnapshot", "RoomTreatmentApplications", "ProductNameSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.CommonNameSnapshot", "RoomTreatmentApplications", "CommonNameSnapshot", RequireNullable: true),
        new("RoomTreatmentApplications.CropSnapshot", "RoomTreatmentApplications", "CropSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.VolumeSnapshot", "RoomTreatmentApplications", "VolumeSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.UnitSnapshot", "RoomTreatmentApplications", "UnitSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.UnitPriceSnapshot", "RoomTreatmentApplications", "UnitPriceSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.CurrencySnapshot", "RoomTreatmentApplications", "CurrencySnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.EstimatedCostSnapshot", "RoomTreatmentApplications", "EstimatedCostSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplications.CreatedAt", "RoomTreatmentApplications", "CreatedAt", RequireNotNullable: true),
        new("RoomTreatmentApplications.CreatedByUserId", "RoomTreatmentApplications", "CreatedByUserId", RequireNotNullable: true),
        new("RoomTreatmentApplications.ReversedAt", "RoomTreatmentApplications", "ReversedAt", RequireNullable: true),
        new("RoomTreatmentApplications.ReversedByUserId", "RoomTreatmentApplications", "ReversedByUserId", RequireNullable: true),
        new("RoomTreatmentApplications.ReversalReason", "RoomTreatmentApplications", "ReversalReason", RequireNullable: true),
        new("RoomTreatmentApplicationSources", "RoomTreatmentApplicationSources", null),
        new("RoomTreatmentApplicationSources.Id", "RoomTreatmentApplicationSources", "Id", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.RoomTreatmentApplicationId", "RoomTreatmentApplicationSources", "RoomTreatmentApplicationId", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.ReceiptId", "RoomTreatmentApplicationSources", "ReceiptId", RequireNullable: true),
        new("RoomTreatmentApplicationSources.CropYear", "RoomTreatmentApplicationSources", "CropYear", RequireNullable: true),
        new("RoomTreatmentApplicationSources.GrowerLotId", "RoomTreatmentApplicationSources", "GrowerLotId", RequireNullable: true),
        new("RoomTreatmentApplicationSources.FruitProfileId", "RoomTreatmentApplicationSources", "FruitProfileId", RequireNullable: true),
        new("RoomTreatmentApplicationSources.IdentityKey", "RoomTreatmentApplicationSources", "IdentityKey", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.GrowerNumberSnapshot", "RoomTreatmentApplicationSources", "GrowerNumberSnapshot", RequireNullable: true),
        new("RoomTreatmentApplicationSources.GrowerNameSnapshot", "RoomTreatmentApplicationSources", "GrowerNameSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.LotNumberSnapshot", "RoomTreatmentApplicationSources", "LotNumberSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.VarietyCodeSnapshot", "RoomTreatmentApplicationSources", "VarietyCodeSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.ProductionTypeSnapshot", "RoomTreatmentApplicationSources", "ProductionTypeSnapshot", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.IsOrganicSnapshot", "RoomTreatmentApplicationSources", "IsOrganicSnapshot", RequireNullable: true),
        new("RoomTreatmentApplicationSources.InventoryStatusSnapshot", "RoomTreatmentApplicationSources", "InventoryStatusSnapshot", RequireNullable: true),
        new("RoomTreatmentApplicationSources.BinsTreated", "RoomTreatmentApplicationSources", "BinsTreated", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.PriorTreatmentSignature", "RoomTreatmentApplicationSources", "PriorTreatmentSignature", RequireNotNullable: true),
        new("RoomTreatmentApplicationSources.ResultTreatmentSignature", "RoomTreatmentApplicationSources", "ResultTreatmentSignature", RequireNotNullable: true),
        new("TreatmentLineageSegments", "TreatmentLineageSegments", null),
        new("TreatmentLineageSegments.Id", "TreatmentLineageSegments", "Id", RequireNotNullable: true),
        new("TreatmentLineageSegments.WarehouseId", "TreatmentLineageSegments", "WarehouseId", RequireNotNullable: true),
        new("TreatmentLineageSegments.RoomId", "TreatmentLineageSegments", "RoomId", RequireNotNullable: true),
        new("TreatmentLineageSegments.ReceiptId", "TreatmentLineageSegments", "ReceiptId", RequireNullable: true),
        new("TreatmentLineageSegments.CropYear", "TreatmentLineageSegments", "CropYear", RequireNullable: true),
        new("TreatmentLineageSegments.GrowerLotId", "TreatmentLineageSegments", "GrowerLotId", RequireNullable: true),
        new("TreatmentLineageSegments.FruitProfileId", "TreatmentLineageSegments", "FruitProfileId", RequireNullable: true),
        new("TreatmentLineageSegments.IdentityKey", "TreatmentLineageSegments", "IdentityKey", RequireNotNullable: true),
        new("TreatmentLineageSegments.GrowerNumberSnapshot", "TreatmentLineageSegments", "GrowerNumberSnapshot", RequireNullable: true),
        new("TreatmentLineageSegments.GrowerNameSnapshot", "TreatmentLineageSegments", "GrowerNameSnapshot", RequireNotNullable: true),
        new("TreatmentLineageSegments.LotNumberSnapshot", "TreatmentLineageSegments", "LotNumberSnapshot", RequireNotNullable: true),
        new("TreatmentLineageSegments.VarietyCodeSnapshot", "TreatmentLineageSegments", "VarietyCodeSnapshot", RequireNotNullable: true),
        new("TreatmentLineageSegments.ProductionTypeSnapshot", "TreatmentLineageSegments", "ProductionTypeSnapshot", RequireNotNullable: true),
        new("TreatmentLineageSegments.IsOrganicSnapshot", "TreatmentLineageSegments", "IsOrganicSnapshot", RequireNullable: true),
        new("TreatmentLineageSegments.InventoryStatusSnapshot", "TreatmentLineageSegments", "InventoryStatusSnapshot", RequireNullable: true),
        new("TreatmentLineageSegments.TreatmentState", "TreatmentLineageSegments", "TreatmentState", RequireNotNullable: true),
        new("TreatmentLineageSegments.TreatmentSignature", "TreatmentLineageSegments", "TreatmentSignature", RequireNotNullable: true),
        new("TreatmentLineageSegments.CurrentBins", "TreatmentLineageSegments", "CurrentBins", RequireNotNullable: true),
        new("TreatmentLineageSegments.CreatedAt", "TreatmentLineageSegments", "CreatedAt", RequireNotNullable: true),
        new("TreatmentLineageSegments.UpdatedAt", "TreatmentLineageSegments", "UpdatedAt", RequireNotNullable: true),
        new("TreatmentLineageSegments.ConcurrencyVersion", "TreatmentLineageSegments", "ConcurrencyVersion", RequireNotNullable: true),
        new("TreatmentLineageSegmentApplications", "TreatmentLineageSegmentApplications", null),
        new("TreatmentLineageSegmentApplications.TreatmentLineageSegmentId", "TreatmentLineageSegmentApplications", "TreatmentLineageSegmentId", RequireNotNullable: true),
        new("TreatmentLineageSegmentApplications.RoomTreatmentApplicationId", "TreatmentLineageSegmentApplications", "RoomTreatmentApplicationId", RequireNotNullable: true),
        new("TreatmentLineageSegmentApplications.Sequence", "TreatmentLineageSegmentApplications", "Sequence", RequireNotNullable: true),
        new("TreatmentLineageMovements", "TreatmentLineageMovements", null),
        new("TreatmentLineageMovements.Id", "TreatmentLineageMovements", "Id", RequireNotNullable: true),
        new("TreatmentLineageMovements.OperationKey", "TreatmentLineageMovements", "OperationKey", RequireNotNullable: true),
        new("TreatmentLineageMovements.MovementType", "TreatmentLineageMovements", "MovementType", RequireNotNullable: true),
        new("TreatmentLineageMovements.SourceSegmentId", "TreatmentLineageMovements", "SourceSegmentId", RequireNullable: true),
        new("TreatmentLineageMovements.DestinationSegmentId", "TreatmentLineageMovements", "DestinationSegmentId", RequireNullable: true),
        new("TreatmentLineageMovements.SourceRoomId", "TreatmentLineageMovements", "SourceRoomId", RequireNullable: true),
        new("TreatmentLineageMovements.DestinationRoomId", "TreatmentLineageMovements", "DestinationRoomId", RequireNullable: true),
        new("TreatmentLineageMovements.ReceiptId", "TreatmentLineageMovements", "ReceiptId", RequireNullable: true),
        new("TreatmentLineageMovements.IdentityKey", "TreatmentLineageMovements", "IdentityKey", RequireNotNullable: true),
        new("TreatmentLineageMovements.TreatmentStateSnapshot", "TreatmentLineageMovements", "TreatmentStateSnapshot", RequireNotNullable: true),
        new("TreatmentLineageMovements.TreatmentSignatureSnapshot", "TreatmentLineageMovements", "TreatmentSignatureSnapshot", RequireNotNullable: true),
        new("TreatmentLineageMovements.BinCount", "TreatmentLineageMovements", "BinCount", RequireNotNullable: true),
        new("TreatmentLineageMovements.RoomTransferId", "TreatmentLineageMovements", "RoomTransferId", RequireNullable: true),
        new("TreatmentLineageMovements.RoomInventoryLossId", "TreatmentLineageMovements", "RoomInventoryLossId", RequireNullable: true),
        new("TreatmentLineageMovements.BinsRunEntryId", "TreatmentLineageMovements", "BinsRunEntryId", RequireNullable: true),
        new("TreatmentLineageMovements.ReversesTreatmentLineageMovementId", "TreatmentLineageMovements", "ReversesTreatmentLineageMovementId", RequireNullable: true),
        new("TreatmentLineageMovements.OccurredAt", "TreatmentLineageMovements", "OccurredAt", RequireNotNullable: true),
        new("TreatmentLineageMovements.CreatedByUserId", "TreatmentLineageMovements", "CreatedByUserId", RequireNullable: true),
        new("TreatmentLineageMovements.CreatedAt", "TreatmentLineageMovements", "CreatedAt", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments", "RoomTreatmentApplicationAttachments", null),
        new("RoomTreatmentApplicationAttachments.Id", "RoomTreatmentApplicationAttachments", "Id", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.RoomTreatmentApplicationId", "RoomTreatmentApplicationAttachments", "RoomTreatmentApplicationId", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.OperationKey", "RoomTreatmentApplicationAttachments", "OperationKey", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.FileName", "RoomTreatmentApplicationAttachments", "FileName", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.ContentType", "RoomTreatmentApplicationAttachments", "ContentType", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.FileSizeBytes", "RoomTreatmentApplicationAttachments", "FileSizeBytes", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.StorageProvider", "RoomTreatmentApplicationAttachments", "StorageProvider", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.DriveId", "RoomTreatmentApplicationAttachments", "DriveId", RequireNullable: true),
        new("RoomTreatmentApplicationAttachments.FileId", "RoomTreatmentApplicationAttachments", "FileId", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.FolderId", "RoomTreatmentApplicationAttachments", "FolderId", RequireNullable: true),
        new("RoomTreatmentApplicationAttachments.StoragePath", "RoomTreatmentApplicationAttachments", "StoragePath", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.CreatedAt", "RoomTreatmentApplicationAttachments", "CreatedAt", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.CreatedByUserId", "RoomTreatmentApplicationAttachments", "CreatedByUserId", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.IsDeleted", "RoomTreatmentApplicationAttachments", "IsDeleted", RequireNotNullable: true),
        new("RoomTreatmentApplicationAttachments.DeletedAt", "RoomTreatmentApplicationAttachments", "DeletedAt", RequireNullable: true),
        new("RoomTreatmentApplicationAttachments.DeletedByUserId", "RoomTreatmentApplicationAttachments", "DeletedByUserId", RequireNullable: true),
        new("RoomTreatmentApplicationAttachments.DeleteReason", "RoomTreatmentApplicationAttachments", "DeleteReason", RequireNullable: true),
        new("RoomInventoryAdjustments.ProcessorShipmentLineId", "RoomInventoryAdjustments", "ProcessorShipmentLineId", RequireNullable: true),
        new("TreatmentLineageMovements.ProcessorShipmentLineId", "TreatmentLineageMovements", "ProcessorShipmentLineId", RequireNullable: true),
        new("Processors", "Processors", null),
        new("Processors.Id", "Processors", "Id", RequireNotNullable: true),
        new("Processors.Name", "Processors", "Name", RequireNotNullable: true),
        new("Processors.Code", "Processors", "Code", RequireNullable: true),
        new("Processors.IsActive", "Processors", "IsActive", RequireNotNullable: true),
        new("Processors.Notes", "Processors", "Notes", RequireNullable: true),
        new("Processors.CreatedAt", "Processors", "CreatedAt", RequireNotNullable: true),
        new("Processors.CreatedByUserId", "Processors", "CreatedByUserId", RequireNullable: true),
        new("Processors.UpdatedAt", "Processors", "UpdatedAt", RequireNotNullable: true),
        new("Processors.UpdatedByUserId", "Processors", "UpdatedByUserId", RequireNullable: true),
        new("ProcessorShipments", "ProcessorShipments", null),
        new("ProcessorShipments.Id", "ProcessorShipments", "Id", RequireNotNullable: true),
        new("ProcessorShipments.OperationKey", "ProcessorShipments", "OperationKey", RequireNotNullable: true),
        new("ProcessorShipments.ProcessorId", "ProcessorShipments", "ProcessorId", RequireNotNullable: true),
        new("ProcessorShipments.ProcessorNameSnapshot", "ProcessorShipments", "ProcessorNameSnapshot", RequireNotNullable: true),
        new("ProcessorShipments.ShippedAt", "ProcessorShipments", "ShippedAt", RequireNotNullable: true),
        new("ProcessorShipments.OriginalSaleRate", "ProcessorShipments", "OriginalSaleRate", RequireNotNullable: true),
        new("ProcessorShipments.OriginalPricingBasis", "ProcessorShipments", "OriginalPricingBasis", RequireNotNullable: true),
        new("ProcessorShipments.SaleRate", "ProcessorShipments", "SaleRate", RequireNotNullable: true),
        new("ProcessorShipments.PricingBasis", "ProcessorShipments", "PricingBasis", RequireNotNullable: true),
        new("ProcessorShipments.Currency", "ProcessorShipments", "Currency", RequireNotNullable: true),
        new("ProcessorShipments.ReferenceNumber", "ProcessorShipments", "ReferenceNumber", RequireNullable: true),
        new("ProcessorShipments.Notes", "ProcessorShipments", "Notes", RequireNullable: true),
        new("ProcessorShipments.CreatedByUserId", "ProcessorShipments", "CreatedByUserId", RequireNotNullable: true),
        new("ProcessorShipments.CreatedAt", "ProcessorShipments", "CreatedAt", RequireNotNullable: true),
        new("ProcessorShipments.ReversedAt", "ProcessorShipments", "ReversedAt", RequireNullable: true),
        new("ProcessorShipments.ReversedByUserId", "ProcessorShipments", "ReversedByUserId", RequireNullable: true),
        new("ProcessorShipments.ReversalReason", "ProcessorShipments", "ReversalReason", RequireNullable: true),
        new("ProcessorShipments.ConcurrencyVersion", "ProcessorShipments", "ConcurrencyVersion", RequireNotNullable: true),
        new("ProcessorShipmentLines", "ProcessorShipmentLines", null),
        new("ProcessorShipmentLines.Id", "ProcessorShipmentLines", "Id", RequireNotNullable: true),
        new("ProcessorShipmentLines.ProcessorShipmentId", "ProcessorShipmentLines", "ProcessorShipmentId", RequireNotNullable: true),
        new("ProcessorShipmentLines.WarehouseId", "ProcessorShipmentLines", "WarehouseId", RequireNotNullable: true),
        new("ProcessorShipmentLines.RoomId", "ProcessorShipmentLines", "RoomId", RequireNotNullable: true),
        new("ProcessorShipmentLines.CropYear", "ProcessorShipmentLines", "CropYear", RequireNullable: true),
        new("ProcessorShipmentLines.ReceiptId", "ProcessorShipmentLines", "ReceiptId", RequireNullable: true),
        new("ProcessorShipmentLines.SourceInventoryAdjustmentId", "ProcessorShipmentLines", "SourceInventoryAdjustmentId", RequireNullable: true),
        new("ProcessorShipmentLines.GrowerLotId", "ProcessorShipmentLines", "GrowerLotId", RequireNullable: true),
        new("ProcessorShipmentLines.FruitProfileId", "ProcessorShipmentLines", "FruitProfileId", RequireNullable: true),
        new("ProcessorShipmentLines.GrowerNumberSnapshot", "ProcessorShipmentLines", "GrowerNumberSnapshot", RequireNullable: true),
        new("ProcessorShipmentLines.GrowerNameSnapshot", "ProcessorShipmentLines", "GrowerNameSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.LotNumberSnapshot", "ProcessorShipmentLines", "LotNumberSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.VarietyCodeSnapshot", "ProcessorShipmentLines", "VarietyCodeSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.ProductionTypeSnapshot", "ProcessorShipmentLines", "ProductionTypeSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.IsOrganicSnapshot", "ProcessorShipmentLines", "IsOrganicSnapshot", RequireNullable: true),
        new("ProcessorShipmentLines.InventoryStatusSnapshot", "ProcessorShipmentLines", "InventoryStatusSnapshot", RequireNullable: true),
        new("ProcessorShipmentLines.TreatmentStateSnapshot", "ProcessorShipmentLines", "TreatmentStateSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.TreatmentSignatureSnapshot", "ProcessorShipmentLines", "TreatmentSignatureSnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.TreatmentSummarySnapshot", "ProcessorShipmentLines", "TreatmentSummarySnapshot", RequireNotNullable: true),
        new("ProcessorShipmentLines.BinsSent", "ProcessorShipmentLines", "BinsSent", RequireNotNullable: true),
        new("ProcessorShipmentLines.PoundsPerBinSnapshot", "ProcessorShipmentLines", "PoundsPerBinSnapshot", RequireNullable: true),
        new("ProcessorShipmentPriceCorrections", "ProcessorShipmentPriceCorrections", null),
        new("ProcessorShipmentPriceCorrections.Id", "ProcessorShipmentPriceCorrections", "Id", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.ProcessorShipmentId", "ProcessorShipmentPriceCorrections", "ProcessorShipmentId", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.OperationKey", "ProcessorShipmentPriceCorrections", "OperationKey", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.OriginalSaleRate", "ProcessorShipmentPriceCorrections", "OriginalSaleRate", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.OriginalPricingBasis", "ProcessorShipmentPriceCorrections", "OriginalPricingBasis", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.CorrectedSaleRate", "ProcessorShipmentPriceCorrections", "CorrectedSaleRate", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.CorrectedPricingBasis", "ProcessorShipmentPriceCorrections", "CorrectedPricingBasis", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.Reason", "ProcessorShipmentPriceCorrections", "Reason", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.CorrectedByUserId", "ProcessorShipmentPriceCorrections", "CorrectedByUserId", RequireNotNullable: true),
        new("ProcessorShipmentPriceCorrections.CorrectedAt", "ProcessorShipmentPriceCorrections", "CorrectedAt", RequireNotNullable: true),
        new("Rooms.IsSealed", "Rooms", "IsSealed", RequireNotNullable: true),
        new("Rooms.SealedAt", "Rooms", "SealedAt", RequireNullable: true),
        new("Rooms.SealRecordedAt", "Rooms", "SealRecordedAt", RequireNullable: true),
        new("Rooms.SealedByUserId", "Rooms", "SealedByUserId", RequireNullable: true),
        new("RoomSealEvents", "RoomSealEvents", null),
        new("RoomSealEvents.Id", "RoomSealEvents", "Id", RequireNotNullable: true),
        new("RoomSealEvents.RoomId", "RoomSealEvents", "RoomId", RequireNotNullable: true),
        new("RoomSealEvents.Action", "RoomSealEvents", "Action", RequireNotNullable: true),
        new("RoomSealEvents.EffectiveAt", "RoomSealEvents", "EffectiveAt", RequireNotNullable: true),
        new("RoomSealEvents.PreviousEffectiveAt", "RoomSealEvents", "PreviousEffectiveAt", RequireNullable: true),
        new("RoomSealEvents.ChangedAt", "RoomSealEvents", "ChangedAt", RequireNotNullable: true),
        new("RoomSealEvents.ChangedByUserId", "RoomSealEvents", "ChangedByUserId", RequireNotNullable: true),
        new("RoomSealEvents.WarehouseCodeSnapshot", "RoomSealEvents", "WarehouseCodeSnapshot", RequireNotNullable: true),
        new("RoomSealEvents.RoomCodeSnapshot", "RoomSealEvents", "RoomCodeSnapshot", RequireNotNullable: true),
        new("RoomSealEvents.Note", "RoomSealEvents", "Note", RequireNullable: true),
        new("OutsideWarehouses", "OutsideWarehouses", null),
        new("OutsideWarehouses.Id", "OutsideWarehouses", "Id", RequireNotNullable: true),
        new("OutsideWarehouses.Name", "OutsideWarehouses", "Name", RequireNotNullable: true),
        new("OutsideWarehouses.Code", "OutsideWarehouses", "Code", RequireNotNullable: true),
        new("OutsideWarehouses.Address", "OutsideWarehouses", "Address", RequireNullable: true),
        new("OutsideWarehouses.Notes", "OutsideWarehouses", "Notes", RequireNullable: true),
        new("OutsideWarehouses.IsActive", "OutsideWarehouses", "IsActive", RequireNotNullable: true),
        new("OutsideWarehouses.CreatedAt", "OutsideWarehouses", "CreatedAt", RequireNotNullable: true),
        new("OutsideWarehouses.CreatedByUserId", "OutsideWarehouses", "CreatedByUserId", RequireNullable: true),
        new("OutsideWarehouses.UpdatedAt", "OutsideWarehouses", "UpdatedAt", RequireNotNullable: true),
        new("OutsideWarehouses.UpdatedByUserId", "OutsideWarehouses", "UpdatedByUserId", RequireNullable: true),
        new("OutsideWarehouseTransfers", "OutsideWarehouseTransfers", null),
        new("OutsideWarehouseTransfers.Id", "OutsideWarehouseTransfers", "Id", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.OperationKey", "OutsideWarehouseTransfers", "OperationKey", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.OutsideWarehouseId", "OutsideWarehouseTransfers", "OutsideWarehouseId", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.OutsideWarehouseCodeSnapshot", "OutsideWarehouseTransfers", "OutsideWarehouseCodeSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.OutsideWarehouseNameSnapshot", "OutsideWarehouseTransfers", "OutsideWarehouseNameSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.OutsideWarehouseAddressSnapshot", "OutsideWarehouseTransfers", "OutsideWarehouseAddressSnapshot", RequireNullable: true),
        new("OutsideWarehouseTransfers.SourceWarehouseId", "OutsideWarehouseTransfers", "SourceWarehouseId", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.SourceRoomId", "OutsideWarehouseTransfers", "SourceRoomId", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.ReceiptId", "OutsideWarehouseTransfers", "ReceiptId", RequireNullable: true),
        new("OutsideWarehouseTransfers.SourceInventoryAdjustmentId", "OutsideWarehouseTransfers", "SourceInventoryAdjustmentId", RequireNullable: true),
        new("OutsideWarehouseTransfers.CropYear", "OutsideWarehouseTransfers", "CropYear", RequireNullable: true),
        new("OutsideWarehouseTransfers.GrowerLotId", "OutsideWarehouseTransfers", "GrowerLotId", RequireNullable: true),
        new("OutsideWarehouseTransfers.FruitProfileId", "OutsideWarehouseTransfers", "FruitProfileId", RequireNullable: true),
        new("OutsideWarehouseTransfers.GrowerNumberSnapshot", "OutsideWarehouseTransfers", "GrowerNumberSnapshot", RequireNullable: true),
        new("OutsideWarehouseTransfers.GrowerNameSnapshot", "OutsideWarehouseTransfers", "GrowerNameSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.LotNumberSnapshot", "OutsideWarehouseTransfers", "LotNumberSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.VarietyCodeSnapshot", "OutsideWarehouseTransfers", "VarietyCodeSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.ProductionTypeSnapshot", "OutsideWarehouseTransfers", "ProductionTypeSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.IsOrganicSnapshot", "OutsideWarehouseTransfers", "IsOrganicSnapshot", RequireNullable: true),
        new("OutsideWarehouseTransfers.InventoryStatusSnapshot", "OutsideWarehouseTransfers", "InventoryStatusSnapshot", RequireNullable: true),
        new("OutsideWarehouseTransfers.TreatmentStateSnapshot", "OutsideWarehouseTransfers", "TreatmentStateSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.TreatmentSignatureSnapshot", "OutsideWarehouseTransfers", "TreatmentSignatureSnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.TreatmentSummarySnapshot", "OutsideWarehouseTransfers", "TreatmentSummarySnapshot", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.BinCount", "OutsideWarehouseTransfers", "BinCount", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.TransferredAt", "OutsideWarehouseTransfers", "TransferredAt", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.TruckLoadBolNumber", "OutsideWarehouseTransfers", "TruckLoadBolNumber", RequireNullable: true),
        new("OutsideWarehouseTransfers.Notes", "OutsideWarehouseTransfers", "Notes", RequireNullable: true),
        new("OutsideWarehouseTransfers.CreatedByUserId", "OutsideWarehouseTransfers", "CreatedByUserId", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.CreatedAt", "OutsideWarehouseTransfers", "CreatedAt", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.IsReversed", "OutsideWarehouseTransfers", "IsReversed", RequireNotNullable: true),
        new("OutsideWarehouseTransfers.ReversalOperationKey", "OutsideWarehouseTransfers", "ReversalOperationKey", RequireNullable: true),
        new("OutsideWarehouseTransfers.ReversedAt", "OutsideWarehouseTransfers", "ReversedAt", RequireNullable: true),
        new("OutsideWarehouseTransfers.ReversedByUserId", "OutsideWarehouseTransfers", "ReversedByUserId", RequireNullable: true),
        new("OutsideWarehouseTransfers.ReverseReason", "OutsideWarehouseTransfers", "ReverseReason", RequireNullable: true),
        new("OutsideWarehouseTransfers.ConcurrencyVersion", "OutsideWarehouseTransfers", "ConcurrencyVersion", RequireNotNullable: true),
        new("RoomInventoryAdjustments.OutsideWarehouseTransferId", "RoomInventoryAdjustments", "OutsideWarehouseTransferId", RequireNullable: true),
        new("TreatmentLineageMovements.OutsideWarehouseTransferId", "TreatmentLineageMovements", "OutsideWarehouseTransferId", RequireNullable: true)
        ,new("InterCrewTransfers", "InterCrewTransfers", null)
        ,new("InterCrewTransfers.Id", "InterCrewTransfers", "Id", RequireNotNullable: true)
        ,new("InterCrewTransfers.OperationKey", "InterCrewTransfers", "OperationKey", RequireNotNullable: true)
        ,new("InterCrewTransfers.SourceWarehouseId", "InterCrewTransfers", "SourceWarehouseId", RequireNotNullable: true)
        ,new("InterCrewTransfers.SourceRoomId", "InterCrewTransfers", "SourceRoomId", RequireNotNullable: true)
        ,new("InterCrewTransfers.DestinationCustodyGroup", "InterCrewTransfers", "DestinationCustodyGroup", RequireNotNullable: true)
        ,new("InterCrewTransfers.DestinationWarehouseId", "InterCrewTransfers", "DestinationWarehouseId", RequireNullable: true)
        ,new("InterCrewTransfers.DestinationRoomId", "InterCrewTransfers", "DestinationRoomId", RequireNullable: true)
        ,new("InterCrewTransfers.BinsLoaded", "InterCrewTransfers", "BinsLoaded", RequireNotNullable: true)
        ,new("InterCrewTransfers.BinsReceived", "InterCrewTransfers", "BinsReceived", RequireNullable: true)
        ,new("InterCrewTransfers.VarianceBins", "InterCrewTransfers", "VarianceBins", RequireNullable: true)
        ,new("InterCrewTransfers.Status", "InterCrewTransfers", "Status", RequireNotNullable: true)
        ,new("InterCrewTransfers.ReceiveOperationKey", "InterCrewTransfers", "ReceiveOperationKey", RequireNullable: true)
        ,new("InterCrewTransfers.ReviewOperationKey", "InterCrewTransfers", "ReviewOperationKey", RequireNullable: true)
        ,new("InterCrewTransfers.ReversalOperationKey", "InterCrewTransfers", "ReversalOperationKey", RequireNullable: true)
        ,new("RoomInventoryAdjustments.InterCrewTransferId", "RoomInventoryAdjustments", "InterCrewTransferId", RequireNullable: true)
        ,new("TreatmentLineageMovements.InterCrewTransferId", "TreatmentLineageMovements", "InterCrewTransferId", RequireNullable: true)
        ,new("InventoryIdentityCorrections", "InventoryIdentityCorrections", null)
        ,new("InventoryIdentityCorrections.Id", "InventoryIdentityCorrections", "Id", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.OperationKey", "InventoryIdentityCorrections", "OperationKey", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.SourceCropYear", "InventoryIdentityCorrections", "SourceCropYear", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.SourceGrowerLotId", "InventoryIdentityCorrections", "SourceGrowerLotId", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.SourceFruitProfileId", "InventoryIdentityCorrections", "SourceFruitProfileId", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.TargetCropYear", "InventoryIdentityCorrections", "TargetCropYear", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.TargetGrowerLotId", "InventoryIdentityCorrections", "TargetGrowerLotId", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.TargetFruitProfileId", "InventoryIdentityCorrections", "TargetFruitProfileId", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.CorrectedReceiptId", "InventoryIdentityCorrections", "CorrectedReceiptId", RequireNullable: true)
        ,new("InventoryIdentityCorrections.ReceiptInventoryOverrideId", "InventoryIdentityCorrections", "ReceiptInventoryOverrideId", RequireNullable: true)
        ,new("InventoryIdentityCorrections.Reason", "InventoryIdentityCorrections", "Reason", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.CreatedByUserId", "InventoryIdentityCorrections", "CreatedByUserId", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.CreatedAt", "InventoryIdentityCorrections", "CreatedAt", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.SourceIdentitySnapshotJson", "InventoryIdentityCorrections", "SourceIdentitySnapshotJson", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.TargetIdentitySnapshotJson", "InventoryIdentityCorrections", "TargetIdentitySnapshotJson", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.ExpectedAdjustmentCount", "InventoryIdentityCorrections", "ExpectedAdjustmentCount", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.ExpectedTreatmentMovementCount", "InventoryIdentityCorrections", "ExpectedTreatmentMovementCount", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.IsComplete", "InventoryIdentityCorrections", "IsComplete", RequireNotNullable: true)
        ,new("InventoryIdentityCorrections.IsActive", "InventoryIdentityCorrections", "IsActive", RequireNotNullable: true)
        ,new("RoomInventoryAdjustments.InventoryIdentityCorrectionId", "RoomInventoryAdjustments", "InventoryIdentityCorrectionId", RequireNullable: true)
        ,new("TreatmentLineageMovements.InventoryIdentityCorrectionId", "TreatmentLineageMovements", "InventoryIdentityCorrectionId", RequireNullable: true)
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredIndexExpectations =
    [
        new("IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted", "GrowerReportRecipients", "IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted"),
        new("IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress", "GrowerReportRecipients", "IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress", RequireUnique: true),
        new("IX_GrowerReportRecipients_CreatedByUserId", "GrowerReportRecipients", "IX_GrowerReportRecipients_CreatedByUserId"),
        new("IX_GrowerReportRecipients_DeletedByUserId", "GrowerReportRecipients", "IX_GrowerReportRecipients_DeletedByUserId"),
        new("IX_GrowerReportRecipients_UpdatedByUserId", "GrowerReportRecipients", "IX_GrowerReportRecipients_UpdatedByUserId"),
        new("IX_PackoutReportSources_UploadedByUserId", "PackoutReportSources", "IX_PackoutReportSources_UploadedByUserId"),
        new("IX_PackoutRuns_RunExpectationId", "PackoutRuns", "IX_PackoutRuns_RunExpectationId"),
        new("UX_PackoutRuns_ActualRunId", "PackoutRuns", "UX_PackoutRuns_ActualRunId", RequireUnique: true),
        new("IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId", "PackoutSourceAllocations", "IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId", RequireUnique: true),
        new("IX_PackoutSourceAllocations_RunExpectationSourceId", "PackoutSourceAllocations", "IX_PackoutSourceAllocations_RunExpectationSourceId"),
        new("IX_RunExpectations_ActualRunId_RevisionNumber", "RunExpectations", "IX_RunExpectations_ActualRunId_RevisionNumber", RequireUnique: true),
        new("IX_RunExpectations_ActualRunRevisionId", "RunExpectations", "IX_RunExpectations_ActualRunRevisionId", RequireUnique: true),
        new("IX_RunExpectations_CreatedByUserId", "RunExpectations", "IX_RunExpectations_CreatedByUserId"),
        new("IX_RunExpectationSources_BinsRunEntryId", "RunExpectationSources", "IX_RunExpectationSources_BinsRunEntryId"),
        new("IX_RunExpectationSources_QcSampleId", "RunExpectationSources", "IX_RunExpectationSources_QcSampleId"),
        new("IX_RunExpectationSources_RunExpectationId_BinsRunEntryId", "RunExpectationSources", "IX_RunExpectationSources_RunExpectationId_BinsRunEntryId", RequireUnique: true),
        new("IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot", "RunExpectationSources", "IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot"),
        new("IX_Users_EmploymentFacility", "Users", "IX_Users_EmploymentFacility"),
        new("IX_Users_EmploymentUpdatedByUserId", "Users", "IX_Users_EmploymentUpdatedByUserId"),
        new("IX_ActualRuns_RunFacilityAssignedByUserId", "ActualRuns", "IX_ActualRuns_RunFacilityAssignedByUserId"),
        new("IX_ActualRuns_RunFacilityWarehouseId_Status_RunAt", "ActualRuns", "IX_ActualRuns_RunFacilityWarehouseId_Status_RunAt"),
        new("IX_ActualRunOverrideRequests_RunFacilityWarehouseId", "ActualRunOverrideRequests", "IX_ActualRunOverrideRequests_RunFacilityWarehouseId"),
        new("IX_BinsRunEntries_ReportingFacilityAssignedByUserId", "BinsRunEntries", "IX_BinsRunEntries_ReportingFacilityAssignedByUserId"),
        new("IX_BinsRunEntries_ReportingFacilityWarehouseId_ReportingCropYearSnapshot_RunAt", "BinsRunEntries", "IX_BinsRunEntries_ReportingFacilityWarehouseId_ReportingCropYearSnapshot_RunAt"),
        new("IX_UserEmploymentHistory_ChangedByUserId", "UserEmploymentHistory", "IX_UserEmploymentHistory_ChangedByUserId"),
        new("IX_UserEmploymentHistory_UserId_ChangedAt", "UserEmploymentHistory", "IX_UserEmploymentHistory_UserId_ChangedAt"),
        new("IX_ReceiptInventoryOverrides_OperationKey", "ReceiptInventoryOverrides", "IX_ReceiptInventoryOverrides_OperationKey", RequireUnique: true),
        new("IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt", "ReceiptInventoryOverrides", "IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt"),
        new("IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId"),
        new("IX_EndOfDayFillReportGroups_Name", "EndOfDayFillReportGroups", "IX_EndOfDayFillReportGroups_Name", RequireUnique: true),
        new("IX_EndOfDayFillReportGroups_WarehouseId", "EndOfDayFillReportGroups", "IX_EndOfDayFillReportGroups_WarehouseId"),
        new("IX_Rooms_EndOfDayFillReportGroupId", "Rooms", "IX_Rooms_EndOfDayFillReportGroupId"),
        new("IX_EndOfDayFillReportRecipients_NormalizedEmailAddress", "EndOfDayFillReportRecipients", "IX_EndOfDayFillReportRecipients_NormalizedEmailAddress", RequireUnique: true),
        new("IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId", "EndOfDayFillUserGroupAssignments", "IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId", RequireUnique: true),
        new("IX_EndOfDayFillReportSends_SuccessRevisionKey", "EndOfDayFillReportSends", "IX_EndOfDayFillReportSends_SuccessRevisionKey", RequireUnique: true),
        new("IX_EndOfDayFillSendReservations_SendAttemptId", "EndOfDayFillSendReservations", "IX_EndOfDayFillSendReservations_SendAttemptId", RequireUnique: true),
        new("IX_Roles_NormalizedName", "Roles", "IX_Roles_NormalizedName", RequireUnique: true),
        new("IX_UserRoles_UserId", "UserRoles", "IX_UserRoles_UserId", RequireUnique: true),
        new("IX_RolePageAccesses_RoleId_AreaKey", "RolePageAccesses", "IX_RolePageAccesses_RoleId_AreaKey", RequireUnique: true),
        new("IX_RolePageAccesses_UpdatedByUserId", "RolePageAccesses", "IX_RolePageAccesses_UpdatedByUserId"),
        new("IX_InventoryDiagnosticAck_Key", "InventoryDiagnosticAcknowledgments", "IX_InventoryDiagnosticAck_Key", RequireUnique: true),
        new("IX_InventoryDiagnosticAck_DismissedBy", "InventoryDiagnosticAcknowledgments", "IX_InventoryDiagnosticAck_DismissedBy"),
        new("IX_InventoryDiagnosticAck_ActiveAdjustment", "InventoryDiagnosticAcknowledgments", "IX_InventoryDiagnosticAck_ActiveAdjustment"),
        new("IX_InventoryDiagnosticAck_RestoredBy", "InventoryDiagnosticAcknowledgments", "IX_InventoryDiagnosticAck_RestoredBy"),
        new("IX_InventoryDiagnosticAck_Adjustment", "InventoryDiagnosticAcknowledgments", "IX_InventoryDiagnosticAck_Adjustment"),
        new("IX_RoomInventoryAdjustments_RoomInventoryLossId", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_RoomInventoryLossId"),
        new("IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType", RequireUnique: true),
        new("IX_RoomInventoryLosses_CreatedByUserId", "RoomInventoryLosses", "IX_RoomInventoryLosses_CreatedByUserId"),
        new("IX_RoomInventoryLosses_FruitProfileId", "RoomInventoryLosses", "IX_RoomInventoryLosses_FruitProfileId"),
        new("IX_RoomInventoryLosses_OperationKey", "RoomInventoryLosses", "IX_RoomInventoryLosses_OperationKey", RequireUnique: true),
        new("IX_RoomInventoryLosses_ReceiptId_CreatedAt", "RoomInventoryLosses", "IX_RoomInventoryLosses_ReceiptId_CreatedAt"),
        new("IX_RoomInventoryLosses_ReversedByUserId", "RoomInventoryLosses", "IX_RoomInventoryLosses_ReversedByUserId"),
        new("IX_RoomInventoryLosses_RoomId_CreatedAt", "RoomInventoryLosses", "IX_RoomInventoryLosses_RoomId_CreatedAt"),
        new("IX_RoomInventoryLosses_WarehouseId", "RoomInventoryLosses", "IX_RoomInventoryLosses_WarehouseId"),
        new("IX_RoomTreatmentApplications_AppliedByUserId", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_AppliedByUserId"),
        new("IX_RoomTreatmentApplications_CreatedByUserId", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_CreatedByUserId"),
        new("IX_RoomTreatmentApplications_OperationKey", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_OperationKey", RequireUnique: true),
        new("IX_RoomTreatmentApplications_ReversedByUserId", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_ReversedByUserId"),
        new("IX_RoomTreatmentApplications_ReceiptId_AppliedAt", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_ReceiptId_AppliedAt"),
        new("IX_RoomTreatmentApplications_RoomId_AppliedAt", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_RoomId_AppliedAt"),
        new("IX_RoomTreatmentApplications_TreatmentChemicalId", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_TreatmentChemicalId"),
        new("IX_RoomTreatmentApplications_WarehouseId", "RoomTreatmentApplications", "IX_RoomTreatmentApplications_WarehouseId"),
        new("IX_RoomTreatmentApplicationSources_FruitProfileId", "RoomTreatmentApplicationSources", "IX_RoomTreatmentApplicationSources_FruitProfileId"),
        new("IX_RoomTreatmentApplicationSources_GrowerLotId", "RoomTreatmentApplicationSources", "IX_RoomTreatmentApplicationSources_GrowerLotId"),
        new("IX_RoomTreatmentApplicationSources_ReceiptId", "RoomTreatmentApplicationSources", "IX_RoomTreatmentApplicationSources_ReceiptId"),
        new("IX_RoomTreatmentApplicationSources_RoomTreatmentApplicationId_IdentityKey", "RoomTreatmentApplicationSources", "IX_RoomTreatmentApplicationSources_RoomTreatmentApplicationId_IdentityKey"),
        new("IX_TreatmentChemicals_CreatedByUserId", "TreatmentChemicals", "IX_TreatmentChemicals_CreatedByUserId"),
        new("IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductName", "TreatmentChemicals", "IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductName"),
        new("IX_TreatmentChemicals_ProductName", "TreatmentChemicals", "IX_TreatmentChemicals_ProductName", RequireUnique: true),
        new("IX_TreatmentChemicals_UpdatedByUserId", "TreatmentChemicals", "IX_TreatmentChemicals_UpdatedByUserId"),
        new("IX_TreatmentLineageMovements_BinsRunEntryId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_BinsRunEntryId"),
        new("IX_TreatmentLineageMovements_CreatedByUserId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_CreatedByUserId"),
        new("IX_TreatmentLineageMovements_DestinationRoomId_OccurredAt", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_DestinationRoomId_OccurredAt"),
        new("IX_TreatmentLineageMovements_DestinationSegmentId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_DestinationSegmentId"),
        new("IX_TreatmentLineageMovements_OperationKey", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_OperationKey", RequireUnique: true),
        new("IX_TreatmentLineageMovements_ReceiptId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_ReceiptId"),
        new("IX_TreatmentLineageMovements_ReversesTreatmentLineageMovementId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_ReversesTreatmentLineageMovementId"),
        new("IX_TreatmentLineageMovements_RoomInventoryLossId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_RoomInventoryLossId"),
        new("IX_TreatmentLineageMovements_RoomTransferId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_RoomTransferId"),
        new("IX_TreatmentLineageMovements_SourceRoomId_OccurredAt", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_SourceRoomId_OccurredAt"),
        new("IX_TreatmentLineageMovements_SourceSegmentId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_SourceSegmentId"),
        new("IX_TreatmentLineageSegmentApplications_RoomTreatmentApplicationId_TreatmentLineageSegmentId", "TreatmentLineageSegmentApplications", "IX_TreatmentLineageSegmentApplications_RoomTreatmentApplicationId_TreatmentLineageSegmentId"),
        new("IX_TreatmentLineageSegments_FruitProfileId", "TreatmentLineageSegments", "IX_TreatmentLineageSegments_FruitProfileId"),
        new("IX_TreatmentLineageSegments_GrowerLotId", "TreatmentLineageSegments", "IX_TreatmentLineageSegments_GrowerLotId"),
        new("IX_TreatmentLineageSegments_RoomId_CurrentBins", "TreatmentLineageSegments", "IX_TreatmentLineageSegments_RoomId_CurrentBins"),
        new("IX_TreatmentLineageSegments_ReceiptId", "TreatmentLineageSegments", "IX_TreatmentLineageSegments_ReceiptId"),
        new("UX_TreatmentLineageSegments_Receipt", "TreatmentLineageSegments", "UX_TreatmentLineageSegments_Receipt", RequireUnique: true),
        new("UX_TreatmentLineageSegments_Unassigned", "TreatmentLineageSegments", "UX_TreatmentLineageSegments_Unassigned", RequireUnique: true),
        new("IX_TreatmentLineageSegments_WarehouseId", "TreatmentLineageSegments", "IX_TreatmentLineageSegments_WarehouseId"),
        new("IX_RoomTreatmentApplicationAttachments_CreatedByUserId", "RoomTreatmentApplicationAttachments", "IX_RoomTreatmentApplicationAttachments_CreatedByUserId"),
        new("IX_RoomTreatmentApplicationAttachments_DeletedByUserId", "RoomTreatmentApplicationAttachments", "IX_RoomTreatmentApplicationAttachments_DeletedByUserId"),
        new("IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt", "RoomTreatmentApplicationAttachments", "IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt"),
        new("UX_TreatmentReportAttachments_Application_OperationKey", "RoomTreatmentApplicationAttachments", "UX_TreatmentReportAttachments_Application_OperationKey", RequireUnique: true),
        new("IX_TreatmentLineageMovements_ProcessorShipmentLineId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_ProcessorShipmentLineId"),
        new("IX_RoomInventoryAdjustments_ProcessorShipmentLineId", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_ProcessorShipmentLineId"),
        new("IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType", RequireUnique: true),
        new("IX_Processors_CreatedByUserId", "Processors", "IX_Processors_CreatedByUserId"),
        new("IX_Processors_IsActive_Name", "Processors", "IX_Processors_IsActive_Name"),
        new("IX_Processors_Name", "Processors", "IX_Processors_Name"),
        new("IX_Processors_UpdatedByUserId", "Processors", "IX_Processors_UpdatedByUserId"),
        new("IX_ProcessorShipmentLines_ProcessorShipmentId", "ProcessorShipmentLines", "IX_ProcessorShipmentLines_ProcessorShipmentId"),
        new("IX_ProcessorShipmentLines_ReceiptId", "ProcessorShipmentLines", "IX_ProcessorShipmentLines_ReceiptId"),
        new("IX_ProcessorShipmentLines_RoomId", "ProcessorShipmentLines", "IX_ProcessorShipmentLines_RoomId"),
        new("IX_ProcessorShipmentLines_SourceInventoryAdjustmentId", "ProcessorShipmentLines", "IX_ProcessorShipmentLines_SourceInventoryAdjustmentId"),
        new("IX_ProcessorShipmentLines_WarehouseId_RoomId", "ProcessorShipmentLines", "IX_ProcessorShipmentLines_WarehouseId_RoomId"),
        new("IX_ProcessorShipmentPriceCorrections_CorrectedByUserId", "ProcessorShipmentPriceCorrections", "IX_ProcessorShipmentPriceCorrections_CorrectedByUserId"),
        new("IX_ProcessorShipmentPriceCorrections_OperationKey", "ProcessorShipmentPriceCorrections", "IX_ProcessorShipmentPriceCorrections_OperationKey", RequireUnique: true),
        new("IX_ProcessorShipmentPriceCorrections_ProcessorShipmentId_CorrectedAt", "ProcessorShipmentPriceCorrections", "IX_ProcessorShipmentPriceCorrections_ProcessorShipmentId_CorrectedAt"),
        new("IX_ProcessorShipments_CreatedByUserId", "ProcessorShipments", "IX_ProcessorShipments_CreatedByUserId"),
        new("IX_ProcessorShipments_OperationKey", "ProcessorShipments", "IX_ProcessorShipments_OperationKey", RequireUnique: true),
        new("IX_ProcessorShipments_ProcessorId", "ProcessorShipments", "IX_ProcessorShipments_ProcessorId"),
        new("IX_ProcessorShipments_ReversedByUserId", "ProcessorShipments", "IX_ProcessorShipments_ReversedByUserId"),
        new("IX_ProcessorShipments_ShippedAt_ProcessorId", "ProcessorShipments", "IX_ProcessorShipments_ShippedAt_ProcessorId"),
        new("IX_Rooms_SealedByUserId", "Rooms", "IX_Rooms_SealedByUserId"),
        new("IX_RoomSealEvents_ChangedByUserId", "RoomSealEvents", "IX_RoomSealEvents_ChangedByUserId"),
        new("IX_RoomSealEvents_RoomId_ChangedAt", "RoomSealEvents", "IX_RoomSealEvents_RoomId_ChangedAt"),
        new("IX_ActualRuns_SalesDeskId_Status_RunAt", "ActualRuns", "IX_ActualRuns_SalesDeskId_Status_RunAt"),
        new("IX_ActualRunOverrideRequests_SalesDeskId", "ActualRunOverrideRequests", "IX_ActualRunOverrideRequests_SalesDeskId"),
        new("IX_ActualRunSalesDeskCorrections_ActualRunId_CorrectedAt", "ActualRunSalesDeskCorrections", "IX_ActualRunSalesDeskCorrections_ActualRunId_CorrectedAt"),
        new("IX_ActualRunSalesDeskCorrections_CorrectedByUserId", "ActualRunSalesDeskCorrections", "IX_ActualRunSalesDeskCorrections_CorrectedByUserId"),
        new("IX_ActualRunSalesDeskCorrections_NewSalesDeskId", "ActualRunSalesDeskCorrections", "IX_ActualRunSalesDeskCorrections_NewSalesDeskId"),
        new("IX_ActualRunSalesDeskCorrections_OperationKey", "ActualRunSalesDeskCorrections", "IX_ActualRunSalesDeskCorrections_OperationKey", RequireUnique: true),
        new("IX_ActualRunSalesDeskCorrections_PreviousSalesDeskId", "ActualRunSalesDeskCorrections", "IX_ActualRunSalesDeskCorrections_PreviousSalesDeskId"),
        new("IX_SalesDesks_CreatedByUserId", "SalesDesks", "IX_SalesDesks_CreatedByUserId"),
        new("IX_SalesDesks_IsActive_DisplayOrder_Name", "SalesDesks", "IX_SalesDesks_IsActive_DisplayOrder_Name"),
        new("IX_SalesDesks_Name", "SalesDesks", "IX_SalesDesks_Name", RequireUnique: true),
        new("IX_SalesDesks_UpdatedByUserId", "SalesDesks", "IX_SalesDesks_UpdatedByUserId"),
        new("IX_TreatmentLineageMovements_OutsideWarehouseTransferId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_OutsideWarehouseTransferId"),
        new("IX_RoomInventoryAdjustments_OutsideWarehouseTransferId", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId"),
        new("IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType", RequireUnique: true),
        new("IX_OutsideWarehouses_Code", "OutsideWarehouses", "IX_OutsideWarehouses_Code", RequireUnique: true),
        new("IX_OutsideWarehouses_CreatedByUserId", "OutsideWarehouses", "IX_OutsideWarehouses_CreatedByUserId"),
        new("IX_OutsideWarehouses_IsActive_Name", "OutsideWarehouses", "IX_OutsideWarehouses_IsActive_Name"),
        new("IX_OutsideWarehouses_UpdatedByUserId", "OutsideWarehouses", "IX_OutsideWarehouses_UpdatedByUserId"),
        new("IX_OutsideWarehouseTransfers_CreatedByUserId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_CreatedByUserId"),
        new("IX_OutsideWarehouseTransfers_FruitProfileId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_FruitProfileId"),
        new("IX_OutsideWarehouseTransfers_GrowerNumberSnapshot", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_GrowerNumberSnapshot"),
        new("IX_OutsideWarehouseTransfers_OperationKey", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_OperationKey", RequireUnique: true),
        new("IX_OutsideWarehouseTransfers_OutsideWarehouseId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_OutsideWarehouseId"),
        new("IX_OutsideWarehouseTransfers_ReceiptId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_ReceiptId"),
        new("IX_OutsideWarehouseTransfers_ReversalOperationKey", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_ReversalOperationKey", RequireUnique: true),
        new("IX_OutsideWarehouseTransfers_ReversedByUserId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_ReversedByUserId"),
        new("IX_OutsideWarehouseTransfers_SourceInventoryAdjustmentId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_SourceInventoryAdjustmentId"),
        new("IX_OutsideWarehouseTransfers_SourceRoomId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_SourceRoomId"),
        new("IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt"),
        new("IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId", "OutsideWarehouseTransfers", "IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId")
        ,new("IX_InterCrewTransfers_OperationKey", "InterCrewTransfers", "IX_InterCrewTransfers_OperationKey", RequireUnique: true)
        ,new("IX_InterCrewTransfers_ReceiveOperationKey", "InterCrewTransfers", "IX_InterCrewTransfers_ReceiveOperationKey", RequireUnique: true)
        ,new("IX_InterCrewTransfers_ReviewOperationKey", "InterCrewTransfers", "IX_InterCrewTransfers_ReviewOperationKey", RequireUnique: true)
        ,new("IX_InterCrewTransfers_ReversalOperationKey", "InterCrewTransfers", "IX_InterCrewTransfers_ReversalOperationKey", RequireUnique: true)
        ,new("IX_InterCrewTransfers_DestinationCustodyGroup_Status_LoadedAt", "InterCrewTransfers", "IX_InterCrewTransfers_DestinationCustodyGroup_Status_LoadedAt")
        ,new("IX_InterCrewTransfers_SourceRoomId_LoadedAt", "InterCrewTransfers", "IX_InterCrewTransfers_SourceRoomId_LoadedAt")
        ,new("IX_RoomInventoryAdjustments_InterCrewTransferId", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_InterCrewTransferId")
        ,new("IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType", "RoomInventoryAdjustments", "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType", RequireUnique: true)
        ,new("IX_TreatmentLineageMovements_InterCrewTransferId", "TreatmentLineageMovements", "IX_TreatmentLineageMovements_InterCrewTransferId")
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredForeignKeyExpectations =
    [
        new("FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId", "GrowerReportRecipients", "FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId"),
        new("FK_GrowerReportRecipients_Users_CreatedByUserId", "GrowerReportRecipients", "FK_GrowerReportRecipients_Users_CreatedByUserId"),
        new("FK_GrowerReportRecipients_Users_DeletedByUserId", "GrowerReportRecipients", "FK_GrowerReportRecipients_Users_DeletedByUserId"),
        new("FK_GrowerReportRecipients_Users_UpdatedByUserId", "GrowerReportRecipients", "FK_GrowerReportRecipients_Users_UpdatedByUserId"),
        new("FK_PackoutReportSources_Users_UploadedByUserId", "PackoutReportSources", "FK_PackoutReportSources_Users_UploadedByUserId"),
        new("FK_PackoutRuns_ActualRuns_ActualRunId", "PackoutRuns", "FK_PackoutRuns_ActualRuns_ActualRunId"),
        new("FK_PackoutRuns_RunExpectations_RunExpectationId", "PackoutRuns", "FK_PackoutRuns_RunExpectations_RunExpectationId"),
        new("FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId", "RunExpectations", "FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId"),
        new("FK_RunExpectations_ActualRuns_ActualRunId", "RunExpectations", "FK_RunExpectations_ActualRuns_ActualRunId"),
        new("FK_RunExpectations_Users_CreatedByUserId", "RunExpectations", "FK_RunExpectations_Users_CreatedByUserId"),
        new("FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId", "RunExpectationSources", "FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId"),
        new("FK_RunExpectationSources_QcSamples_QcSampleId", "RunExpectationSources", "FK_RunExpectationSources_QcSamples_QcSampleId"),
        new("FK_RunExpectationSources_RunExpectations_RunExpectationId", "RunExpectationSources", "FK_RunExpectationSources_RunExpectations_RunExpectationId"),
        new("FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId", "PackoutSourceAllocations", "FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId"),
        new("FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId", "PackoutSourceAllocations", "FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId"),
        new("FK_ActualRunOverrideRequests_Warehouses_RunFacilityWarehouseId", "ActualRunOverrideRequests", "FK_ActualRunOverrideRequests_Warehouses_RunFacilityWarehouseId"),
        new("FK_ActualRuns_Users_RunFacilityAssignedByUserId", "ActualRuns", "FK_ActualRuns_Users_RunFacilityAssignedByUserId"),
        new("FK_ActualRuns_Warehouses_RunFacilityWarehouseId", "ActualRuns", "FK_ActualRuns_Warehouses_RunFacilityWarehouseId"),
        new("FK_BinsRunEntries_Users_ReportingFacilityAssignedByUserId", "BinsRunEntries", "FK_BinsRunEntries_Users_ReportingFacilityAssignedByUserId"),
        new("FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId", "BinsRunEntries", "FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId"),
        new("FK_Users_Users_EmploymentUpdatedByUserId", "Users", "FK_Users_Users_EmploymentUpdatedByUserId"),
        new("FK_UserEmploymentHistory_Users_ChangedByUserId", "UserEmploymentHistory", "FK_UserEmploymentHistory_Users_ChangedByUserId"),
        new("FK_UserEmploymentHistory_Users_UserId", "UserEmploymentHistory", "FK_UserEmploymentHistory_Users_UserId"),
        new("FK_ReceiptInventoryOverrides_Receipts_ReceiptId", "ReceiptInventoryOverrides", "FK_ReceiptInventoryOverrides_Receipts_ReceiptId"),
        new("FK_ReceiptInventoryOverrides_Users_AdministratorUserId", "ReceiptInventoryOverrides", "FK_ReceiptInventoryOverrides_Users_AdministratorUserId"),
        new("FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId", "RoomInventoryAdjustments", "FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId"),
        new("FK_Rooms_EndOfDayFillReportGroups_EndOfDayFillReportGroupId", "Rooms", "FK_Rooms_EndOfDayFillReportGroups_EndOfDayFillReportGroupId"),
        new("FK_EndOfDayFillReportGroups_Warehouses_WarehouseId", "EndOfDayFillReportGroups", "FK_EndOfDayFillReportGroups_Warehouses_WarehouseId"),
        new("FK_EndOfDayFillUserGroupAssignments_Users_UserId", "EndOfDayFillUserGroupAssignments", "FK_EndOfDayFillUserGroupAssignments_Users_UserId"),
        new("FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId", "EndOfDayFillReportSends", "FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId"),
        new("FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId", "EndOfDayFillSendReservations", "FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId"),
        new("FK_RolePageAccesses_Roles_RoleId", "RolePageAccesses", "FK_RolePageAccesses_Roles_RoleId"),
        new("FK_RolePageAccesses_Users_UpdatedByUserId", "RolePageAccesses", "FK_RolePageAccesses_Users_UpdatedByUserId"),
        new("FK_InventoryDiagnosticAck_Adjustment", "InventoryDiagnosticAcknowledgments", "FK_InventoryDiagnosticAck_Adjustment"),
        new("FK_InventoryDiagnosticAck_DismissedBy", "InventoryDiagnosticAcknowledgments", "FK_InventoryDiagnosticAck_DismissedBy"),
        new("FK_InventoryDiagnosticAck_RestoredBy", "InventoryDiagnosticAcknowledgments", "FK_InventoryDiagnosticAck_RestoredBy"),
        new("FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId", "RoomInventoryAdjustments", "FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId"),
        new("FK_RoomInventoryLosses_FruitProfiles_FruitProfileId", "RoomInventoryLosses", "FK_RoomInventoryLosses_FruitProfiles_FruitProfileId"),
        new("FK_RoomInventoryLosses_Receipts_ReceiptId", "RoomInventoryLosses", "FK_RoomInventoryLosses_Receipts_ReceiptId"),
        new("FK_RoomInventoryLosses_Rooms_RoomId", "RoomInventoryLosses", "FK_RoomInventoryLosses_Rooms_RoomId"),
        new("FK_RoomInventoryLosses_Users_CreatedByUserId", "RoomInventoryLosses", "FK_RoomInventoryLosses_Users_CreatedByUserId"),
        new("FK_RoomInventoryLosses_Users_ReversedByUserId", "RoomInventoryLosses", "FK_RoomInventoryLosses_Users_ReversedByUserId"),
        new("FK_RoomInventoryLosses_Warehouses_WarehouseId", "RoomInventoryLosses", "FK_RoomInventoryLosses_Warehouses_WarehouseId"),
        new("FK_TreatmentChemicals_Users_CreatedByUserId", "TreatmentChemicals", "FK_TreatmentChemicals_Users_CreatedByUserId"),
        new("FK_TreatmentChemicals_Users_UpdatedByUserId", "TreatmentChemicals", "FK_TreatmentChemicals_Users_UpdatedByUserId"),
        new("FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId", "TreatmentLineageSegments", "FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId"),
        new("FK_TreatmentLineageSegments_Rooms_RoomId", "TreatmentLineageSegments", "FK_TreatmentLineageSegments_Rooms_RoomId"),
        new("FK_TreatmentLineageSegments_Warehouses_WarehouseId", "TreatmentLineageSegments", "FK_TreatmentLineageSegments_Warehouses_WarehouseId"),
        new("FK_TreatmentLineageSegments_Receipts_ReceiptId", "TreatmentLineageSegments", "FK_TreatmentLineageSegments_Receipts_ReceiptId"),
        new("FK_RoomTreatmentApplications_Rooms_RoomId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Rooms_RoomId"),
        new("FK_RoomTreatmentApplications_TreatmentChemicals_TreatmentChemicalId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_TreatmentChemicals_TreatmentChemicalId"),
        new("FK_RoomTreatmentApplications_Users_AppliedByUserId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Users_AppliedByUserId"),
        new("FK_RoomTreatmentApplications_Users_CreatedByUserId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Users_CreatedByUserId"),
        new("FK_RoomTreatmentApplications_Users_ReversedByUserId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Users_ReversedByUserId"),
        new("FK_RoomTreatmentApplications_Warehouses_WarehouseId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Warehouses_WarehouseId"),
        new("FK_RoomTreatmentApplications_Receipts_ReceiptId", "RoomTreatmentApplications", "FK_RoomTreatmentApplications_Receipts_ReceiptId"),
        new("FK_TreatmentLineageMovements_BinsRunEntries_BinsRunEntryId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_BinsRunEntries_BinsRunEntryId"),
        new("FK_TreatmentLineageMovements_RoomInventoryLosses_RoomInventoryLossId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_RoomInventoryLosses_RoomInventoryLossId"),
        new("FK_TreatmentLineageMovements_RoomTransfers_RoomTransferId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_RoomTransfers_RoomTransferId"),
        new("FK_TreatmentLineageMovements_Rooms_DestinationRoomId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_Rooms_DestinationRoomId"),
        new("FK_TreatmentLineageMovements_Rooms_SourceRoomId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_Rooms_SourceRoomId"),
        new("FK_TreatmentLineageMovements_TreatmentLineageMovements_ReversesTreatmentLineageMovementId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_TreatmentLineageMovements_ReversesTreatmentLineageMovementId"),
        new("FK_TreatmentLineageMovements_TreatmentLineageSegments_DestinationSegmentId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_TreatmentLineageSegments_DestinationSegmentId"),
        new("FK_TreatmentLineageMovements_TreatmentLineageSegments_SourceSegmentId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_TreatmentLineageSegments_SourceSegmentId"),
        new("FK_TreatmentLineageMovements_Users_CreatedByUserId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_Users_CreatedByUserId"),
        new("FK_TreatmentLineageMovements_Receipts_ReceiptId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_Receipts_ReceiptId"),
        new("FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId", "RoomTreatmentApplicationSources", "FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId"),
        new("FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId", "RoomTreatmentApplicationSources", "FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId"),
        new("FK_RoomTreatmentApplicationSources_Receipts_ReceiptId", "RoomTreatmentApplicationSources", "FK_RoomTreatmentApplicationSources_Receipts_ReceiptId"),
        new("FK_TreatmentLineageSegmentApplications_RoomTreatmentApplications_RoomTreatmentApplicationId", "TreatmentLineageSegmentApplications", "FK_TreatmentLineageSegmentApplications_RoomTreatmentApplications_RoomTreatmentApplicationId"),
        new("FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId", "TreatmentLineageSegmentApplications", "FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId"),
        new("FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId", "RoomTreatmentApplicationAttachments", "FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId"),
        new("FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId", "RoomTreatmentApplicationAttachments", "FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId"),
        new("FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId", "RoomTreatmentApplicationAttachments", "FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId"),
        new("FK_Processors_Users_CreatedByUserId", "Processors", "FK_Processors_Users_CreatedByUserId"),
        new("FK_Processors_Users_UpdatedByUserId", "Processors", "FK_Processors_Users_UpdatedByUserId"),
        new("FK_ProcessorShipments_Processors_ProcessorId", "ProcessorShipments", "FK_ProcessorShipments_Processors_ProcessorId"),
        new("FK_ProcessorShipments_Users_CreatedByUserId", "ProcessorShipments", "FK_ProcessorShipments_Users_CreatedByUserId"),
        new("FK_ProcessorShipments_Users_ReversedByUserId", "ProcessorShipments", "FK_ProcessorShipments_Users_ReversedByUserId"),
        new("FK_ProcessorShipmentLines_ProcessorShipments_ProcessorShipmentId", "ProcessorShipmentLines", "FK_ProcessorShipmentLines_ProcessorShipments_ProcessorShipmentId"),
        new("FK_ProcessorShipmentLines_Receipts_ReceiptId", "ProcessorShipmentLines", "FK_ProcessorShipmentLines_Receipts_ReceiptId"),
        new("FK_ProcessorShipmentLines_RoomInventoryAdjustments_SourceInventoryAdjustmentId", "ProcessorShipmentLines", "FK_ProcessorShipmentLines_RoomInventoryAdjustments_SourceInventoryAdjustmentId"),
        new("FK_ProcessorShipmentLines_Rooms_RoomId", "ProcessorShipmentLines", "FK_ProcessorShipmentLines_Rooms_RoomId"),
        new("FK_ProcessorShipmentLines_Warehouses_WarehouseId", "ProcessorShipmentLines", "FK_ProcessorShipmentLines_Warehouses_WarehouseId"),
        new("FK_ProcessorShipmentPriceCorrections_ProcessorShipments_ProcessorShipmentId", "ProcessorShipmentPriceCorrections", "FK_ProcessorShipmentPriceCorrections_ProcessorShipments_ProcessorShipmentId"),
        new("FK_ProcessorShipmentPriceCorrections_Users_CorrectedByUserId", "ProcessorShipmentPriceCorrections", "FK_ProcessorShipmentPriceCorrections_Users_CorrectedByUserId"),
        new("FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId", "RoomInventoryAdjustments", "FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId"),
        new("FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId"),
        new("FK_Rooms_Users_SealedByUserId", "Rooms", "FK_Rooms_Users_SealedByUserId"),
        new("FK_RoomSealEvents_Rooms_RoomId", "RoomSealEvents", "FK_RoomSealEvents_Rooms_RoomId"),
        new("FK_RoomSealEvents_Users_ChangedByUserId", "RoomSealEvents", "FK_RoomSealEvents_Users_ChangedByUserId"),
        new("FK_ActualRuns_SalesDesks_SalesDeskId", "ActualRuns", "FK_ActualRuns_SalesDesks_SalesDeskId"),
        new("FK_ActualRunOverrideRequests_SalesDesks_SalesDeskId", "ActualRunOverrideRequests", "FK_ActualRunOverrideRequests_SalesDesks_SalesDeskId"),
        new("FK_SalesDesks_Users_CreatedByUserId", "SalesDesks", "FK_SalesDesks_Users_CreatedByUserId"),
        new("FK_SalesDesks_Users_UpdatedByUserId", "SalesDesks", "FK_SalesDesks_Users_UpdatedByUserId"),
        new("FK_ActualRunSalesDeskCorrections_ActualRuns_ActualRunId", "ActualRunSalesDeskCorrections", "FK_ActualRunSalesDeskCorrections_ActualRuns_ActualRunId"),
        new("FK_ActualRunSalesDeskCorrections_SalesDesks_NewSalesDeskId", "ActualRunSalesDeskCorrections", "FK_ActualRunSalesDeskCorrections_SalesDesks_NewSalesDeskId"),
        new("FK_ActualRunSalesDeskCorrections_SalesDesks_PreviousSalesDeskId", "ActualRunSalesDeskCorrections", "FK_ActualRunSalesDeskCorrections_SalesDesks_PreviousSalesDeskId"),
        new("FK_ActualRunSalesDeskCorrections_Users_CorrectedByUserId", "ActualRunSalesDeskCorrections", "FK_ActualRunSalesDeskCorrections_Users_CorrectedByUserId"),
        new("FK_OutsideWarehouses_Users_CreatedByUserId", "OutsideWarehouses", "FK_OutsideWarehouses_Users_CreatedByUserId"),
        new("FK_OutsideWarehouses_Users_UpdatedByUserId", "OutsideWarehouses", "FK_OutsideWarehouses_Users_UpdatedByUserId"),
        new("FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId"),
        new("FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId"),
        new("FK_OutsideWarehouseTransfers_Receipts_ReceiptId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_Receipts_ReceiptId"),
        new("FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId"),
        new("FK_OutsideWarehouseTransfers_Rooms_SourceRoomId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_Rooms_SourceRoomId"),
        new("FK_OutsideWarehouseTransfers_Users_CreatedByUserId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_Users_CreatedByUserId"),
        new("FK_OutsideWarehouseTransfers_Users_ReversedByUserId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_Users_ReversedByUserId"),
        new("FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId", "OutsideWarehouseTransfers", "FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId"),
        new("FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId", "RoomInventoryAdjustments", "FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId"),
        new("FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId")
        ,new("FK_InterCrewTransfers_Rooms_SourceRoomId", "InterCrewTransfers", "FK_InterCrewTransfers_Rooms_SourceRoomId")
        ,new("FK_InterCrewTransfers_Rooms_DestinationRoomId", "InterCrewTransfers", "FK_InterCrewTransfers_Rooms_DestinationRoomId")
        ,new("FK_InterCrewTransfers_Warehouses_SourceWarehouseId", "InterCrewTransfers", "FK_InterCrewTransfers_Warehouses_SourceWarehouseId")
        ,new("FK_InterCrewTransfers_Warehouses_DestinationWarehouseId", "InterCrewTransfers", "FK_InterCrewTransfers_Warehouses_DestinationWarehouseId")
        ,new("FK_RoomInventoryAdjustments_InterCrewTransfers_InterCrewTransferId", "RoomInventoryAdjustments", "FK_RoomInventoryAdjustments_InterCrewTransfers_InterCrewTransferId")
        ,new("FK_TreatmentLineageMovements_InterCrewTransfers_InterCrewTransferId", "TreatmentLineageMovements", "FK_TreatmentLineageMovements_InterCrewTransfers_InterCrewTransferId")
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredPrimaryKeyExpectations =
    [
        new("PK_GrowerReportRecipients", "GrowerReportRecipients", "PK_GrowerReportRecipients"),
        new("PK_RunExpectations", "RunExpectations", "PK_RunExpectations"),
        new("PK_RunExpectationSources", "RunExpectationSources", "PK_RunExpectationSources"),
        new("PK_PackoutSourceAllocations", "PackoutSourceAllocations", "PK_PackoutSourceAllocations"),
        new("PK_UserEmploymentHistory", "UserEmploymentHistory", "PK_UserEmploymentHistory"),
        new("PK_ReceiptInventoryOverrides", "ReceiptInventoryOverrides", "PK_ReceiptInventoryOverrides"),
        new("PK_EndOfDayFillReportGroups", "EndOfDayFillReportGroups", "PK_EndOfDayFillReportGroups"),
        new("PK_EndOfDayFillReportRecipients", "EndOfDayFillReportRecipients", "PK_EndOfDayFillReportRecipients"),
        new("PK_EndOfDayFillUserGroupAssignments", "EndOfDayFillUserGroupAssignments", "PK_EndOfDayFillUserGroupAssignments"),
        new("PK_EndOfDayFillReportSends", "EndOfDayFillReportSends", "PK_EndOfDayFillReportSends"),
        new("PK_EndOfDayFillSendReservations", "EndOfDayFillSendReservations", "PK_EndOfDayFillSendReservations"),
        new("PK_RolePageAccesses", "RolePageAccesses", "PK_RolePageAccesses"),
        new("PK_InventoryDiagnosticAcknowledgments", "InventoryDiagnosticAcknowledgments", "PK_InventoryDiagnosticAcknowledgments"),
        new("PK_RoomInventoryLosses", "RoomInventoryLosses", "PK_RoomInventoryLosses"),
        new("PK_TreatmentChemicals", "TreatmentChemicals", "PK_TreatmentChemicals"),
        new("PK_RoomTreatmentApplications", "RoomTreatmentApplications", "PK_RoomTreatmentApplications"),
        new("PK_RoomTreatmentApplicationSources", "RoomTreatmentApplicationSources", "PK_RoomTreatmentApplicationSources"),
        new("PK_TreatmentLineageSegments", "TreatmentLineageSegments", "PK_TreatmentLineageSegments"),
        new("PK_TreatmentLineageSegmentApplications", "TreatmentLineageSegmentApplications", "PK_TreatmentLineageSegmentApplications"),
        new("PK_TreatmentLineageMovements", "TreatmentLineageMovements", "PK_TreatmentLineageMovements"),
        new("PK_RoomTreatmentApplicationAttachments", "RoomTreatmentApplicationAttachments", "PK_RoomTreatmentApplicationAttachments"),
        new("PK_Processors", "Processors", "PK_Processors"),
        new("PK_ProcessorShipments", "ProcessorShipments", "PK_ProcessorShipments"),
        new("PK_ProcessorShipmentLines", "ProcessorShipmentLines", "PK_ProcessorShipmentLines"),
        new("PK_ProcessorShipmentPriceCorrections", "ProcessorShipmentPriceCorrections", "PK_ProcessorShipmentPriceCorrections"),
        new("PK_RoomSealEvents", "RoomSealEvents", "PK_RoomSealEvents"),
        new("PK_SalesDesks", "SalesDesks", "PK_SalesDesks"),
        new("PK_ActualRunSalesDeskCorrections", "ActualRunSalesDeskCorrections", "PK_ActualRunSalesDeskCorrections"),
        new("PK_OutsideWarehouses", "OutsideWarehouses", "PK_OutsideWarehouses"),
        new("PK_OutsideWarehouseTransfers", "OutsideWarehouseTransfers", "PK_OutsideWarehouseTransfers")
        ,new("PK_InterCrewTransfers", "InterCrewTransfers", "PK_InterCrewTransfers")
    ];

    public static async Task InspectAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartupDiagnostics");
        var provider = db.Database.ProviderName ?? "Unknown";
        if (!db.Database.IsRelational())
        {
            logger.LogInformation(
                "Database startup migration diagnostics skipped for non-relational provider {Provider}.",
                provider);
            return;
        }

        var deployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"] ?? "Unknown";
        var applicationVersion = GetApplicationVersion();
        var compiledMigrations = db.Database.GetMigrations().ToArray();
        var latestCompiledMigration = compiledMigrations.LastOrDefault() ?? "None";

        logger.LogInformation(
            "Database startup check. Environment {Environment}; provider {Provider}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; latest compiled migration {LatestCompiledMigration}.",
            environment.EnvironmentName,
            provider,
            applicationVersion,
            deployedCommit,
            latestCompiledMigration);

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogError(
                    "Database startup check failed. Category {Category}; provider {Provider}; the configured database did not accept a connection.",
                    DatabaseFailureCategory.ConnectionUnavailable,
                    provider);
                return;
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database startup check failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
            return;
        }

        logger.LogInformation("Database startup connection check succeeded for provider {Provider}.", provider);

        try
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            logger.LogInformation(
                "Database migration status. Applied count {AppliedCount}; pending count {PendingCount}; latest applied {LatestApplied}; latest compiled {LatestCompiled}.",
                applied.Length,
                pending.Length,
                applied.LastOrDefault() ?? "None",
                latestCompiledMigration);

            if (pending.Length > 0)
            {
                logger.LogWarning(
                    "Database schema tracking is behind the application. Pending migrations: {PendingMigrations}.",
                    string.Join(", ", pending));
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database migration status check failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
        }

        try
        {
            var missing = await FindMissingSchemaObjectsAsync(db, provider, cancellationToken);
            if (missing.Count == 0)
            {
                logger.LogInformation(
                    "Application schema check succeeded. Expected migration {ExpectedMigration}; checked object count {CheckedObjectCount}.",
                    ExpectedSchemaMigration,
                    RequiredObjectCount);
            }
            else
            {
                var referenceId = Guid.NewGuid().ToString("N")[..8];
                var partiallyUpdated = missing.Count < RequiredObjectCount;
                logger.LogError(
                    "Database schema mismatch detected. Reference {ReferenceId}; category {Category}; provider {Provider}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; partially updated {PartiallyUpdated}; missing objects {MissingObjects}; operator action {OperatorAction}. Production data was not modified.",
                    referenceId,
                    DatabaseFailureCategory.SchemaMismatch,
                    provider,
                    applicationVersion,
                    deployedCommit,
                    ExpectedSchemaMigration,
                    partiallyUpdated,
                    string.Join(", ", missing),
                    "Keep the prior compatible deployment active, run the reviewed PostgreSQL preflight, obtain backup and production authorization, apply the approved compatibility script, then verify before redeploying.");
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database schema inspection failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
        }
    }

    public static async Task<bool> VerifyRequiredSchemaAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string expectedMigration,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSchemaDeploymentGate");
        var provider = db.Database.ProviderName ?? "Unknown";
        var deployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"] ?? "Unknown";
        var applicationVersion = GetApplicationVersion();
        var referenceId = Guid.NewGuid().ToString("N")[..8];

        if (!string.Equals(expectedMigration, ExpectedSchemaMigration, StringComparison.Ordinal))
        {
            logger.LogError(
                "Database deployment gate rejected an unknown expected migration. Reference {ReferenceId}; requested migration {RequestedMigration}; supported migration {ExpectedMigration}.",
                referenceId,
                expectedMigration,
                ExpectedSchemaMigration);
            return false;
        }

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogError(
                    "Database deployment gate failed. Reference {ReferenceId}; category {Category}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; operator action {OperatorAction}.",
                    referenceId,
                    DatabaseFailureCategory.ConnectionUnavailable,
                    provider,
                    environment.EnvironmentName,
                    applicationVersion,
                    deployedCommit,
                    expectedMigration,
                    "Restore database connectivity before retrying the deployment. No schema changes were attempted.");
                return false;
            }

            var missing = await FindMissingSchemaObjectsAsync(db, provider, cancellationToken);
            if (missing.Count == 0)
            {
                logger.LogInformation(
                    "Database deployment gate passed. Reference {ReferenceId}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; checked object count {CheckedObjectCount}.",
                    referenceId,
                    provider,
                    environment.EnvironmentName,
                    applicationVersion,
                    deployedCommit,
                    expectedMigration,
                    RequiredObjectCount);
                return true;
            }

            logger.LogError(
                "Database deployment gate blocked activation. Reference {ReferenceId}; category {Category}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; partially updated {PartiallyUpdated}; missing objects {MissingObjects}; operator action {OperatorAction}. No schema changes were attempted.",
                referenceId,
                DatabaseFailureCategory.SchemaMismatch,
                provider,
                environment.EnvironmentName,
                applicationVersion,
                deployedCommit,
                expectedMigration,
                missing.Count < RequiredObjectCount,
                string.Join(", ", missing),
                "Keep the prior compatible deployment active. Run the reviewed preflight and apply scripts only after a verified backup and explicit production authorization, then run verification and retry the deployment.");
            return false;
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database deployment gate failed. Reference {ReferenceId}; category {Category}; provider {Provider}; provider code {ProviderCode}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; operator action {OperatorAction}.",
                referenceId,
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None",
                environment.EnvironmentName,
                applicationVersion,
                deployedCommit,
                expectedMigration,
                "Review the safe server log and correct connectivity or schema state before retrying. No schema changes were attempted.");
            return false;
        }
    }

    private static async Task<IReadOnlyList<string>> FindMissingSchemaObjectsAsync(
        CropQcDbContext db,
        string provider,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var missing = new List<string>();
            foreach (var expectation in RequiredSchemaExpectations)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = SchemaExistsSql(
                    provider,
                    expectation.ColumnName is not null,
                    expectation.RequireNullable,
                    expectation.RequireNotNullable);

                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "tableName";
                tableParameter.Value = expectation.TableName;
                command.Parameters.Add(tableParameter);

                if (expectation.ColumnName is not null)
                {
                    var columnParameter = command.CreateParameter();
                    columnParameter.ParameterName = "columnName";
                    columnParameter.Value = expectation.ColumnName;
                    command.Parameters.Add(columnParameter);
                }

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (!Convert.ToBoolean(result))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            foreach (var expectation in RequiredIndexExpectations)
            {
                if (!await NamedObjectExistsAsync(
                    connection,
                    provider,
                    expectation,
                    SchemaNamedObjectKind.Index,
                    cancellationToken))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            foreach (var expectation in RequiredForeignKeyExpectations)
            {
                if (!await NamedObjectExistsAsync(
                    connection,
                    provider,
                    expectation,
                    SchemaNamedObjectKind.ForeignKey,
                    cancellationToken))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            foreach (var expectation in RequiredPrimaryKeyExpectations)
            {
                if (!await NamedObjectExistsAsync(
                    connection,
                    provider,
                    expectation,
                    SchemaNamedObjectKind.PrimaryKey,
                    cancellationToken))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            return missing;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string GetApplicationVersion() =>
        typeof(DatabaseStartupDiagnostics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(DatabaseStartupDiagnostics).Assembly.GetName().Version?.ToString()
        ?? "Unknown";

    private static int RequiredObjectCount =>
        RequiredSchemaExpectations.Length
        + RequiredIndexExpectations.Length
        + RequiredForeignKeyExpectations.Length
        + RequiredPrimaryKeyExpectations.Length;

    private static async Task<bool> NamedObjectExistsAsync(
        System.Data.Common.DbConnection connection,
        string provider,
        SchemaNamedObjectExpectation expectation,
        SchemaNamedObjectKind kind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NamedObjectExistsSql(provider, kind);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "tableName";
        tableParameter.Value = expectation.TableName;
        command.Parameters.Add(tableParameter);

        var objectParameter = command.CreateParameter();
        objectParameter.ParameterName = "objectName";
        objectParameter.Value = expectation.ObjectName;
        command.Parameters.Add(objectParameter);

        var uniqueParameter = command.CreateParameter();
        uniqueParameter.ParameterName = "requireUnique";
        uniqueParameter.Value = expectation.RequireUnique;
        command.Parameters.Add(uniqueParameter);

        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string SchemaExistsSql(
        string provider,
        bool column,
        bool requireNullable,
        bool requireNotNullable)
    {
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            if (column && requireNullable)
            {
                return "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName AND is_nullable = 'YES');";
            }
            if (column && requireNotNullable)
            {
                return "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName AND is_nullable = 'NO');";
            }

            return column
                ? "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName);"
                : "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @tableName);";
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            if (column && requireNullable)
            {
                return "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@tableName) AND name = @columnName AND is_nullable = 1) THEN 1 ELSE 0 END);";
            }
            if (column && requireNotNullable)
            {
                return "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@tableName) AND name = @columnName AND is_nullable = 0) THEN 1 ELSE 0 END);";
            }

            return column
                ? "SELECT CONVERT(bit, CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END);"
                : "SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END);";
        }

        throw new InvalidOperationException($"Unsupported database provider '{provider}' for schema diagnostics.");
    }

    private static string NamedObjectExistsSql(string provider, SchemaNamedObjectKind kind)
    {
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return kind switch
            {
                SchemaNamedObjectKind.ForeignKey => "SELECT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = current_schema() AND t.relname = @tableName AND c.conname = left(@objectName, 63) AND c.contype = 'f');",
                SchemaNamedObjectKind.PrimaryKey => "SELECT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = current_schema() AND t.relname = @tableName AND c.conname = left(@objectName, 63) AND c.contype = 'p');",
                _ => "SELECT EXISTS (SELECT 1 FROM pg_class i JOIN pg_index ix ON ix.indexrelid = i.oid JOIN pg_class t ON t.oid = ix.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = current_schema() AND t.relname = @tableName AND i.relname = left(@objectName, 63) AND (NOT @requireUnique OR ix.indisunique));"
            };
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return kind switch
            {
                SchemaNamedObjectKind.ForeignKey => "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@tableName) AND name = @objectName) THEN 1 ELSE 0 END);",
                SchemaNamedObjectKind.PrimaryKey => "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(@tableName) AND name = @objectName AND type = 'PK') THEN 1 ELSE 0 END);",
                _ => "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@tableName) AND name = @objectName AND (@requireUnique = 0 OR is_unique = 1)) THEN 1 ELSE 0 END);"
            };
        }

        throw new InvalidOperationException($"Unsupported database provider '{provider}' for schema diagnostics.");
    }

    private sealed record SchemaExpectation(
        string DisplayName,
        string TableName,
        string? ColumnName,
        bool RequireNullable = false,
        bool RequireNotNullable = false);

    private sealed record SchemaNamedObjectExpectation(
        string DisplayName,
        string TableName,
        string ObjectName,
        bool RequireUnique = false);

    private enum SchemaNamedObjectKind
    {
        Index,
        ForeignKey,
        PrimaryKey
    }
}
