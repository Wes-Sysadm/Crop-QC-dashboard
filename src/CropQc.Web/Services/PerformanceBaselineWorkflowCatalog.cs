namespace CropQc.Web.Services;

public sealed record PerformanceBaselineWorkflow(
    int Order,
    string Name,
    string Method,
    string RouteTemplate,
    string Kind,
    string ExpectedScaleSignal,
    string Notes);

public static class PerformanceBaselineWorkflowCatalog
{
    public static IReadOnlyList<PerformanceBaselineWorkflow> Workflows { get; } =
    [
        new(1, "Dashboard initial load", "GET", "/", "Page", "occupied room cards", "Warm once, then capture the dashboard landing page."),
        new(2, "Dashboard room-summary data", "GET", "/Dashboard/Rooms/{roomId}/Summary", "Page/API", "active lots in selected room", "Use a room with multiple current lots."),
        new(3, "Room detail open", "GET", "/Dashboard/Rooms/{roomId}", "Page", "room lots and sample history", "Open a room with sizing, grade, and warning data."),
        new(4, "Room projection update", "POST", "/Dashboard/Rooms/{roomId}/Projection", "API", "selected lots", "Select one lot, then multiple lots."),
        new(5, "Bins Run initial load", "GET", "/BinsRun", "Page", "occupied rooms", "Open as a user with Bins Run view permission."),
        new(6, "Bins Run room selection", "GET", "/BinsRun?WarehouseId={warehouseId}&RoomId={roomId}", "Page", "active lots in selected room", "Use a room with several active lots."),
        new(7, "Bins Run selected-lot projection", "POST", "/BinsRun/Projection", "API", "selected lots", "Capture one selected lot and multiple selected lots."),
        new(8, "Daily QC", "GET", "/DailyQc", "Page", "samples for UTC day", "Use a day with multiple sample types."),
        new(9, "Ready-to-Email", "GET", "/ReadyToEmail", "Page", "ready samples", "Capture with sent and unsent samples present."),
        new(10, "Receipts list", "GET", "/Receipts", "Page", "receipt rows", "Capture filtered and unfiltered lists."),
        new(11, "Receipt detail", "GET", "/Receipts/Details/{receiptId}", "Page", "samples and photos for receipt", "Use a receipt with photos and QC samples."),
        new(12, "QC sample detail", "GET", "/Samples/Details/{sampleId}", "Page", "fruit rows", "Use 10, 25, and 50 fruit samples across runs."),
        new(13, "Crop Year Review initial card list", "GET", "/CropYearReview?cropYear={cropYear}", "Page", "canonical grower cards", "Use mapped and unmapped growers."),
        new(14, "Crop Year Review grower detail", "GET", "/CropYearReview/Grower/{growerKey}", "Page/API", "receipts and lots for grower", "Open mapped and unmapped identities."),
        new(15, "Master Data varieties", "GET", "/MasterData#varieties", "Page", "known varieties", "Capture configured and fallback variety colors."),
        new(16, "Master Data growers", "GET", "/MasterData#growers", "Page", "canonical growers and source identities", "Capture mapped and unmapped grower sections."),
        new(17, "Permissions matrix", "GET", "/Users", "Page", "users and access rows", "Open as an admin user."),
        new(18, "Audit history", "GET", "/Admin/Audit", "Page", "audit rows", "Use default page size and any available filters."),
        new(19, "Photo metadata section opening", "GET", "/Receipts/Details/{receiptId}#photos", "Page", "photo metadata rows", "Use a receipt with hosted Google Drive photo metadata.")
    ];
}
