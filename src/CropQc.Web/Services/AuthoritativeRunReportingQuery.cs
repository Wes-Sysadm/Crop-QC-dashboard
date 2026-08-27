using CropQc.Data.Entities;

namespace CropQc.Web.Services;

/// <summary>
/// Shared fail-closed rules for run quantities that may enter authoritative reporting.
/// Report-specific date, facility, and search predicates are applied by each caller.
/// </summary>
public static class AuthoritativeRunReportingQuery
{
    public static IQueryable<BinsRunEntry> ApplyActiveQuantityRules(IQueryable<BinsRunEntry> query) =>
        query.Where(x =>
            (x.TransactionType == ActualRunTransactionTypes.Depletion
                && x.ActualRunId != null
                && x.ActualRun != null
                && x.ActualRun.Status == ActualRunStatuses.Active
                && x.ActualRunRevisionId != null
                && x.ActualRunRevision != null
                && x.ActualRunRevision.IsCurrent
                && !x.IsReversed)
            || (x.TransactionType == ActualRunTransactionTypes.Legacy
                && x.ActualRunId == null
                && !x.IsReversed));

    public static IQueryable<BinsRunEntry> ApplyWpEbsRunFacilityRules(IQueryable<BinsRunEntry> query) =>
        query.Where(x => x.ActualRunId != null
            ? x.ActualRun!.RunFacilityCodeSnapshot == EmploymentFacilities.Wp
                || x.ActualRun.RunFacilityCodeSnapshot == EmploymentFacilities.Ebs
            : x.ReportingFacilityCodeSnapshot == EmploymentFacilities.Wp
                || x.ReportingFacilityCodeSnapshot == EmploymentFacilities.Ebs);

    public static IQueryable<BinsRunEntry> ApplyCompleteGrowerLotIdentityRules(IQueryable<BinsRunEntry> query) =>
        query.Where(x => x.ReportingFruitProfileIdSnapshot != null
            && x.ReportingVarietyCodeSnapshot != null && x.ReportingVarietyCodeSnapshot != ""
            && x.ProductionTypeSnapshot != null && x.ProductionTypeSnapshot != ""
            && x.IsOrganicSnapshot != null
            && x.GrowerNumberSnapshot != null && x.GrowerNumberSnapshot != ""
            && x.LotNumber != ""
            && x.Warehouse.Code != "");

    public static IQueryable<BinsRunEntry> ApplyIncompleteGrowerLotIdentityRules(IQueryable<BinsRunEntry> query) =>
        query.Where(x => x.ReportingFruitProfileIdSnapshot == null
            || x.ReportingVarietyCodeSnapshot == null || x.ReportingVarietyCodeSnapshot == ""
            || x.ProductionTypeSnapshot == null || x.ProductionTypeSnapshot == ""
            || x.IsOrganicSnapshot == null
            || x.GrowerNumberSnapshot == null || x.GrowerNumberSnapshot == ""
            || x.LotNumber == ""
            || x.Warehouse.Code == "");

    public static IQueryable<BinsRunEntry> ApplyValidRules(IQueryable<BinsRunEntry> query) =>
        ApplyCompleteGrowerLotIdentityRules(
            ApplyWpEbsRunFacilityRules(
                ApplyActiveQuantityRules(query)));

    public static string RunFacility(BinsRunEntry line) => line.ActualRunId != null
        ? line.ActualRun!.RunFacilityCodeSnapshot!
        : line.ReportingFacilityCodeSnapshot!;
}
