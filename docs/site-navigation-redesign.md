# Crop QC site navigation redesign

## Old navigation audit

The application previously exposed global destinations through several independent surfaces:

| Surface | Classification | Result |
| --- | --- | --- |
| `_Layout.cshtml` direct links and Rooms, Growers, and Admin dropdowns | Module/global navigation | Replaced by the central catalog. |
| Bins Run `operation-tabs` (Planner, Actual, Transfer, True Up, Activity, Processor Shipments, Run Totals, Needs Review) | Module/global navigation | Moved to Runs, Transfers, Shipments, and Growers & Reports. |
| Processor Shipments `operation-tabs` back to run/transfer tools | Duplicate module/global navigation | Removed. |
| Grower & Lot Progress report tabs and header links | Duplicate module/global navigation | Moved to Growers & Reports. |
| `_MasterDataNavigation.cshtml` on Master Data, Grower Lots, End-of-Day groups, QC Recipients, and recipient import/review pages | Module/global navigation | Moved to the flat Admin panel and removed. |
| Inventory by Variety hand-built breadcrumb | Contextual orientation | Superseded by the shared breadcrumb metadata. |
| Receiving receipt-type tabs | Page filter/view control | Retained. |
| Bins Run planning facility, projection visibility/sort, calendar/date shortcuts | Page filter/view controls | Retained. |
| User Administration role selector and comparison controls | Page filter/view controls | Retained. |
| Pagination in Grower & Lot Progress and supporting run evidence | Contextual detail navigation | Retained. |
| Receipt, room, Actual Run, projection, Packout, shipment, and Field Sample detail links | Contextual detail navigation | Retained. |
| Save, Add, Upload, Transfer, True Up, Finalize, Review, Edit, Remove, Reverse, and Download controls | Business actions | Retained locally. |

No additional site-navigation strip was found in Dashboard, Receiving, Field Samples, Rooms, Room Inventory, End-of-Day Fill, Backups, Downloads, Configuration, Commercial Packs, QC Stations, Crop Year Review, or Data Cleanup.

## Final information architecture

Facility behavior is `Preserve` only where the existing destination accepts the global `Facility` context. `Local` means the destination retains its own filters or does not accept that global parameter.

| Category | Destination | Route | Minimum access / special rule | Facility |
| --- | --- | --- | --- | --- |
| Dashboard | Dashboard Home | `/` | Dashboard View | Preserve |
| Dashboard | Inventory by Variety | `/Inventory/ByVariety` | Dashboard View | Preserve |
| QC | Field Samples | `/FieldSamples` | Field Samples View | Local |
| QC | Receipt QC | `/DailyQc` | Receipt QC View | Preserve |
| Inventory | Current Room Inventory | `/Admin/RoomInventory` | Current Lots View | Local |
| Inventory | Inventory Reconciliation | `/Admin/RoomInventory/Reconciliation` | Current Lots Admin | Local |
| Receiving | Receipts | `/Receipts` | Receipts View | Preserve |
| Receiving | Voided Receipt Administration | `/Receipts/Admin/Voided` | Receipt Delete Admin | Local |
| Rooms | Room Overview | `/Rooms` | Rooms View | Preserve |
| Rooms | End of Day Fill | `/EndOfDayFill` | Active End-of-Day assignment | Local |
| Runs | Run Planner | `/BinsRun?Section=Planner` | Projection Planner View | Preserve |
| Runs | Actual Runs | `/BinsRun?Section=Actual` | Actual Runs View | Preserve |
| Runs | Recent Activity | `/BinsRun?Section=Activity` | Bins Run View | Preserve |
| Transfers | Room Transfers | `/BinsRun?Section=Transfer` | Bins Run View; action permissions remain authoritative | Preserve |
| Transfers | True Up | `/BinsRun?Section=TrueUp` | True Up Admin | Preserve |
| Shipments | Processor Shipments | `/ProcessorShipments` | Processor Shipments View | Local |
| Growers & Reports | Grower Lots | `/GrowerLots/Current` | Grower Lots View | Preserve |
| Growers & Reports | Grower & Lot Progress | `/RunReporting/Growers?Facility=All` | Bins Run View | Local |
| Growers & Reports | Run Totals | `/BinsRun?Section=RunTotals&ReportFacility=WP` | Bins Run View | Preserve |
| Growers & Reports | Needs Review | `/BinsRun?Section=NeedsReview` | Bins Run Edit | Local |
| Admin / Access & Devices | Users | `/Admin/Users` | Users Admin | Local |
| Admin / Access & Devices | QC Stations | `/Admin/QcStations` | QC Stations View | Local |
| Admin / Master Data | Master Data Home | `/MasterData` | Master Data View | Local |
| Admin / Master Data | Fruit Profiles / Varieties | `/MasterData/fruit-profiles` | Varieties View | Local |
| Admin / Master Data | Growers | `/MasterData/canonical-growers` | Master Data View | Local |
| Admin / Master Data | Orchards / Blocks | `/MasterData/orchard-blocks` | Master Data View | Local |
| Admin / Master Data | End of Day Fill Groups | `/MasterData/end-of-day-fill-groups` | Master Data Admin | Local |
| Admin / Master Data | Treatment Chemicals | `/MasterData/treatment-chemicals` | Master Data View | Local |
| Admin / Master Data | QC Recipients | `/Admin/OrchardRecipients` | Orchard Managers View | Local |
| Admin / Master Data | Manager Import | `/Admin/OrchardRecipientImports` | Import Tools Admin | Local |
| Admin / Master Data | Unmatched Identities | `/Admin/OrchardRecipientImports#recent-review-batches` | Import Tools Admin | Local |
| Admin / Master Data | Commercial Packs | `/Admin/CommercialPacks` | Master Data Admin | Local |
| Admin / Master Data | Variety Colors | `/Admin/VarietyColors` | Variety Colors View | Local |
| Admin / System | Configuration | `/Admin/Configuration` | Email Configuration Admin | Local |
| Admin / System | Downloads | `/Admin/Downloads` | Downloads View | Local |
| Admin / System | Backups | `/Admin/Backups` | Backup History View | Local |
| Admin / Data Maintenance | Crop Year Review | `/CropYearReview` | Crop Year Review View and owner-only controller rule | Local |
| Admin / Data Maintenance | Audit History | `/MasterData/audit-logs` | Audit History View | Local |
| Admin / Data Maintenance | Data Cleanup | `/Admin/DataCleanup` | Data Cleanup Admin | Local |

