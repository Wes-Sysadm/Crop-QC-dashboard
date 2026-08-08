using System.Data;
using System.Reflection;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class DatabaseStartupDiagnostics
{
    public const string ExpectedSchemaMigration = "20260807210820_AddRoleBasedUserAccess";

    private static readonly SchemaExpectation[] RequiredSchemaExpectations =
    [
        new("CanonicalOrchards", "CanonicalOrchards", null),
        new("OrchardReportRecipients", "OrchardReportRecipients", null),
        new("Receipts.CanonicalOrchardBlockId", "Receipts", "CanonicalOrchardBlockId"),
        new("CanonicalOrchardBlocks.CanonicalOrchardId", "CanonicalOrchardBlocks", "CanonicalOrchardId"),
        new("PackCodeDefinitions", "PackCodeDefinitions", null),
        new("PackoutAnalysisConfigurations", "PackoutAnalysisConfigurations", null),
        new("PackoutRuns", "PackoutRuns", null),
        new("PackoutEmailAttempts", "PackoutEmailAttempts", null),
        new("PackoutReportSources", "PackoutReportSources", null),
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
        new("RolePageAccesses.UpdatedAt", "RolePageAccesses", "UpdatedAt", RequireNotNullable: true)
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredIndexExpectations =
    [
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
        new("IX_Rooms_EndOfDayFillReportGroupId", "Rooms", "IX_Rooms_EndOfDayFillReportGroupId"),
        new("IX_EndOfDayFillReportRecipients_NormalizedEmailAddress", "EndOfDayFillReportRecipients", "IX_EndOfDayFillReportRecipients_NormalizedEmailAddress", RequireUnique: true),
        new("IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId", "EndOfDayFillUserGroupAssignments", "IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId", RequireUnique: true),
        new("IX_EndOfDayFillReportSends_SuccessRevisionKey", "EndOfDayFillReportSends", "IX_EndOfDayFillReportSends_SuccessRevisionKey", RequireUnique: true),
        new("IX_EndOfDayFillSendReservations_SendAttemptId", "EndOfDayFillSendReservations", "IX_EndOfDayFillSendReservations_SendAttemptId", RequireUnique: true),
        new("IX_Roles_NormalizedName", "Roles", "IX_Roles_NormalizedName", RequireUnique: true),
        new("IX_UserRoles_UserId", "UserRoles", "IX_UserRoles_UserId", RequireUnique: true),
        new("IX_RolePageAccesses_RoleId_AreaKey", "RolePageAccesses", "IX_RolePageAccesses_RoleId_AreaKey", RequireUnique: true),
        new("IX_RolePageAccesses_UpdatedByUserId", "RolePageAccesses", "IX_RolePageAccesses_UpdatedByUserId")
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredForeignKeyExpectations =
    [
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
        new("FK_EndOfDayFillUserGroupAssignments_Users_UserId", "EndOfDayFillUserGroupAssignments", "FK_EndOfDayFillUserGroupAssignments_Users_UserId"),
        new("FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId", "EndOfDayFillReportSends", "FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId"),
        new("FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId", "EndOfDayFillSendReservations", "FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId"),
        new("FK_RolePageAccesses_Roles_RoleId", "RolePageAccesses", "FK_RolePageAccesses_Roles_RoleId"),
        new("FK_RolePageAccesses_Users_UpdatedByUserId", "RolePageAccesses", "FK_RolePageAccesses_Users_UpdatedByUserId")
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredPrimaryKeyExpectations =
    [
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
        new("PK_RolePageAccesses", "RolePageAccesses", "PK_RolePageAccesses")
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
