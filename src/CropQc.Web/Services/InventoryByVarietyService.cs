using CropQc.Data;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IInventoryByVarietyService
{
    Task<InventoryByVarietyPageViewModel> GetSummaryAsync(string? facility, CancellationToken cancellationToken);
    Task<InventoryVarietyDetailPageViewModel?> GetDetailAsync(string varietyKey, string? facility, CancellationToken cancellationToken);
}

public sealed class InventoryByVarietyService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IRoomTreatmentService roomTreatments,
    IVarietyColorService varietyColors,
    IFacilityContextService facilityContext) : IInventoryByVarietyService
{
    public async Task<InventoryByVarietyPageViewModel> GetSummaryAsync(
        string? facility,
        CancellationToken cancellationToken)
    {
        var normalizedFacility = facilityContext.Normalize(facility);
        var snapshots = await GetCurrentSnapshotsAsync(normalizedFacility, cancellationToken);
        var identities = snapshots
            .Select(CanonicalVariety)
            .DistinctBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var colors = await varietyColors.GetResolvedColorsReadOnlyAsync(
            identities.Select(x => x.Key), cancellationToken);

        var cards = snapshots
            .GroupBy(x => CanonicalVariety(x).Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var identity = CanonicalVariety(group.First());
                var resolved = colors.GetValueOrDefault(identity.Key);
                var breakdowns = group
                    .GroupBy(x => new { ProductionType = Production(x.ProductionType), OrganicStatus = Organic(x.IsOrganic) })
                    .Select(x => new InventoryVarietyBreakdownViewModel(
                        x.Key.ProductionType,
                        x.Key.OrganicStatus,
                        x.Sum(y => y.CurrentBins)))
                    .OrderBy(x => x.ProductionType)
                    .ThenBy(x => x.OrganicStatus)
                    .ToList();
                return new InventoryVarietyCardViewModel(
                    identity.Key,
                    resolved?.VarietyName ?? identity.Name,
                    resolved?.HexColor ?? VarietyColorService.FallbackColor(identity.Key),
                    group.Sum(x => x.CurrentBins),
                    group.Select(x => x.RoomId).Distinct().Count(),
                    group.Select(x => x.GrowerNumber?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    breakdowns);
            })
            .OrderBy(x => x.VarietyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.VarietyKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryByVarietyPageViewModel
        {
            Facility = normalizedFacility,
            FacilityOptions = facilityContext.SelectableFacilities,
            Varieties = cards
        };
    }

    public async Task<InventoryVarietyDetailPageViewModel?> GetDetailAsync(
        string varietyKey,
        string? facility,
        CancellationToken cancellationToken)
    {
        var normalizedFacility = facilityContext.Normalize(facility);
        var normalizedVarietyKey = VarietyColorService.NormalizeVarietyKey(varietyKey);
        var snapshots = (await GetCurrentSnapshotsAsync(normalizedFacility, cancellationToken))
            .Where(x => string.Equals(CanonicalVariety(x).Key, normalizedVarietyKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (snapshots.Count == 0)
        {
            return null;
        }

        var identity = CanonicalVariety(snapshots[0]);
        var colors = await varietyColors.GetResolvedColorsReadOnlyAsync([identity.Key], cancellationToken);
        var color = colors.GetValueOrDefault(identity.Key);
        var treatmentSelections = await roomTreatments.GetSelectionsAsync(snapshots, cancellationToken);
        var latestAdjustmentIds = snapshots.Select(x => x.LatestAdjustmentId).Distinct().ToList();
        var receiptEvidence = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => latestAdjustmentIds.Contains(x.Id) && x.ReceiptId != null)
            .Select(x => new { x.Id, x.ReceiptId, ReceiptNumber = x.Receipt!.CompuTechReceiptId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var lines = snapshots.Select(snapshot =>
        {
            receiptEvidence.TryGetValue(snapshot.LatestAdjustmentId, out var receipt);
            var selectionKey = RoomTreatmentService.SelectionLookupKey(snapshot);
            var segments = treatmentSelections.GetValueOrDefault(selectionKey) ?? [];
            var treatment = segments.Count == 0
                ? "Untreated"
                : string.Join("; ", segments
                    .OrderBy(x => x.TreatmentState)
                    .ThenBy(x => x.Label)
                    .Select(x => $"{x.Label}: {x.CurrentBins:N0} bins"));
            return new InventoryVarietyDetailLineViewModel(
                snapshot.WarehouseId,
                snapshot.Facility,
                snapshot.RoomId,
                snapshot.Room,
                snapshot.GrowerNumber,
                snapshot.Grower,
                snapshot.GrowerLotId,
                receipt?.ReceiptId,
                receipt?.ReceiptNumber,
                snapshot.SourceReference,
                Production(snapshot.ProductionType),
                Organic(snapshot.IsOrganic),
                string.IsNullOrWhiteSpace(snapshot.InventoryStatus) ? "Current" : snapshot.InventoryStatus,
                treatment,
                snapshot.CurrentBins);
        })
        .OrderBy(x => x.Facility)
        .ThenBy(x => x.Room)
        .ThenBy(x => x.GrowerNumber)
        .ThenBy(x => x.ProductionType)
        .ThenBy(x => x.OrganicStatus)
        .ToList();

        return new InventoryVarietyDetailPageViewModel
        {
            Facility = normalizedFacility,
            VarietyKey = identity.Key,
            VarietyName = color?.VarietyName ?? identity.Name,
            HexColor = color?.HexColor ?? VarietyColorService.FallbackColor(identity.Key),
            Lines = lines
        };
    }

    private async Task<List<RoomInventoryLedgerSnapshot>> GetCurrentSnapshotsAsync(
        string facility,
        CancellationToken cancellationToken) =>
        (await ledger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0 && facilityContext.Matches(x.Facility, x.Facility, facility))
            .ToList();

    private static VarietyIdentity CanonicalVariety(RoomInventoryLedgerSnapshot snapshot) =>
        VarietyColorService.NormalizeIdentity(snapshot.VarietyName, snapshot.Variety);

    private static string Production(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Production type unavailable" : value.Trim();

    private static string Organic(bool? value) => value switch
    {
        true => "Organic",
        false => "Conventional",
        _ => "Organic status unavailable"
    };
}