`EBS Historical Cleanup` remains available only through its protected direct route and is intentionally absent from normal navigation.

## Shared behavior

- `SiteNavigationCatalog` is the single code-defined source for category, label, URL, permission, ordering, facility behavior, active-route matches, and breadcrumb hierarchy.
- Desktop and mobile render the same filtered model. A category with zero visible destinations is omitted.
- Controller authorization remains authoritative; menu filtering is only the discovery layer.
- The active matcher considers route descendants and Bins Run `Section`, so Actual Run/Packout details, receipt details, room details, reports, and Admin descendants keep the correct top-level highlight.
- The facility context bar remains directly below the navy header. It preserves the current query while replacing only `Facility`.
- The shared breadcrumb follows the facility bar, uses linked parents, marks the current item with `aria-current="page"`, and wraps on compact screens.
- Admin is one flat panel with non-clickable group headings; there are no cascading or nested submenus.

## Contextual navigation intentionally retained

- Lists to their receipt, room, sample, Actual Run, projection, Packout, shipment, or report evidence details.
- Back, Details, Edit, Review, Open, Download, and pagination links within a workflow.
- Local facility/date/status/sort/type selectors that change the current page rather than navigate between modules.
- Business actions and confirmation flows, including transfer, true up, upload, finalize, reverse, dismissal, and cleanup controls.

## Data and deployment impact

- Database migration: none.
- Permission matrix changes: none.
- Operational data changes: none.
- Production deployment/configuration changes during development: none.

## Responsive browser evidence

All screenshots use an authenticated owner/admin account against a disposable in-memory local application host. No production endpoint or data was used.

| View | Evidence |
| --- | --- |
| Desktop Dashboard | [1440 px Dashboard](navigation-screenshots/desktop-dashboard-1440.png) |
| Desktop Runs | [1440 px Runs dropdown](navigation-screenshots/desktop-runs-1440.png) |
| Desktop Admin | [1440 px grouped Admin panel](navigation-screenshots/desktop-admin-1440.png) |
| Desktop Actual Run | [1440 px Actual Run breadcrumb](navigation-screenshots/desktop-actual-run-1440.png) |
| Desktop Receiving | [1440 px Receiving](navigation-screenshots/desktop-receiving-1440.png) |
| Desktop Rooms | [1440 px Rooms](navigation-screenshots/desktop-rooms-1440.png) |
| Tablet | [1024 px grouped Admin panel](navigation-screenshots/tablet-admin-1024.png) and [768 px Receiving](navigation-screenshots/tablet-receiving-768.png) |
| Mobile closed | [430 px closed menu](navigation-screenshots/mobile-dashboard-closed-430.png) |
| Mobile Runs | [430 px Runs accordion](navigation-screenshots/mobile-runs-430.png) |
| Mobile Admin | [390 px Admin accordion](navigation-screenshots/mobile-admin-390.png) |
| Mobile detail | [390 px Actual Run breadcrumb](navigation-screenshots/mobile-actual-run-breadcrumb-390.png) |

The tested viewport widths were 1440, 1024, 768, 430, and 390 pixels. Each had zero horizontal document overflow. Dropdown/accordion panels remained within the viewport, account controls did not overlap navigation, facility context remained directly below the header, and active category/item styling remained visible. Browser interaction checks also covered exclusive category expansion, outside-click close, Escape close with sensible focus restoration, and an error-free browser console.
