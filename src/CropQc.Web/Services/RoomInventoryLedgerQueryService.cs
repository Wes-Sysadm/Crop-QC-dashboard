using CropQc.Data;

namespace CropQc.Web.Services;

// Compatibility facade: operational callers keep the same ledger and no
// canonical eligibility resolver is inserted into any write path in Phase 1.
public interface IRoomInventoryLedgerQueryService : CropQc.Data.Inventory.IRoomInventoryLedgerQueryService;

public sealed class RoomInventoryLedgerQueryService(CropQcDbContext dbContext)
    : CropQc.Data.Inventory.RoomInventoryLedgerQueryService(dbContext), IRoomInventoryLedgerQueryService;
